using FlowOps.Domain.Enums;
namespace FlowOps.Domain.Entities;
// One record represents one deployment attempt. A retry is a new Draft, retaining the failed attempt.
public sealed class Release
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid EnvironmentId { get; set; }
    public ReleaseEnvironment Environment { get; set; } = null!;
    public string ApplicationName { get; set; } = string.Empty;
    public string ReleaseVersion { get; set; } = string.Empty;
    public string? Notes { get; private set; }
    public ReleaseStatus Status { get; private set; } = ReleaseStatus.Draft;
    public Guid CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public User? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? DeploymentStartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? Outcome { get; private set; }
    public ICollection<ReleaseWorkItem> WorkItems { get; set; } = new List<ReleaseWorkItem>();
    public ICollection<ReleaseIncident> Incidents { get; set; } = new List<ReleaseIncident>();

    public void EnsureDraft()
    {
        if (Status != ReleaseStatus.Draft) throw new InvalidOperationException("Approved deployment details and links cannot be changed. Create a new draft for another attempt.");
    }
    public void UpdateNotes(string? notes, DateTime now)
    {
        EnsureDraft();
        if (notes?.Length > 8000) throw new ArgumentException("Notes must be 8000 characters or fewer.");
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(); UpdatedAtUtc = now;
    }
    public void TransitionTo(ReleaseStatus next, Guid actorUserId, string? outcome, DateTime now)
    {
        var allowed = (Status, next) is (ReleaseStatus.Draft, ReleaseStatus.Approved)
            or (ReleaseStatus.Approved, ReleaseStatus.Deploying)
            or (ReleaseStatus.Deploying, ReleaseStatus.Successful)
            or (ReleaseStatus.Deploying, ReleaseStatus.Failed);
        if (!allowed) throw new InvalidOperationException($"Transition from {Status} to {next} is not allowed.");
        if (now < CreatedAtUtc || ApprovedAtUtc.HasValue && now < ApprovedAtUtc || DeploymentStartedAtUtc.HasValue && now < DeploymentStartedAtUtc)
            throw new ArgumentException("Release timestamps must follow the recorded lifecycle.");
        if (next is ReleaseStatus.Successful or ReleaseStatus.Failed)
        {
            if (string.IsNullOrWhiteSpace(outcome) || outcome.Trim().Length is < 2 or > 4000)
                throw new ArgumentException("A deployment outcome of 2–4000 characters is required.");
            CompletedAtUtc = now; Outcome = outcome.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(outcome)) throw new ArgumentException("An outcome is recorded only when completing a deployment.");
        if (next == ReleaseStatus.Approved) { ApprovedByUserId = actorUserId; ApprovedAtUtc = now; }
        if (next == ReleaseStatus.Deploying) DeploymentStartedAtUtc = now;
        Status = next; UpdatedAtUtc = now;
    }
}
public sealed class ReleaseWorkItem
{
    public Guid ReleaseId { get; set; }
    public Release Release { get; set; } = null!;
    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;
}
public sealed class ReleaseIncident
{
    public Guid ReleaseId { get; set; }
    public Release Release { get; set; } = null!;
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
}
