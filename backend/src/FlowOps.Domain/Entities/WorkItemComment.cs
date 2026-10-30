namespace FlowOps.Domain.Entities;

public sealed class WorkItemComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;
    public Guid AuthorUserId { get; set; }
    public User Author { get; set; } = null!;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
