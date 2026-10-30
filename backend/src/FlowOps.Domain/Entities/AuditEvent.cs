namespace FlowOps.Domain.Entities;

/// <summary>Immutable business audit entry for a tenant-owned state change.</summary>
public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public Organization Organization { get; set; } = null!;

    public Guid ActorUserId { get; set; }

    public User Actor { get; set; } = null!;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public string? DetailsJson { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
