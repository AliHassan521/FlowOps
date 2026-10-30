using FlowOps.Application.Common.Configuration;
using FlowOps.Application.Common.Exceptions;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Auth;
using FlowOps.Infrastructure.Persistence;
using FlowOps.Infrastructure.Security;
using FlowOps.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowOps.Application.Tests;

public sealed class AuthenticationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FlowOpsDbContext _dbContext;
    private readonly AuthService _authService;

    public AuthenticationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<FlowOpsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FlowOpsDbContext(options);
        _dbContext.Database.EnsureCreated();

        var settings = Options.Create(new JwtSettings
        {
            SecretKey = "test-only-secret-key-with-at-least-32-characters",
            Issuer = "FlowOps.Tests",
            Audience = "FlowOps.Tests.Client",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 30
        });

        _authService = new AuthService(
            _dbContext,
            new PasswordHasher(),
            new TestJwtTokenService(),
            settings);
    }

    [Fact]
    public async Task Register_assigns_only_the_safe_default_role()
    {
        var session = await _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));

        Assert.Equal(["Engineer"], session.Response.Roles);
        Assert.NotEmpty(session.Response.AccessToken);
        Assert.NotEmpty(session.RefreshToken);
        Assert.Single(await _dbContext.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_email_returns_conflict()
    {
        var request = new RegisterRequest("person@example.com", "Test Person", "correct horse battery");
        await _authService.RegisterAsync(request);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _authService.RegisterAsync(request));

        Assert.Equal("An account with this email already exists.", exception.Message);
    }

    [Fact]
    public async Task Invalid_password_does_not_reveal_account_state()
    {
        await _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequest("person@example.com", "wrong password")));

        Assert.Equal("Invalid email or password.", exception.Message);
    }

    [Fact]
    public async Task Unknown_email_returns_the_same_authentication_error()
    {
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequest("missing@example.com", "wrong password")));

        Assert.Equal("Invalid email or password.", exception.Message);
    }

    [Fact]
    public async Task Inactive_account_cannot_log_in()
    {
        await _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));
        var user = await _dbContext.Users.SingleAsync();
        user.IsActive = false;
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequest("person@example.com", "correct horse battery")));
    }

    [Fact]
    public async Task Refresh_rotates_token_and_rejects_reuse_of_previous_token()
    {
        var firstSession = await _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));

        var secondSession = await _authService.RefreshAsync(firstSession.RefreshToken);

        Assert.NotEqual(firstSession.RefreshToken, secondSession.RefreshToken);
        Assert.Equal(2, await _dbContext.RefreshTokens.CountAsync());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshAsync(firstSession.RefreshToken));
    }

    [Fact]
    public async Task Refresh_tokens_are_persisted_as_hashes_only()
    {
        var session = await _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));
        var storedToken = await _dbContext.RefreshTokens.SingleAsync();

        Assert.NotEqual(session.RefreshToken, storedToken.TokenHash);
        Assert.Matches("^[A-F0-9]{64}$", storedToken.TokenHash);
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        var session = await _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));
        var storedToken = await _dbContext.RefreshTokens.SingleAsync();
        storedToken.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshAsync(session.RefreshToken));
    }

    [Fact]
    public async Task Inactive_account_cannot_refresh_a_session()
    {
        var session = await _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));
        var user = await _dbContext.Users.SingleAsync();
        user.IsActive = false;
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshAsync(session.RefreshToken));
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_active_token_in_its_family_only()
    {
        var firstSession = await _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));
        var secondSession = await _authService.LoginAsync(
            new LoginRequest("person@example.com", "correct horse battery"));
        var rotatedSession = await _authService.RefreshAsync(firstSession.RefreshToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshAsync(firstSession.RefreshToken));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshAsync(rotatedSession.RefreshToken));
        var stillValidSession = await _authService.RefreshAsync(secondSession.RefreshToken);
        Assert.NotEmpty(stillValidSession.Response.AccessToken);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var session = await _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));

        await _authService.RevokeRefreshTokenAsync(session.RefreshToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshAsync(session.RefreshToken));
    }

    [Fact]
    public async Task Short_password_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", "short")));

        Assert.Contains("12 and 1024 characters", exception.Message);
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("person@")]
    public async Task Registration_rejects_malformed_email(string email)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(
            new RegisterRequest(email, "Test Person", "correct horse battery")));
    }

    [Fact]
    public async Task Registration_rejects_passwords_above_the_size_limit()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(
            new RegisterRequest("person@example.com", "Test Person", new string('x', 1025))));

        Assert.Contains("1024 characters", exception.Message);
    }

    [Fact]
    public async Task Login_rejects_oversized_credentials_before_password_verification()
    {
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(
            new LoginRequest("person@example.com", new string('x', 1025))));

        Assert.Equal("Invalid email or password.", exception.Message);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private sealed class TestJwtTokenService : IJwtTokenService
    {
        public string GenerateToken(Guid userId, string email, IEnumerable<string> roles) =>
            $"test-token-{userId}-{email}-{string.Join(',', roles)}";
    }
}
