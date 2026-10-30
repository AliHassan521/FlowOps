using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FlowOps.Contracts.Auth;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Organizations;
using FlowOps.Contracts.Projects;
using FlowOps.Contracts.WorkItems;
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

    [Fact]
    public async Task Organization_creator_is_admin_and_membership_is_tenant_scoped_and_audited()
    {
        var admin = await RegisterAndAuthorizeAsync("org-admin@example.com");
        using var create = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Platform Team", "platform-team", "Delivery group"));
        Assert.True(create.StatusCode == HttpStatusCode.Created,
            $"Expected organization creation, got {(int)create.StatusCode}: {await create.Content.ReadAsStringAsync()}");
        var organization = await create.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(organization);
        Assert.Equal("Admin", organization.CurrentUserRole);

        using var invalidOrganization = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Invalid Slug", "Bad Slug", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidOrganization.StatusCode);

        using var duplicate = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Another Team", "platform-team", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var member = await RegisterAndAuthorizeAsync("engineer@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var addMember = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest("ENGINEER@example.com", "Engineer"));
        Assert.Equal(HttpStatusCode.OK, addMember.StatusCode);

        using var duplicateMember = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest("engineer@example.com", "Engineer"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateMember.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        using var getOrganization = await _client.GetAsync($"/api/organizations/{organization.Id}");
        Assert.Equal(HttpStatusCode.OK, getOrganization.StatusCode);
        var memberOrganization = await getOrganization.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(memberOrganization);
        Assert.Equal("Engineer", memberOrganization.CurrentUserRole);
        using var membersResponse = await _client.GetAsync($"/api/organizations/{organization.Id}/members");
        Assert.Equal(HttpStatusCode.OK, membersResponse.StatusCode);
        var members = await membersResponse.Content.ReadFromJsonAsync<PagedResponse<OrganizationMemberResponse>>();
        Assert.NotNull(members);
        Assert.Equal(2, members.TotalCount);

        using var forbiddenUpdate = await _client.PutAsJsonAsync($"/api/organizations/{organization.Id}",
            new UpdateOrganizationRequest("Stolen", null));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUpdate.StatusCode);

        using var secondOrganizationCreate = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Engineering", "engineering", null));
        Assert.Equal(HttpStatusCode.Created, secondOrganizationCreate.StatusCode);
        var secondOrganization = await secondOrganizationCreate.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(secondOrganization);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var crossTenant = await _client.GetAsync($"/api/organizations/{secondOrganization.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        using var deactivateMember = await _client.DeleteAsync($"/api/organizations/{organization.Id}/members/{member.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, deactivateMember.StatusCode);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        using var revokedMemberAccess = await _client.GetAsync($"/api/organizations/{organization.Id}");
        Assert.Equal(HttpStatusCode.NotFound, revokedMemberAccess.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
        Assert.Equal(3, await dbContext.AuditEvents.CountAsync(auditEvent => auditEvent.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task Organization_deactivation_removes_member_access_and_preserves_audit_history()
    {
        var admin = await RegisterAndAuthorizeAsync("deactivation-admin@example.com");
        using var create = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Retiring Team", "retiring-team", null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var organization = await create.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(organization);

        var member = await RegisterAndAuthorizeAsync("deactivation-member@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var addMember = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest("deactivation-member@example.com", "Engineer"));
        Assert.Equal(HttpStatusCode.OK, addMember.StatusCode);

        using var deactivate = await _client.DeleteAsync($"/api/organizations/{organization.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        foreach (var accessToken in new[] { admin.AccessToken, member.AccessToken })
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var getOrganization = await _client.GetAsync($"/api/organizations/{organization.Id}");
            Assert.Equal(HttpStatusCode.NotFound, getOrganization.StatusCode);
            using var listProjects = await _client.GetAsync($"/api/organizations/{organization.Id}/projects");
            Assert.Equal(HttpStatusCode.NotFound, listProjects.StatusCode);
        }

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
        Assert.Equal(3, await dbContext.AuditEvents.CountAsync(auditEvent => auditEvent.OrganizationId == organization.Id));
        Assert.Contains(await dbContext.AuditEvents.Where(auditEvent => auditEvent.OrganizationId == organization.Id)
            .Select(auditEvent => auditEvent.Action).ToListAsync(), action => action == "Organization.Deactivated");
    }

    [Fact]
    public async Task Organization_must_retain_an_active_admin_and_invalid_paging_is_rejected()
    {
        var admin = await RegisterAndAuthorizeAsync("sole-admin@example.com");
        using var create = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Solo Team", "solo-team", null));
        var organization = await create.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(organization);

        using var selfDeactivate = await _client.DeleteAsync($"/api/organizations/{organization.Id}/members/{admin.UserId}");
        Assert.True(selfDeactivate.StatusCode == HttpStatusCode.Conflict,
            $"Expected last-admin conflict, got {(int)selfDeactivate.StatusCode}: {await selfDeactivate.Content.ReadAsStringAsync()}");

        using var selfDemote = await _client.PutAsJsonAsync($"/api/organizations/{organization.Id}/members/{admin.UserId}/role",
            new UpdateOrganizationMemberRoleRequest("Engineer"));
        Assert.Equal(HttpStatusCode.Conflict, selfDemote.StatusCode);

        using var badPage = await _client.GetAsync("/api/organizations?page=0&pageSize=500");
        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
    }

    [Fact]
    public async Task Audit_events_cannot_be_updated_or_deleted_through_the_application_context()
    {
        await RegisterAndAuthorizeAsync("audit-owner@example.com");
        using var create = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Audit Team", "audit-team", null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        Guid auditEventId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
            auditEventId = await dbContext.AuditEvents.Select(auditEvent => auditEvent.Id).SingleAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
            var auditEvent = await dbContext.AuditEvents.SingleAsync(item => item.Id == auditEventId);
            auditEvent.Action = "Audit.Tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
            var auditEvent = await dbContext.AuditEvents.SingleAsync(item => item.Id == auditEventId);
            dbContext.AuditEvents.Remove(auditEvent);
            await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Projects_enforce_organization_scope_and_role_rules_and_audit_changes()
    {
        var admin = await RegisterAndAuthorizeAsync("project-admin@example.com");
        using var organizationResponse = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Product Group", "product-group", null));
        var organization = await organizationResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(organization);

        var teamLead = await RegisterAndAuthorizeAsync("lead@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var addLead = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest("lead@example.com", "TeamLead"));
        Assert.Equal(HttpStatusCode.OK, addLead.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", teamLead.AccessToken);
        var projectPath = $"/api/organizations/{organization.Id}/projects";
        using var createProject = await _client.PostAsJsonAsync(projectPath,
            new CreateProjectRequest("Reliability", "REL", "Reliability work"));
        Assert.Equal(HttpStatusCode.Created, createProject.StatusCode);
        var project = await createProject.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);

        using var duplicateKey = await _client.PostAsJsonAsync(projectPath,
            new CreateProjectRequest("Other Project", "REL", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicateKey.StatusCode);

        using var teamLeadUpdate = await _client.PutAsJsonAsync($"{projectPath}/{project.Id}",
            new UpdateProjectRequest("Reliability Engineering", "Operational readiness"));
        Assert.Equal(HttpStatusCode.OK, teamLeadUpdate.StatusCode);
        var updatedProject = await teamLeadUpdate.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(updatedProject);
        Assert.Equal("Reliability Engineering", updatedProject.Name);

        var engineer = await RegisterAndAuthorizeAsync("project-engineer@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var addEngineer = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest("project-engineer@example.com", "Engineer"));
        Assert.Equal(HttpStatusCode.OK, addEngineer.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", engineer.AccessToken);
        using var deniedCreate = await _client.PostAsJsonAsync(projectPath,
            new CreateProjectRequest("Restricted", "NOPE", null));
        Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);
        using var allowedList = await _client.GetAsync(projectPath);
        Assert.Equal(HttpStatusCode.OK, allowedList.StatusCode);
        using var deniedUpdate = await _client.PutAsJsonAsync($"{projectPath}/{project.Id}",
            new UpdateProjectRequest("Unauthorized Rename", null));
        Assert.Equal(HttpStatusCode.Forbidden, deniedUpdate.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var otherOrganizationResponse = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Other Product", "other-product", null));
        var otherOrganization = await otherOrganizationResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(otherOrganization);
        var otherProjectPath = $"/api/organizations/{otherOrganization.Id}/projects";
        using var sameKeyDifferentOrganization = await _client.PostAsJsonAsync(otherProjectPath,
            new CreateProjectRequest("Different Reliability", "REL", null));
        Assert.Equal(HttpStatusCode.Created, sameKeyDifferentOrganization.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", engineer.AccessToken);
        using var crossTenantList = await _client.GetAsync(otherProjectPath);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantList.StatusCode);
        using var crossTenant = await _client.GetAsync($"{otherProjectPath}/{project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var deactivate = await _client.DeleteAsync($"{projectPath}/{project.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        using var hiddenProject = await _client.GetAsync($"{projectPath}/{project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenProject.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
        Assert.Equal(3, await dbContext.AuditEvents.CountAsync(auditEvent =>
            auditEvent.OrganizationId == organization.Id && auditEvent.EntityType == "Project"));
    }

    [Fact]
    public async Task Work_item_workflow_enforces_transitions_assignment_roles_and_audit_history()
    {
        var admin = await RegisterAndAuthorizeAsync("work-item-admin@example.com");
        using var organizationResponse = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Work Item Team", "work-item-team", null));
        var organization = await organizationResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(organization);
        var projectPath = $"/api/organizations/{organization.Id}/projects";
        using var projectResponse = await _client.PostAsJsonAsync(projectPath,
            new CreateProjectRequest("Work Item Project", "WIP", null));
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        var workItemsPath = $"{projectPath}/{project.Id}/work-items";

        var engineer = await RegisterAndAuthorizeAsync("work-item-engineer@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var addEngineer = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest("work-item-engineer@example.com", "Engineer"));
        Assert.Equal(HttpStatusCode.OK, addEngineer.StatusCode);
        var qa = await RegisterAndAuthorizeAsync("work-item-qa@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var addQa = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest("work-item-qa@example.com", "QA"));
        Assert.Equal(HttpStatusCode.OK, addQa.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", engineer.AccessToken);
        using var create = await _client.PostAsJsonAsync(workItemsPath,
            new CreateWorkItemRequest("Task", "Ship the API", "Implement the resource flow", "High", null, null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var workItem = await create.Content.ReadFromJsonAsync<WorkItemResponse>();
        Assert.NotNull(workItem);
        Assert.Equal("New", workItem.Status);

        using var engineerCannotTriage = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("Triaged"));
        Assert.Equal(HttpStatusCode.Forbidden, engineerCannotTriage.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var invalidSkip = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("Assigned"));
        Assert.Equal(HttpStatusCode.Conflict, invalidSkip.StatusCode);

        using var triage = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("Triaged"));
        Assert.Equal(HttpStatusCode.OK, triage.StatusCode);
        using var unassigned = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("Assigned"));
        Assert.Equal(HttpStatusCode.Conflict, unassigned.StatusCode);

        using var assign = await _client.PutAsJsonAsync($"{workItemsPath}/{workItem.Id}/assignee",
            new AssignWorkItemRequest(engineer.UserId));
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        using var assigned = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("Assigned"));
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", engineer.AccessToken);
        using var inProgress = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("InProgress"));
        Assert.Equal(HttpStatusCode.OK, inProgress.StatusCode);
        using var codeReview = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("CodeReview"));
        Assert.Equal(HttpStatusCode.OK, codeReview.StatusCode);
        using var engineerCannotApproveQa = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("QA"));
        Assert.Equal(HttpStatusCode.Forbidden, engineerCannotApproveQa.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", qa.AccessToken);
        using var qaReview = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("QA"));
        Assert.Equal(HttpStatusCode.OK, qaReview.StatusCode);
        using var qaResolve = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("Resolved"));
        Assert.Equal(HttpStatusCode.OK, qaResolve.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var close = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("Closed"));
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        using var reopen = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/transitions",
            new TransitionWorkItemRequest("New"));
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);

        using var historyResponse = await _client.GetAsync($"{workItemsPath}/{workItem.Id}/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<PagedResponse<WorkItemHistoryResponse>>();
        Assert.NotNull(history);
        Assert.Equal(9, history.TotalCount);
    }

    [Fact]
    public async Task Work_items_support_filtering_pagination_comments_and_metadata_updates()
    {
        var admin = await RegisterAndAuthorizeAsync("work-item-list-admin@example.com");
        using var organizationResponse = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Work Item Lists", "work-item-lists", null));
        var organization = await organizationResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(organization);
        var projectPath = $"/api/organizations/{organization.Id}/projects";
        using var projectResponse = await _client.PostAsJsonAsync(projectPath,
            new CreateProjectRequest("Filter Project", "FLT", null));
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        var workItemsPath = $"{projectPath}/{project.Id}/work-items";

        using var taskResponse = await _client.PostAsJsonAsync(workItemsPath,
            new CreateWorkItemRequest("Task", "Zeta implementation", null, "High", null, null));
        Assert.Equal(HttpStatusCode.Created, taskResponse.StatusCode);
        var task = await taskResponse.Content.ReadFromJsonAsync<WorkItemResponse>();
        Assert.NotNull(task);
        using var bugResponse = await _client.PostAsJsonAsync(workItemsPath,
            new CreateWorkItemRequest("Bug", "Alpha dashboard defect", "Date formatting is wrong", "Critical", null, null));
        Assert.Equal(HttpStatusCode.Created, bugResponse.StatusCode);
        var bug = await bugResponse.Content.ReadFromJsonAsync<WorkItemResponse>();
        Assert.NotNull(bug);

        using var badFilter = await _client.GetAsync($"{workItemsPath}?status=Whatever");
        Assert.Equal(HttpStatusCode.BadRequest, badFilter.StatusCode);
        using var badSort = await _client.GetAsync($"{workItemsPath}?sortBy=sql");
        Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);
        using var badPaging = await _client.GetAsync($"{workItemsPath}?page=0&pageSize=1000");
        Assert.Equal(HttpStatusCode.BadRequest, badPaging.StatusCode);

        using var listResponse = await _client.GetAsync($"{workItemsPath}?type=Bug&priority=Critical&search=dashboard&page=1&pageSize=1&sortBy=title&sortDirection=asc");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var filtered = await listResponse.Content.ReadFromJsonAsync<PagedResponse<WorkItemResponse>>();
        Assert.NotNull(filtered);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal(bug.Id, Assert.Single(filtered.Items).Id);

        using var pagedResponse = await _client.GetAsync($"{workItemsPath}?page=1&pageSize=1&sortBy=title&sortDirection=asc");
        var paged = await pagedResponse.Content.ReadFromJsonAsync<PagedResponse<WorkItemResponse>>();
        Assert.NotNull(paged);
        Assert.Equal(2, paged.TotalCount);
        Assert.Equal("Alpha dashboard defect", Assert.Single(paged.Items).Title);

        using var priorityOrderedResponse = await _client.GetAsync($"{workItemsPath}?sortBy=priority&sortDirection=asc");
        var priorityOrdered = await priorityOrderedResponse.Content.ReadFromJsonAsync<PagedResponse<WorkItemResponse>>();
        Assert.NotNull(priorityOrdered);
        Assert.Equal("High", priorityOrdered.Items[0].Priority);
        Assert.Equal("Critical", priorityOrdered.Items[1].Priority);

        using var update = await _client.PutAsJsonAsync($"{workItemsPath}/{bug.Id}",
            new UpdateWorkItemRequest("Alpha dashboard date defect", "Date formatting is inconsistent", "High", null));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<WorkItemResponse>();
        Assert.NotNull(updated);
        Assert.Equal("High", updated.Priority);

        using var addComment = await _client.PostAsJsonAsync($"{workItemsPath}/{bug.Id}/comments",
            new AddWorkItemCommentRequest("  Reproduced in the overview.  "));
        Assert.Equal(HttpStatusCode.Created, addComment.StatusCode);
        var comment = await addComment.Content.ReadFromJsonAsync<WorkItemCommentResponse>();
        Assert.NotNull(comment);
        Assert.Equal("Reproduced in the overview.", comment.Body);
        using var listComments = await _client.GetAsync($"{workItemsPath}/{bug.Id}/comments?page=1&pageSize=10");
        var comments = await listComments.Content.ReadFromJsonAsync<PagedResponse<WorkItemCommentResponse>>();
        Assert.NotNull(comments);
        Assert.Equal(comment.Id, Assert.Single(comments.Items).Id);
        using var oversizedComment = await _client.PostAsJsonAsync($"{workItemsPath}/{bug.Id}/comments",
            new AddWorkItemCommentRequest(new string('x', 4001)));
        Assert.Equal(HttpStatusCode.BadRequest, oversizedComment.StatusCode);

        using var itemHistoryResponse = await _client.GetAsync($"{workItemsPath}/{bug.Id}/history");
        var itemHistory = await itemHistoryResponse.Content.ReadFromJsonAsync<PagedResponse<WorkItemHistoryResponse>>();
        Assert.NotNull(itemHistory);
        Assert.Equal(3, itemHistory.TotalCount);
    }

    [Fact]
    public async Task Work_items_are_tenant_scoped_and_viewer_members_are_read_only()
    {
        var admin = await RegisterAndAuthorizeAsync("work-item-access-admin@example.com");
        using var organizationResponse = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Work Item Access", "work-item-access", null));
        var organization = await organizationResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(organization);
        var projectPath = $"/api/organizations/{organization.Id}/projects";
        using var projectResponse = await _client.PostAsJsonAsync(projectPath,
            new CreateProjectRequest("Access Project", "ACL", null));
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        var workItemsPath = $"{projectPath}/{project.Id}/work-items";
        using var create = await _client.PostAsJsonAsync(workItemsPath,
            new CreateWorkItemRequest("Bug", "Tenant-only defect", null, "Medium", null, null));
        var workItem = await create.Content.ReadFromJsonAsync<WorkItemResponse>();
        Assert.NotNull(workItem);

        var viewer = await RegisterAndAuthorizeAsync("work-item-viewer@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var addViewer = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest("work-item-viewer@example.com", "Viewer"));
        Assert.Equal(HttpStatusCode.OK, addViewer.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewer.AccessToken);
        using var allowedRead = await _client.GetAsync($"{workItemsPath}/{workItem.Id}");
        Assert.Equal(HttpStatusCode.OK, allowedRead.StatusCode);
        using var deniedCreate = await _client.PostAsJsonAsync(workItemsPath,
            new CreateWorkItemRequest("Task", "Viewer write", null, "Low", null, null));
        Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);
        using var deniedComment = await _client.PostAsJsonAsync($"{workItemsPath}/{workItem.Id}/comments",
            new AddWorkItemCommentRequest("Read-only member"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedComment.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var viewerAssignment = await _client.PutAsJsonAsync($"{workItemsPath}/{workItem.Id}/assignee",
            new AssignWorkItemRequest(viewer.UserId));
        Assert.Equal(HttpStatusCode.NotFound, viewerAssignment.StatusCode);

        using var otherOrganizationResponse = await _client.PostAsJsonAsync("/api/organizations",
            new CreateOrganizationRequest("Another Work Item Org", "another-work-item-org", null));
        var otherOrganization = await otherOrganizationResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(otherOrganization);
        using var otherProjectResponse = await _client.PostAsJsonAsync($"/api/organizations/{otherOrganization.Id}/projects",
            new CreateProjectRequest("Other Access Project", "OAC", null));
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(otherProject);
        using var crossTenant = await _client.GetAsync(
            $"/api/organizations/{otherOrganization.Id}/projects/{otherProject.Id}/work-items/{workItem.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
    }

    private async Task<AuthResponse> RegisterAndAuthorizeAsync(string email)
    {
        using var registration = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Test User", "correct horse battery"));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var auth = await registration.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
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
