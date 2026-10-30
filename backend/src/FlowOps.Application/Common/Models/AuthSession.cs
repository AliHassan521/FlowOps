using FlowOps.Contracts.Auth;

namespace FlowOps.Application.Common.Models;

public sealed record AuthSession(
    AuthResponse Response,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
