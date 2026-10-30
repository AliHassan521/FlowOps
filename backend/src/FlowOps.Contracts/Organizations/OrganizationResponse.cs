namespace FlowOps.Contracts.Organizations;

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string CurrentUserRole,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
