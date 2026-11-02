using System.Net;
using System.Net.Http.Json;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Sla;
using FlowOps.Contracts.Organizations;
using FlowOps.Contracts.Projects;
using FlowOps.Contracts.WorkItems;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOps.Application.Tests;

public sealed partial class AuthApiTests
{
    [Fact]
    public async Task Sla_policies_measurements_and_metrics_enforce_tenant_roles_and_fixed_deadlines()
    {
        var admin = await RegisterAndAuthorizeAsync("sla-owner@example.test");
        using var orgResponse = await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("SLA Team", "sla-api-team", null));
        var org = (await orgResponse.Content.ReadFromJsonAsync<OrganizationResponse>())!;
        var policiesPath = $"/api/organizations/{org.Id}/sla-policies";
        using var invalid = await _client.PostAsJsonAsync(policiesPath, new CreateSlaPolicyRequest("Bad warning", 60, 60));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var createPolicy = await _client.PostAsJsonAsync(policiesPath, new CreateSlaPolicyRequest("Critical", 240, 60));
        Assert.Equal(HttpStatusCode.Created, createPolicy.StatusCode);
        var policy = (await createPolicy.Content.ReadFromJsonAsync<SlaPolicyResponse>())!;
        using var duplicate = await _client.PostAsJsonAsync(policiesPath, new CreateSlaPolicyRequest("critical", 120, 30));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var projectResponse = await _client.PostAsJsonAsync($"/api/organizations/{org.Id}/projects", new CreateProjectRequest("SLA delivery", "SLA", null));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>())!;
        var itemPath = $"/api/organizations/{org.Id}/projects/{project.Id}/work-items";
        using var itemResponse = await _client.PostAsJsonAsync(itemPath, new CreateWorkItemRequest("Bug", "Resolve critical defect", null, "Critical", null, null));
        var item = (await itemResponse.Content.ReadFromJsonAsync<WorkItemResponse>())!;
        var slaPath = $"{itemPath}/{item.Id}/sla";
        Assert.Null((await _client.GetFromJsonAsync<WorkItemSlaStateResponse>(slaPath))!.Sla);
        using var attachment = await _client.PostAsJsonAsync(slaPath, new AttachWorkItemSlaRequest(policy.Id));
        Assert.Equal(HttpStatusCode.Created, attachment.StatusCode);
        var state = (await attachment.Content.ReadFromJsonAsync<WorkItemSlaStateResponse>())!;
        Assert.Equal("Active", state.Sla!.Status);
        Assert.Equal(state.ServerTimeUtc.AddHours(4), state.Sla.DeadlineAtUtc);
        Assert.Equal(state.ServerTimeUtc.AddHours(3), state.Sla.WarningAtUtc);
        Assert.Equal(DateTimeKind.Utc, state.Sla.DeadlineAtUtc.Kind);
        using var reset = await _client.PostAsJsonAsync(slaPath, new AttachWorkItemSlaRequest(policy.Id));
        Assert.Equal(HttpStatusCode.Conflict, reset.StatusCode);
        using var archive = await _client.DeleteAsync($"{policiesPath}/{policy.Id}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<PagedResponse<SlaPolicyResponse>>(policiesPath))!.Items);
        Assert.Single((await _client.GetFromJsonAsync<PagedResponse<SlaPolicyResponse>>(policiesPath + "?activeOnly=false"))!.Items);
        Assert.Equal(state.Sla.DeadlineAtUtc, (await _client.GetFromJsonAsync<WorkItemSlaStateResponse>(slaPath))!.Sla!.DeadlineAtUtc);

        var engineer = await RegisterAndAuthorizeAsync("sla-reader@example.test");
        Authorize(admin);
        using var add = await _client.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new AddOrganizationMemberRequest(engineer.Email, "Engineer"));
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Authorize(engineer);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(slaPath)).StatusCode);
        using var denied = await _client.PostAsJsonAsync(policiesPath, new CreateSlaPolicyRequest("Engineer policy", 60, 0));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedAttach = await _client.PostAsJsonAsync(slaPath, new AttachWorkItemSlaRequest(policy.Id));
        Assert.Equal(HttpStatusCode.Forbidden, deniedAttach.StatusCode);
        _factory.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal("AtRisk", (await _client.GetFromJsonAsync<WorkItemSlaStateResponse>(slaPath))!.Sla!.Status);
        var summaryPath = $"/api/organizations/{org.Id}/sla-summary";
        Assert.Equal(1, (await _client.GetFromJsonAsync<SlaSummaryResponse>(summaryPath))!.AtRiskCount);
        _factory.Clock.Advance(TimeSpan.FromHours(1));
        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ISlaProcessor>().ProcessOneAsync(state.Sla.Id, default));
        Assert.Equal("Breached", (await _client.GetFromJsonAsync<WorkItemSlaStateResponse>(slaPath))!.Sla!.Status);
        Assert.Equal(1, (await _client.GetFromJsonAsync<SlaSummaryResponse>(summaryPath))!.BreachedCount);
        var history = await _client.GetFromJsonAsync<PagedResponse<WorkItemHistoryResponse>>($"{itemPath}/{item.Id}/history");
        Assert.Contains(history!.Items, entry => entry.Action == "Sla.Breached" && entry.ActorName == "System" && entry.ActorUserId == null);
        Authorize(admin);
        using var revoke = await _client.DeleteAsync($"/api/organizations/{org.Id}/members/{engineer.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Authorize(engineer);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(slaPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(summaryPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(policiesPath)).StatusCode);
    }

    [Fact]
    public async Task Sla_policy_ids_cannot_cross_tenants_and_pagination_is_validated()
    {
        var actor = await RegisterAndAuthorizeAsync("sla-two-tenants@example.test");
        async Task<OrganizationResponse> CreateOrg(string slug)
        {
            using var response = await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("SLA " + slug, slug, null));
            return (await response.Content.ReadFromJsonAsync<OrganizationResponse>())!;
        }
        var first = await CreateOrg("sla-first"); var second = await CreateOrg("sla-second");
        using var policyResponse = await _client.PostAsJsonAsync($"/api/organizations/{first.Id}/sla-policies", new CreateSlaPolicyRequest("First policy", 60, 0));
        var policy = (await policyResponse.Content.ReadFromJsonAsync<SlaPolicyResponse>())!;
        using var projectResponse = await _client.PostAsJsonAsync($"/api/organizations/{second.Id}/projects", new CreateProjectRequest("Second project", "SEC", null));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>())!;
        var items = $"/api/organizations/{second.Id}/projects/{project.Id}/work-items";
        using var itemResponse = await _client.PostAsJsonAsync(items, new CreateWorkItemRequest("Task", "Second tenant task", null, "Low", null, null));
        var item = (await itemResponse.Content.ReadFromJsonAsync<WorkItemResponse>())!;
        using var crossTenant = await _client.PostAsJsonAsync($"{items}/{item.Id}/sla", new AttachWorkItemSlaRequest(policy.Id));
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
        using var invalidPage = await _client.GetAsync($"/api/organizations/{first.Id}/sla-policies?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>().WorkItemSlas.ToListAsync());
    }
}
