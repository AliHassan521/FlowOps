namespace FlowOps.Contracts.Notifications;

public sealed record NotificationResponse(Guid Id, Guid OrganizationId, Guid ProjectId, Guid? WorkItemId,
    string Kind, string Message, DateTime CreatedAtUtc, DateTime? ReadAtUtc, Guid? IncidentId = null, Guid? ReleaseId = null);

public sealed record NotificationSummaryResponse(long UnreadCount);
