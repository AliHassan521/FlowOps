using FlowOps.Domain.Enums;
using FlowOps.Domain.WorkItems;

namespace FlowOps.Domain.Entities;

public sealed class WorkItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public WorkItemType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Medium;
    public WorkItemStatus Status { get; private set; } = WorkItemStatus.New;
    public Guid ReporterUserId { get; set; }
    public User Reporter { get; set; } = null!;
    public Guid? AssigneeUserId { get; set; }
    public User? Assignee { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
    public ICollection<WorkItemComment> Comments { get; set; } = new List<WorkItemComment>();

    public void SetAssignee(Guid? assigneeUserId, DateTime occurredAtUtc)
    {
        if (assigneeUserId is null && Status is WorkItemStatus.Assigned or WorkItemStatus.InProgress)
            throw new InvalidOperationException("An assigned or in-progress work item must retain an assignee.");
        AssigneeUserId = assigneeUserId;
        UpdatedAtUtc = occurredAtUtc;
    }

    public void UpdateDetails(string title, string? description, WorkItemPriority priority, DateTime? dueAtUtc, DateTime occurredAtUtc)
    {
        Title = title;
        Description = description;
        Priority = priority;
        DueAtUtc = dueAtUtc;
        UpdatedAtUtc = occurredAtUtc;
    }

    public void TransitionTo(WorkItemStatus nextStatus, DateTime occurredAtUtc)
    {
        if (!WorkItemWorkflow.CanTransition(Status, nextStatus))
        {
            throw new InvalidOperationException($"Transition from {Status} to {nextStatus} is not allowed.");
        }

        if (nextStatus is WorkItemStatus.Assigned or WorkItemStatus.InProgress && AssigneeUserId is null)
        {
            throw new InvalidOperationException($"A work item must have an assignee before moving to {nextStatus}.");
        }

        Status = nextStatus;
        UpdatedAtUtc = occurredAtUtc;
    }
}
