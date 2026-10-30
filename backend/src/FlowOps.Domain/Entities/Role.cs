namespace FlowOps.Domain.Entities;

/// <summary>
/// Represents a security role in FlowOps (e.g. Admin, TeamLead, Engineer, QA, Viewer).
/// </summary>
public sealed class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // Navigation property
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
