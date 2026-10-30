using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FlowOps.Contracts.Auth;
using FlowOps.Domain.Entities;
using FlowOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FlowOps.Application.Tests;

public sealed class AuthApiTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuthApiTests()
    {
        _connection.Open();
        _factory = new AuthApiFactory(_connection);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Registration_sets_a_protected_refresh_cookie_and_bearer_auth_enforces_roles()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.Equal(["Engineer"], auth.Roles);

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("flowops_refresh_token=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(setCookie.Split('=', 2)[1].Split(';', 2)[0], await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var unauthorized = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var authenticated = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);

        using var forbidden = await _client.GetAsync("/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Admin_role_can_access_the_admin_policy()
    {
        using var registration = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("admin@example.com", "Test Admin", "correct horse battery"));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var registeredUser = await registration.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registeredUser);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
            var adminRole = await dbContext.Roles.SingleAsync(role => role.Name == "Admin");
            dbContext.UserRoles.Add(new UserRole
            {
                UserId = registeredUser.UserId,
                RoleId = adminRole.Id,
                AssignedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        using var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("admin@example.com", "correct horse battery"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var adminSession = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(adminSession);
        Assert.Contains("Admin", adminSession.Roles);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminSession.AccessToken);
        using var authorized = await _client.GetAsync("/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_rejects_tampered_and_expired_access_tokens()
    {
        using var registration = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));
        var session = await registration.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(session);

        var lastCharacter = session.AccessToken[^1];
        var tamperedToken = session.AccessToken[..^1] + (lastCharacter == 'a' ? 'b' : 'a');
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);
        using var tamperedResponse = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, tamperedResponse.StatusCode);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthApiFactory.TestJwtSecret));
        var expiredJwt = new JwtSecurityToken(
            issuer: "FlowOps.Api.Tests",
            audience: "FlowOps.Client.Tests",
            claims: [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow.AddMinutes(-3),
            expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var expiredToken = new JwtSecurityTokenHandler().WriteToken(expiredJwt);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);
        using var expiredResponse = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_cookie_reuse_revokes_its_family_and_logout_revokes_current_token()
    {
        using var registration = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var originalCookie = ExtractRefreshCookie(registration);

        using var firstRefresh = await PostWithCookieAsync("/api/auth/refresh", originalCookie);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var rotatedCookie = ExtractRefreshCookie(firstRefresh);
        Assert.NotEqual(originalCookie, rotatedCookie);

        using var replay = await PostWithCookieAsync("/api/auth/refresh", originalCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        using var revokedDescendant = await PostWithCookieAsync("/api/auth/refresh", rotatedCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedDescendant.StatusCode);
    }

    [Fact]
    public async Task Refresh_and_logout_require_a_configured_browser_origin()
    {
        using var registration = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));
        var cookie = ExtractRefreshCookie(registration);

        using var untrustedOrigin = await PostWithCookieAsync("/api/auth/refresh", cookie, "https://attacker.example");
        Assert.Equal(HttpStatusCode.Forbidden, untrustedOrigin.StatusCode);

        using var missingOrigin = await PostWithCookieAsync("/api/auth/logout", cookie, null);
        Assert.Equal(HttpStatusCode.Forbidden, missingOrigin.StatusCode);

        using var allowedOrigin = await PostWithCookieAsync("/api/auth/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, allowedOrigin.StatusCode);
    }

    [Fact]
    public async Task Login_rejects_bad_credentials_and_logout_revokes_refresh_token()
    {
        using var registration = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("person@example.com", "Test Person", "correct horse battery"));
        var cookie = ExtractRefreshCookie(registration);

        using var badLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("person@example.com", "wrong password"));
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);

        using var logout = await PostWithCookieAsync("/api/auth/logout", cookie);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var refresh = await PostWithCookieAsync("/api/auth/refresh", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Authentication_endpoints_apply_rate_limits()
    {
        for (var requestNumber = 0; requestNumber < 10; requestNumber++)
        {
            using var response = await _client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest("missing@example.com", "wrong password"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var rateLimited = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("missing@example.com", "wrong password"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimited.StatusCode);
    }

    [Fact]
    public async Task Cors_allows_only_the_configured_frontend_origin()
    {
        using var allowedRequest = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        allowedRequest.Headers.Add("Origin", "https://localhost");
        allowedRequest.Headers.Add("Access-Control-Request-Method", "POST");
        using var allowedResponse = await _client.SendAsync(allowedRequest);
        Assert.Equal("https://localhost", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var blockedRequest = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        blockedRequest.Headers.Add("Origin", "https://attacker.example");
        blockedRequest.Headers.Add("Access-Control-Request-Method", "POST");
        using var blockedResponse = await _client.SendAsync(blockedRequest);
        Assert.False(blockedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private async Task<HttpResponseMessage> PostWithCookieAsync(string path, string cookie, string? origin = "https://localhost")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }
        request.Content = JsonContent.Create(new { });
        return await _client.SendAsync(request);
    }

    private static string ExtractRefreshCookie(HttpResponseMessage response)
    {
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        return setCookie.Split(';', 2)[0];
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Dispose();
    }

    private sealed class AuthApiFactory : WebApplicationFactory<Program>
    {
        internal const string TestJwtSecret = "integration-test-secret-key-with-at-least-32-characters";
        private readonly SqliteConnection _connection;

        public AuthApiFactory(SqliteConnection connection)
        {
            _connection = connection;

            // Minimal hosting reads these settings while Program.Main is
            // executing, before WebApplicationFactory's later config hooks.
            Environment.SetEnvironmentVariable("JwtSettings__SecretKey", TestJwtSecret);
            Environment.SetEnvironmentVariable("JwtSettings__Issuer", "FlowOps.Api.Tests");
            Environment.SetEnvironmentVariable("JwtSettings__Audience", "FlowOps.Client.Tests");
            Environment.SetEnvironmentVariable("JwtSettings__AccessTokenMinutes", "15");
            Environment.SetEnvironmentVariable("JwtSettings__RefreshTokenDays", "30");
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Server=(local);Database=FlowOpsTests;User Id=test;Password=test;TrustServerCertificate=True");
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "https://localhost");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<FlowOpsDbContext>>();
                services.RemoveAll<DbContextOptions<FlowOpsDbContext>>();
                services.RemoveAll<FlowOpsDbContext>();
                services.AddDbContext<FlowOpsDbContext>(options => options.UseSqlite(_connection));
            });
        }
    }
}
