namespace FlowOps.Contracts.Auth;

public sealed record RegisterRequest(
    string Email,
    string FullName,
    string Password
);
