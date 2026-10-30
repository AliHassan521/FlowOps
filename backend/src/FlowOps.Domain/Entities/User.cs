namespace FlowOps.Domain.Entities;

/// <summary>
/// Represents an application user within the FlowOps platform.
/// Contains credentials (hashed password), profile info, and role mappings.
/// </summary>
public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Stored securely as a salted hash. Plaintext passwords are NEVER persisted.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation property: Many-to-Many via UserRole join entity
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
