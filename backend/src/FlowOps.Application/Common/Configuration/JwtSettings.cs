namespace FlowOps.Application.Common.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; init; } = string.Empty;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 30;
}
