using FlowOps.Contracts.Auth;
using FlowOps.Application.Common.Models;

namespace FlowOps.Application.Common.Interfaces;

public interface IAuthService
{
    /// <summary>
    /// Registers a new user, hashes their password with BCrypt, assigns their initial role, and returns an auth token.
    /// Assigns the safe default role and creates a refresh-token session.
    /// </summary>
    Task<AuthSession> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a user against their stored BCrypt password hash and returns a signed JWT token.
    /// Throws UnauthorizedAccessException if credentials are invalid or the account is inactive.
    /// </summary>
    Task<AuthSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
