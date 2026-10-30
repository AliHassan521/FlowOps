using FlowOps.Application.Common.Interfaces;

namespace FlowOps.Infrastructure.Security;

/// <summary>
/// BCrypt implementation of IPasswordHasher.
/// Generates salted, work-factor tuned hashes that are resistant to rainbow-table and brute-force attacks.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    // Work factor of 12 provides strong security with sub-second execution time
    private const int WorkFactor = 12;

    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.EnhancedHashPassword(password, WorkFactor);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        return BCrypt.Net.BCrypt.EnhancedVerify(password, passwordHash);
    }
}
