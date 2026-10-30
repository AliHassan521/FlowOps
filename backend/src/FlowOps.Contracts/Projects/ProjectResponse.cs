namespace FlowOps.Contracts.Projects;

public sealed record ProjectResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Key,
    string? Description,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
