namespace FlowOps.Contracts.WorkItems;

public sealed record WorkItemHistoryResponse(
    Guid Id,
    Guid ActorUserId,
    string ActorName,
    string Action,
    string? DetailsJson,
    DateTime OccurredAtUtc);
