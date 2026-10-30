namespace FlowOps.Contracts.WorkItems;

public sealed record WorkItemListQuery(
    string? Type = null,
    string? Status = null,
    string? Priority = null,
    Guid? AssigneeUserId = null,
    string? Search = null,
    DateTime? DueBeforeUtc = null,
    int Page = 1,
    int PageSize = 50,
    string SortBy = "createdAtUtc",
    string SortDirection = "desc");
