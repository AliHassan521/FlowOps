using FlowOps.Domain.Enums;
using FlowOps.Domain.Incidents;
namespace FlowOps.Domain.Entities;

public sealed class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number => $"INC-{Id:N}".ToUpperInvariant();
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AffectedService { get; set; } = string.Empty;
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Medium;
    public IncidentStatus Status { get; private set; } = IncidentStatus.Open;
    public Guid ReporterUserId { get; set; }
    public User Reporter { get; set; } = null!;
    public Guid? OwnerUserId { get; private set; }
    public User? Owner { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public User? ResolvedBy { get; set; }
    public string? Resolution { get; private set; }
    public string? RootCause { get; private set; }
    public void SetOwner(Guid? ownerId, DateTime now)
    {
        EnsureOpen();
        if (ownerId is null && Status != IncidentStatus.Open)
            throw new InvalidOperationException("An active investigation must retain an owner.");
        OwnerUserId = ownerId; UpdatedAtUtc = now;
    }
    public void UpdateDetails(string title, string? description, string affectedService, IncidentSeverity severity, DateTime now)
    {
        EnsureOpen();
        Title = title; Description = description; AffectedService = affectedService; Severity = severity; UpdatedAtUtc = now;
    }
    public void TransitionTo(IncidentStatus next, Guid actorId, string? resolution, string? rootCause, DateTime now)
    {
        if (!IncidentWorkflow.CanTransition(Status, next))
            throw new InvalidOperationException($"Transition from {Status} to {next} is not allowed.");
        if (next is IncidentStatus.Investigating or IncidentStatus.Mitigated or IncidentStatus.Resolved && OwnerUserId is null)
            throw new InvalidOperationException("An incident must have an owner before investigation.");
        if (next == IncidentStatus.Resolved)
        {
            if (string.IsNullOrWhiteSpace(resolution) || resolution.Trim().Length is < 2 or > 4000 || rootCause?.Length > 4000)
                throw new ArgumentException("Resolution must be 2–4000 characters; root cause at most 4000.");
            if (now < StartedAtUtc) throw new ArgumentException("Resolution cannot precede the incident start.");
            Resolution = resolution.Trim(); RootCause = string.IsNullOrWhiteSpace(rootCause) ? null : rootCause.Trim();
            EndedAtUtc = now; ResolvedByUserId = actorId;
        }
        else if (!string.IsNullOrWhiteSpace(resolution) || !string.IsNullOrWhiteSpace(rootCause))
            throw new ArgumentException("Resolution and root cause can be supplied only when resolving an incident.");
        Status = next; UpdatedAtUtc = now;
    }
    private void EnsureOpen()
    {
        if (Status is IncidentStatus.Resolved or IncidentStatus.Closed)
            throw new InvalidOperationException("Resolved and closed incidents retain their recorded details and owner.");
    }
}
