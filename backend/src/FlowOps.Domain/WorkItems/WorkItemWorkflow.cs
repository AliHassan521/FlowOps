using FlowOps.Domain.Enums;

namespace FlowOps.Domain.WorkItems;

/// <summary>Defines the only valid state changes for work items.</summary>
public static class WorkItemWorkflow
{
    private static readonly IReadOnlyDictionary<WorkItemStatus, WorkItemStatus[]> Transitions =
        new Dictionary<WorkItemStatus, WorkItemStatus[]>
        {
            [WorkItemStatus.New] = [WorkItemStatus.Triaged],
            [WorkItemStatus.Triaged] = [WorkItemStatus.Assigned],
            [WorkItemStatus.Assigned] = [WorkItemStatus.InProgress],
            [WorkItemStatus.InProgress] = [WorkItemStatus.CodeReview],
            [WorkItemStatus.CodeReview] = [WorkItemStatus.InProgress, WorkItemStatus.QA],
            [WorkItemStatus.QA] = [WorkItemStatus.InProgress, WorkItemStatus.Resolved],
            [WorkItemStatus.Resolved] = [WorkItemStatus.InProgress, WorkItemStatus.Closed],
            [WorkItemStatus.Closed] = [WorkItemStatus.InProgress]
        };

    public static IReadOnlyList<WorkItemStatus> AllowedFrom(WorkItemStatus currentStatus) =>
        Transitions.TryGetValue(currentStatus, out var next) ? next : [];

    public static bool CanTransition(WorkItemStatus currentStatus, WorkItemStatus nextStatus) =>
        AllowedFrom(currentStatus).Contains(nextStatus);
}
