namespace FlowOps.Contracts.Auth;

public sealed record AuthResponse(
    Guid UserId,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc
);
