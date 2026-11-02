namespace FlowOps.Domain.Entities;

public sealed class IncidentComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid AuthorUserId { get; set; }
    public User Author { get; set; } = null!;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
