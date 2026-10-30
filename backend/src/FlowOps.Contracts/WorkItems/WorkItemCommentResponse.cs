namespace FlowOps.Contracts.WorkItems;

public sealed record WorkItemCommentResponse(
    Guid Id,
    Guid AuthorUserId,
    string AuthorName,
    string Body,
    DateTime CreatedAtUtc);
