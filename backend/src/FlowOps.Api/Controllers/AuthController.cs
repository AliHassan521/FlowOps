using System.Security.Claims;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace FlowOps.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;

    public AuthController(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        _configuration = configuration;
    }

    /// <summary>
    /// Registers a new user account with a safe default role.
    /// </summary>
    [HttpPost("register")]
    [RequestSizeLimit(16_384)]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request, CancellationToken cancellationToken)
    {
        var session = await _authService.RegisterAsync(request ?? throw new ArgumentException("Request body is required."), cancellationToken);
        SetRefreshTokenCookie(session.RefreshToken, session.RefreshTokenExpiresAtUtc);
        return StatusCode(StatusCodes.Status201Created, session.Response);
    }

    /// <summary>
    /// Authenticates an existing user with email and password.
    /// </summary>
    [HttpPost("login")]
    [RequestSizeLimit(16_384)]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken cancellationToken)
    {
        var session = await _authService.LoginAsync(request ?? throw new UnauthorizedAccessException("Invalid email or password."), cancellationToken);
        SetRefreshTokenCookie(session.RefreshToken, session.RefreshTokenExpiresAtUtc);
        return Ok(session.Response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!HasTrustedBrowserOrigin())
        {
            return Forbid();
        }

        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        var session = await _authService.RefreshAsync(refreshToken ?? string.Empty, cancellationToken);
        SetRefreshTokenCookie(session.RefreshToken, session.RefreshTokenExpiresAtUtc);
        return Ok(session.Response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!HasTrustedBrowserOrigin())
        {
            return Forbid();
        }

        await _authService.RevokeRefreshTokenAsync(
            Request.Cookies[RefreshTokenCookieName] ?? string.Empty,
            cancellationToken);
        Response.Cookies.Delete(RefreshTokenCookieName, RefreshCookieOptions());
        return NoContent();
    }

    /// <summary>
    /// Protected endpoint that returns the current authenticated user's profile from the JWT claims.
    /// Returns 401 Unauthorized if no valid Bearer token is provided.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();

        return Ok(new
        {
            message = "Authentication successful!",
            userId,
            email,
            roles
        });
    }

    [HttpGet("admin-check")]
    [Authorize(Policy = "OrganizationAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult AdminCheck() => Ok(new { authorized = true });

    private const string RefreshTokenCookieName = "flowops_refresh_token";

    private void SetRefreshTokenCookie(string token, DateTime expiresAtUtc)
    {
        Response.Cookies.Append(RefreshTokenCookieName, token, RefreshCookieOptions(expiresAtUtc));
    }

    private CookieOptions RefreshCookieOptions(DateTime? expiresAtUtc = null) => new()
    {
        HttpOnly = true,
        Secure = !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path = "/api/auth",
        Expires = expiresAtUtc.HasValue ? new DateTimeOffset(expiresAtUtc.Value) : null,
        IsEssential = true
    };

    private bool HasTrustedBrowserOrigin()
    {
        var origin = Request.Headers.Origin.ToString();
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var requestOrigin)
            || !string.Equals(origin, requestOrigin.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
        {
            return false;
        }

        var allowedOrigins = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return allowedOrigins.Any(allowedOrigin =>
            Uri.TryCreate(allowedOrigin, UriKind.Absolute, out var configuredOrigin)
            && string.Equals(
                requestOrigin.GetLeftPart(UriPartial.Authority),
                configuredOrigin.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase));
    }
}
