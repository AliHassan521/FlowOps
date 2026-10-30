namespace FlowOps.Contracts.WorkItems;

public sealed record WorkItemResponse(
    Guid Id,
    Guid ProjectId,
    string Type,
    string Title,
    string? Description,
    string Priority,
    string Status,
    Guid ReporterUserId,
    string ReporterName,
    Guid? AssigneeUserId,
    string? AssigneeName,
    DateTime? DueAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int CommentCount);
