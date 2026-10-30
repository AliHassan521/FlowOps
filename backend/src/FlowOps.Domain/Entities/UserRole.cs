namespace FlowOps.Domain.Entities;

/// <summary>
/// Explicit join entity representing the Many-to-Many relationship between User and Role.
/// Allows capturing assignment metadata like AssignedAt.
/// </summary>
public sealed class UserRole
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
