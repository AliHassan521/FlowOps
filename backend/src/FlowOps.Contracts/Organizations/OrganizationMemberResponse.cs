namespace FlowOps.Contracts.Organizations;

public sealed record OrganizationMemberResponse(
    Guid UserId,
    string Email,
    string FullName,
    string Role,
    DateTime JoinedAtUtc,
    bool IsActive);
