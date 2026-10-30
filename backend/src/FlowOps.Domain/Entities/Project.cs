namespace FlowOps.Domain.Entities;

/// <summary>A delivery workspace owned by exactly one organization.</summary>
public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public Organization Organization { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Short key unique within the owning organization.</summary>
    public string Key { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<WorkItem> WorkItems { get; set; } = new List<WorkItem>();
}
