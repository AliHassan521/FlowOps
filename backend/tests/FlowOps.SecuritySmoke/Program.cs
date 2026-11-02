using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using FlowOps.Application.Common.Configuration;
using FlowOps.Application.Common.Models;
using FlowOps.Contracts.Auth;
using FlowOps.Domain.Entities;
using FlowOps.Infrastructure.Persistence;
using FlowOps.Infrastructure.Security;
using FlowOps.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

// Deliberately fixed to a local, disposable validation database. Never accepts
// production credentials or removes existing databases/data.
const string connection = @"Server=.\SQLEXPRESS;Database=FlowOpsSecurityValidation;Trusted_Connection=True;TrustServerCertificate=True";
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
var ct = timeout.Token;
var settings = Options.Create(new JwtSettings {
    SecretKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
    Issuer = "FlowOps.SecuritySmoke", Audience = "FlowOps.SecuritySmoke"
});
var hasher = new PasswordHasher();
var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var email = $"security-{Guid.NewGuid():N}@example.test";
FlowOpsDbContext Open(DbCommandInterceptor? interceptor = null)
{
    var options = new DbContextOptionsBuilder<FlowOpsDbContext>().UseSqlServer(connection);
    if (interceptor is not null) options.AddInterceptors(interceptor);
    return new FlowOpsDbContext(options.Options);
}
AuthService Auth(FlowOpsDbContext db) => new(db, hasher, new JwtTokenService(settings), settings);
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
async Task<bool> Rejected(string token)
{
    await using var db = Open();
    try { await Auth(db).RefreshAsync(token, ct); return false; }
    catch (UnauthorizedAccessException) { return true; }
}
async Task<AuthSession> Login()
{
    await using var db = Open();
    return await Auth(db).LoginAsync(new LoginRequest(email, password), ct);
}

await using (var db = Open())
{
    // On a fresh database, exercise an upgrade with real pre-existing tokens,
    // rather than testing only EnsureCreated against the newest model.
    var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
    if (applied.Length == 0)
    {
        await db.GetService<IMigrator>().MigrateAsync("20261102050713_AddSlaPoliciesAndMeasurements", ct);
        var legacyToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var legacyUser = new User { Email = $"legacy-{Guid.NewGuid():N}@example.test", FullName = "Migration fixture", PasswordHash = hasher.HashPassword(password) };
        var role = await db.Roles.SingleAsync(r => r.Name == "Engineer", ct);
        legacyUser.UserRoles.Add(new UserRole { UserId = legacyUser.Id, RoleId = role.Id });
        db.Users.Add(legacyUser);
        db.RefreshTokens.Add(new RefreshToken { UserId = legacyUser.Id, FamilyId = Guid.NewGuid(),
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyToken))), ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync(ct);
        await db.Database.MigrateAsync(ct);
        var migrated = await Auth(db).RefreshAsync(legacyToken, ct);
        await Auth(db).RevokeRefreshTokenAsync(migrated.RefreshToken, ct);
        Console.WriteLine("PASS: migration backfills a live legacy family and preserves refresh/logout.");
    }
    else await db.Database.MigrateAsync(ct);
    await Auth(db).RegisterAsync(new RegisterRequest(email, "Concurrency fixture", password), ct);
}

for (var iteration = 0; iteration < 10; iteration++)
{
    var original = await Login();
    var gate = new ReadGate();
    await using var first = Open(gate);
    await using var second = Open(gate);
    async Task<AuthSession?> Rotate(FlowOpsDbContext db)
    {
        try { return await Auth(db).RefreshAsync(original.RefreshToken, ct); }
        catch (UnauthorizedAccessException) { return null; }
    }
    var results = await Task.WhenAll(Rotate(first), Rotate(second));
    Require(results.Count(result => result is not null) == 1, "Exactly one concurrent rotation must succeed.");
    Require(await Rejected(results.Single(result => result is not null)!.RefreshToken), "Replay must invalidate even a concurrently inserted descendant.");

    var logoutSession = await Login();
    gate = new ReadGate();
    await using var refreshDb = Open(gate);
    await using var logoutDb = Open(gate);
    async Task<AuthSession?> RefreshDuringLogout()
    {
        try { return await Auth(refreshDb).RefreshAsync(logoutSession.RefreshToken, ct); }
        catch (UnauthorizedAccessException) { return null; }
    }
    var refresh = RefreshDuringLogout();
    await Task.WhenAll(refresh, Auth(logoutDb).RevokeRefreshTokenAsync(logoutSession.RefreshToken, ct));
    if (await refresh is { } replacement)
        Require(await Rejected(replacement.RefreshToken), "Logout must invalidate a racing replacement.");
    Require(await Rejected(logoutSession.RefreshToken), "Logout must invalidate the original token.");
}
var independent = await Login();
Require(!await Rejected(independent.RefreshToken), "Other login families must remain usable.");
await using (var db = Open())
{
    // Keep migration/audit evidence, but leave no usable synthetic account.
    var user = await db.Users.SingleAsync(user => user.Email == email, ct);
    user.IsActive = false; await db.SaveChangesAsync(ct);
}
Console.WriteLine("PASS: 10 synchronized refresh/replay races and 10 synchronized refresh/logout races; independent family remains usable.");

sealed class ReadGate : DbCommandInterceptor
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FROM [RefreshTokens]", StringComparison.Ordinal))
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        return result;
    }
}
