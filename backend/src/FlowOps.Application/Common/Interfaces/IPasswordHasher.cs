namespace FlowOps.Application.Common.Interfaces;

/// <summary>
/// Abstraction for cryptographic password hashing and verification.
/// Prevents Application and Domain layers from depending on concrete hashing libraries.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a plaintext password using a salted cryptographic algorithm.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Verifies whether the provided plaintext password matches the stored hash.
    /// </summary>
    bool VerifyPassword(string password, string passwordHash);
}
