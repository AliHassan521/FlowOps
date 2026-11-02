using System.Net;
using System.Net.Http.Json;
using System.Text;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Organizations;
using FlowOps.Contracts.WorkItems;
using Xunit;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Net.Http.Headers;
using Microsoft.IdentityModel.Tokens;

namespace FlowOps.Application.Tests;

public sealed partial class AuthApiTests
{
    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("future")]
    [InlineData("unsigned")]
    [InlineData("unknown-user")]
    public async Task Invalid_JWT_boundaries_fail_closed(string attack)
    {
        var user = await RegisterAndAuthorizeAsync("jwt-boundary@example.test");
        var jwt = new JwtSecurityToken(
            issuer: attack == "issuer" ? "attacker" : "FlowOps.Api.Tests",
            audience: attack == "audience" ? "attacker" : "FlowOps.Client.Tests",
            claims: [new Claim(ClaimTypes.NameIdentifier, attack == "unknown-user" ? Guid.NewGuid().ToString() : user.UserId.ToString())],
            notBefore: attack == "future" ? _factory.AuthenticationTimeUtc.AddMinutes(10) : _factory.AuthenticationTimeUtc.AddMinutes(-1),
            expires: _factory.AuthenticationTimeUtc.AddMinutes(15),
            signingCredentials: attack == "unsigned" ? null : new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthApiFactory.TestJwtSecret)), SecurityAlgorithms.HmacSha256));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        using var denied = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("https://localhost.attacker.example")]
    [InlineData("https://localhost/")]
    [InlineData("https://localhost@attacker.example")]
    [InlineData("https://localhost, https://attacker.example")]
    public async Task Cookie_endpoints_reject_ambiguous_and_lookalike_origins(string origin)
    {
        using var registration = await _client.PostAsJsonAsync("/api/auth/register",
            new FlowOps.Contracts.Auth.RegisterRequest("csrf@example.test", "Origin fixture", "correct horse battery"));
        var cookie = ExtractRefreshCookie(registration);
        foreach (var path in new[] { "/api/auth/refresh", "/api/auth/logout" })
        {
            using var denied = await PostWithCookieAsync(path, cookie, origin);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var stillActive = await PostWithCookieAsync("/api/auth/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
    }

    [Theory]
    [InlineData(200, 8000, true)]
    [InlineData(201, 8000, false)]
    [InlineData(200, 8001, false)]
    public async Task Work_item_text_limits_enforce_exact_boundaries(int titleLength, int descriptionLength, bool valid)
    {
        var scenario = await CreateNotificationScenarioAsync();
        using var response = await _client.PostAsJsonAsync(scenario.Path,
            new CreateWorkItemRequest("Task", new string('界', titleLength), new string('d', descriptionLength), "High", null, null));
        Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("page=2147483648")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("sortDirection=sideways")]
    [InlineData("sortBy=title%3BDROP%20TABLE%20Users")]
    [InlineData("type=1")]
    [InlineData("status=999")]
    [InlineData("priority=unknown")]
    [InlineData("assigneeUserId=not-a-guid")]
    [InlineData("dueBeforeUtc=not-a-date")]
    public async Task Malformed_filters_are_rejected_without_persistence_changes(string query)
    {
        var scenario = await CreateNotificationScenarioAsync();
        using var response = await _client.GetAsync($"{scenario.Path}?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("StackTrace", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var unchanged = await _client.GetFromJsonAsync<PagedResponse<WorkItemResponse>>(scenario.Path);
        Assert.Equal(1, unchanged!.TotalCount);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{invalid")]
    [InlineData("{\"type\":null,\"title\":\"bad item\",\"priority\":\"High\"}")]
    [InlineData("{\"type\":\"Task\",\"title\":null,\"priority\":\"High\"}")]
    [InlineData("{\"type\":\"Task\",\"title\":\"bad item\",\"priority\":0}")]
    [InlineData("{\"type\":\"Task\",\"title\":\" \",\"priority\":\"High\"}")]
    [InlineData("{\"type\":\"Task\",\"title\":\"bad item\",\"priority\":\"High\",\"dueAtUtc\":\"invalid\"}")]
    public async Task Malformed_work_item_bodies_return_client_errors(string json)
    {
        var scenario = await CreateNotificationScenarioAsync();
        using var response = await _client.PostAsync(scenario.Path, new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, (await _client.GetFromJsonAsync<PagedResponse<WorkItemResponse>>(scenario.Path))!.TotalCount);
    }

    [Theory]
    [InlineData("Admin", true, true)]
    [InlineData("TeamLead", true, true)]
    [InlineData("Engineer", true, false)]
    [InlineData("QA", true, false)]
    [InlineData("Viewer", false, false)]
    public async Task Role_matrix_enforces_creation_assignment_and_comment_permissions(string role, bool write, bool assign)
    {
        var scenario = await CreateNotificationScenarioAsync();
        using var change = await _client.PutAsJsonAsync($"/api/organizations/{scenario.Organization.Id}/members/{scenario.Recipient.UserId}/role",
            new UpdateOrganizationMemberRoleRequest(role));
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Authorize(scenario.Recipient);
        using var read = await _client.GetAsync($"{scenario.Path}/{scenario.Item.Id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var create = await _client.PostAsJsonAsync(scenario.Path, new CreateWorkItemRequest("Task", "Permission probe", null, "High", null, null));
        Assert.Equal(write ? HttpStatusCode.Created : HttpStatusCode.Forbidden, create.StatusCode);
        using var assignment = await _client.PutAsJsonAsync($"{scenario.Path}/{scenario.Item.Id}/assignee", new AssignWorkItemRequest(scenario.Admin.UserId));
        Assert.Equal(assign ? HttpStatusCode.OK : HttpStatusCode.Forbidden, assignment.StatusCode);
        using var comment = await _client.PostAsJsonAsync($"{scenario.Path}/{scenario.Item.Id}/comments", new AddWorkItemCommentRequest("Permission probe"));
        Assert.Equal(write ? HttpStatusCode.Created : HttpStatusCode.Forbidden, comment.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/comments")]
    [InlineData("/history")]
    [InlineData("/sla")]
    public async Task Outsiders_cannot_read_nested_resources_or_reuse_foreign_project_ids(string suffix)
    {
        var scenario = await CreateNotificationScenarioAsync();
        var outsider = await RegisterAndAuthorizeAsync("outsider@example.test");
        using var denied = await _client.GetAsync($"{scenario.Path}/{scenario.Item.Id}{suffix}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var orgResponse = await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Other tenant", "other-tenant", null));
        var own = (await orgResponse.Content.ReadFromJsonAsync<OrganizationResponse>())!;
        var swapped = scenario.Path.Replace(scenario.Organization.Id.ToString(), own.Id.ToString());
        using var deniedSwap = await _client.GetAsync($"{swapped}/{scenario.Item.Id}{suffix}");
        Assert.Equal(HttpStatusCode.NotFound, deniedSwap.StatusCode);
    }

    [Fact]
    public async Task Inactive_accounts_cannot_use_existing_bearer_tokens_for_tenant_resources()
    {
        var scenario = await CreateNotificationScenarioAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
            var user = await db.Users.SingleAsync(user => user.Id == scenario.Recipient.UserId);
            user.IsActive = false; await db.SaveChangesAsync();
        }
        Authorize(scenario.Recipient);
        foreach (var path in new[] { scenario.Path, $"{scenario.Path}/{scenario.Item.Id}", "/api/notifications", "/api/auth/me" })
        {
            using var denied = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
    }

    [Fact]
    public async Task SQL_and_HTML_payloads_remain_data_and_overposted_security_fields_are_ignored()
    {
        var scenario = await CreateNotificationScenarioAsync();
        const string payload = "<img src=x onerror=alert(1)> '; DROP TABLE Users; --";
        using var response = await _client.PostAsJsonAsync(scenario.Path, new {
            type = "Task", title = payload, description = payload, priority = "High",
            reporterUserId = scenario.Recipient.UserId, status = "Closed", organizationId = Guid.NewGuid(), roles = new[] { "Admin" }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<WorkItemResponse>())!;
        Assert.Equal(payload, item.Title); Assert.Equal("New", item.Status);
        Assert.Equal(scenario.Admin.UserId, item.ReporterUserId);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>().Users.CountAsync());
        using var search = await _client.GetAsync($"{scenario.Path}?search={Uri.EscapeDataString("' OR 1=1 --")}");
        Assert.Equal(0, (await search.Content.ReadFromJsonAsync<PagedResponse<WorkItemResponse>>())!.TotalCount);
    }
    [Theory]
    [InlineData("Engineer")]
    [InlineData("QA")]
    public async Task Creation_cannot_bypass_lead_only_assignment_permissions(string role)
    {
        var scenario = await CreateNotificationScenarioAsync();
        if (role == "QA")
        {
            using var change = await _client.PutAsJsonAsync($"/api/organizations/{scenario.Organization.Id}/members/{scenario.Recipient.UserId}/role",
                new UpdateOrganizationMemberRoleRequest(role));
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        }
        Authorize(scenario.Recipient);
        using var bypass = await _client.PostAsJsonAsync(scenario.Path,
            new CreateWorkItemRequest("Task", "Try to assign a teammate", null, "High", scenario.Admin.UserId, null));
        Assert.Equal(HttpStatusCode.Forbidden, bypass.StatusCode);
        using var allowed = await _client.PostAsJsonAsync(scenario.Path,
            new CreateWorkItemRequest("Task", "Create unassigned work", null, "High", null, null));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }
}
