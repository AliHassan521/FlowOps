namespace FlowOps.Domain.Entities;

/// <summary>
/// Grants a user access to one organization with a role scoped to that tenant.
/// A user's role in another organization is stored on a different row.
/// </summary>
public sealed class OrganizationMembership
{
    public Guid OrganizationId { get; set; }

    public Organization Organization { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Guid RoleId { get; set; }

    public Role Role { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}
