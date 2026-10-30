namespace FlowOps.Contracts.Auth;

public sealed record LoginRequest(
    string Email,
    string Password
);
