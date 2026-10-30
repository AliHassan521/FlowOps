namespace FlowOps.Application.Common.Interfaces;

/// <summary>
/// Contract for generating JWT tokens.
/// Defined here in Application so use cases can depend on it
/// without knowing about the Infrastructure implementation.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a signed JWT token for an authenticated user.
    /// </summary>
    /// <param name="userId">The user's unique identifier.</param>
    /// <param name="email">The user's email address.</param>
    /// <param name="roles">The user's roles.</param>
    /// <returns>A signed JWT token string.</returns>
    string GenerateToken(Guid userId, string email, IEnumerable<string> roles);
}
