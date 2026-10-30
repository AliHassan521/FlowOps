namespace FlowOps.Contracts.WorkItems;

public sealed record UpdateWorkItemRequest(
    string Title,
    string? Description,
    string Priority,
    DateTime? DueAtUtc);
