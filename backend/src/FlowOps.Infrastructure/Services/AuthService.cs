using System.Security.Cryptography;
using System.Text;
using System.ComponentModel.DataAnnotations;
using FlowOps.Application.Common.Configuration;
using FlowOps.Application.Common.Exceptions;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Application.Common.Models;
using FlowOps.Contracts.Auth;
using FlowOps.Domain.Entities;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace FlowOps.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private const string DefaultRoleName = "Engineer";
    private const int RefreshTokenBytes = 64;
    private const int MaximumEmailLength = 256;
    private const int MaximumPasswordLength = 1024;
    private const string DummyPasswordHash = "$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

    private readonly FlowOpsDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtSettings _jwtSettings;

    public AuthService(
        FlowOpsDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IOptions<JwtSettings> jwtOptions)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _jwtSettings = jwtOptions.Value;
    }

    public async Task<AuthSession> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRegistration(request);

        var normalizedEmail = NormalizeEmail(request.Email);
        if (await _dbContext.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var role = await _dbContext.Roles
            .SingleAsync(existingRole => existingRole.Name == DefaultRoleName, cancellationToken);

        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            IsActive = true,
            CreatedAt = now
        };

        user.UserRoles.Add(new UserRole
        {
            User = user,
            Role = role,
            AssignedAt = now
        });

        _dbContext.Users.Add(user);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            var session = await CreateSessionAsync(user, [role.Name], cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return session;
        }
        catch (DbUpdateException exception) when (
            IsUniqueConstraintViolation(exception)
            && exception.Entries.Any(entry => entry.Entity is User))
        {
            throw new ConflictException("An account with this email already exists.");
        }

    }

    public async Task<AuthSession> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email)
            || request.Email.Length > MaximumEmailLength
            || string.IsNullOrWhiteSpace(request.Password)
            || request.Password.Length > MaximumPasswordLength)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _dbContext.Users
            .Include(existingUser => existingUser.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(existingUser => existingUser.Email == normalizedEmail, cancellationToken);

        // Verify against a fixed dummy hash when the account does not exist so login
        // timing does not reveal whether an email is registered.
        var passwordHash = user?.PasswordHash ?? DummyPasswordHash;
        var passwordIsValid = _passwordHasher.VerifyPassword(request.Password, passwordHash);
        if (user is null || !user.IsActive || !passwordIsValid)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var roles = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roles.Length == 0)
        {
            throw new InvalidOperationException("The account has no assigned security role.");
        }

        return await CreateSessionAsync(user, roles, cancellationToken);
    }

    public async Task<AuthSession> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new UnauthorizedAccessException("Refresh token is invalid or expired.");
        }

        var tokenHash = HashRefreshToken(refreshToken);
        var storedToken = await _dbContext.RefreshTokens
            .AsNoTracking()
            .Include(token => token.User)
            .ThenInclude(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        var now = DateTime.UtcNow;
        if (storedToken is null)
        {
            throw new UnauthorizedAccessException("Refresh token is invalid or expired.");
        }

        if (storedToken.RevokedAtUtc.HasValue)
        {
            // A rotated token being presented again is evidence that a copied
            // refresh token may have been stolen. Revoke only this login family.
            await _dbContext.RefreshTokens
                .Where(token => token.FamilyId == storedToken.FamilyId && token.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAtUtc, now), cancellationToken);

            throw new UnauthorizedAccessException("Refresh token is invalid or expired.");
        }

        if (storedToken.ExpiresAtUtc <= now || !storedToken.User.IsActive)
        {
            throw new UnauthorizedAccessException("Refresh token is invalid or expired.");
        }

        var roles = storedToken.User.UserRoles
            .Select(userRole => userRole.Role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roles.Length == 0)
        {
            throw new InvalidOperationException("The account has no assigned security role.");
        }

        var replacementToken = GenerateRefreshToken();
        var replacementHash = HashRefreshToken(replacementToken);

        // The predicate makes rotation single-use even when two requests race.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var revokedCount = await _dbContext.RefreshTokens
            .Where(token => token.Id == storedToken.Id && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAtUtc, now)
                .SetProperty(token => token.ReplacedByTokenHash, replacementHash), cancellationToken);

        if (revokedCount != 1)
        {
            throw new UnauthorizedAccessException("Refresh token is invalid or expired.");
        }

        var replacement = new RefreshToken
        {
            UserId = storedToken.UserId,
            FamilyId = storedToken.FamilyId,
            TokenHash = replacementHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_jwtSettings.RefreshTokenDays)
        };

        _dbContext.RefreshTokens.Add(replacement);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateSessionResponse(storedToken.User, roles, replacementToken, replacement.ExpiresAtUtc);
    }

    public async Task RevokeRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var tokenHash = HashRefreshToken(refreshToken);
        await _dbContext.RefreshTokens
            .Where(token => token.TokenHash == tokenHash && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAtUtc, DateTime.UtcNow), cancellationToken);
    }

    private async Task<AuthSession> CreateSessionAsync(
        User user,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var refreshToken = GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var storedToken = new RefreshToken
        {
            UserId = user.Id,
            FamilyId = Guid.NewGuid(),
            TokenHash = HashRefreshToken(refreshToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_jwtSettings.RefreshTokenDays)
        };

        _dbContext.RefreshTokens.Add(storedToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreateSessionResponse(user, roles, refreshToken, storedToken.ExpiresAtUtc);
    }

    private AuthSession CreateSessionResponse(
        User user,
        IReadOnlyCollection<string> roles,
        string refreshToken,
        DateTime refreshTokenExpiresAtUtc)
    {
        var accessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenMinutes);
        var accessToken = _jwtTokenService.GenerateToken(user.Id, user.Email, roles);
        var response = new AuthResponse(
            user.Id,
            user.Email,
            user.FullName,
            roles,
            accessToken,
            accessTokenExpiresAtUtc);

        return new AuthSession(response, refreshToken, refreshTokenExpiresAtUtc);
    }

    private static void ValidateRegistration(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email)
            || request.Email.Trim().Length > MaximumEmailLength
            || !new EmailAddressAttribute().IsValid(request.Email.Trim()))
        {
            throw new ArgumentException("A valid email address is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100)
        {
            throw new ArgumentException("Full name is required and must be 100 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(request.Password)
            || request.Password.Length < 12
            || request.Password.Length > MaximumPasswordLength)
        {
            throw new ArgumentException("Password must be between 12 and 1024 characters long.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        // SQL Server reports duplicate keys as 2601 (unique index) or 2627
        // (unique/primary-key constraint). Other persistence failures must
        // remain server errors instead of being mislabeled as duplicate email.
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

    private static string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
