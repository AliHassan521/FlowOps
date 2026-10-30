using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FlowOps.Application.Common.Configuration;
using FlowOps.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FlowOps.Infrastructure.Security;

/// <summary>
/// Generates signed JWT tokens for authenticated users.
/// The token contains claims (user info) that can be read by any service
/// that knows the secret key — without hitting the database again.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly JwtSettings _settings;

    public JwtTokenService(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
    }

    public string GenerateToken(Guid userId, string email, IEnumerable<string> roles)
    {
        // The signing key — created from our secret string.
        // HMAC-SHA256 is a one-way hash; only our server can produce valid tokens.
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Claims are pieces of info baked into the token.
        // The client can READ these (they're base64-encoded), but NOT modify them.
        // If someone tampers with the payload, the signature breaks and we reject it.
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub,   userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()) // unique token ID
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer:             _settings.Issuer,
            audience:           _settings.Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(_settings.AccessTokenMinutes),
            signingCredentials: creds);

        // Serialize the token to the compact JWT string format:
        // header.payload.signature
        return _tokenHandler.WriteToken(token);
    }
}
