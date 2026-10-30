namespace FlowOps.Domain.Entities;

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>A stable, normalized URL identifier unique across organizations.</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<OrganizationMembership> Memberships { get; set; } = new List<OrganizationMembership>();

    public ICollection<AuditEvent> AuditEvents { get; set; } = new List<AuditEvent>();
}
