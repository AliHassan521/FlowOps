namespace FlowOps.Contracts.WorkItems;

public sealed record CreateWorkItemRequest(
    string Type,
    string Title,
    string? Description,
    string Priority,
    Guid? AssigneeUserId,
    DateTime? DueAtUtc);
