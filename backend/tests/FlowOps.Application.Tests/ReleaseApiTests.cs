using System.Net;
using System.Net.Http.Json;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Dashboard;
using FlowOps.Contracts.Incidents;
using FlowOps.Contracts.Notifications;
using FlowOps.Contracts.Organizations;
using FlowOps.Contracts.Releases;
using FlowOps.Contracts.WorkItems;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace FlowOps.Application.Tests;

public sealed partial class AuthApiTests
{
    [Theory]
    [InlineData("Successful")]
    [InlineData("Failed")]
    public async Task Release_complete_lifecycle_preserves_links_outcomes_audit_notifications_and_dashboard(string result)
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "releases");
        using var environmentResponse = await _client.PostAsJsonAsync(path + "/environments", new CreateEnvironmentRequest("Production")); Assert.Equal(HttpStatusCode.Created, environmentResponse.StatusCode);
        var environment = (await environmentResponse.Content.ReadFromJsonAsync<ReleaseEnvironmentResponse>())!;
        using var duplicate = await _client.PostAsJsonAsync(path + "/environments", new CreateEnvironmentRequest(" production ")); Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var incidentResponse = await _client.PostAsJsonAsync(s.Path.Replace("work-items", "incidents"), new CreateIncidentRequest("Service outage", null, "API", "Critical", s.Recipient.UserId)); var incident = (await incidentResponse.Content.ReadFromJsonAsync<IncidentResponse>())!;
        Authorize(s.Recipient);
        using var create = await _client.PostAsJsonAsync(path, new CreateReleaseRequest("Public API", "1.2.3", environment.Id, "Deployment notes")); Assert.Equal(HttpStatusCode.Created, create.StatusCode); var release = (await create.Content.ReadFromJsonAsync<ReleaseResponse>())!;
        using var linked = await _client.PutAsJsonAsync($"{path}/{release.Id}/links", new SetReleaseLinksRequest(release.Version, [s.Item.Id, s.Item.Id], [incident.Id])); Assert.Equal(HttpStatusCode.OK, linked.StatusCode); release = (await linked.Content.ReadFromJsonAsync<ReleaseResponse>())!;
        Assert.Equal(1, release.WorkItemCount); Assert.Equal(1, release.IncidentCount);
        using var invalidApproval = await _client.PostAsJsonAsync($"{path}/{release.Id}/transitions", new TransitionReleaseRequest(release.Version, "Approved")); Assert.Equal(HttpStatusCode.Forbidden, invalidApproval.StatusCode);
        Authorize(s.Admin); release = await ReleaseTransition(path, release, "Approved");
        using var frozen = await _client.PutAsJsonAsync($"{path}/{release.Id}", new UpdateReleaseRequest(release.Version, "Rewrite notes")); Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);
        using var frozenLinks = await _client.PutAsJsonAsync($"{path}/{release.Id}/links", new SetReleaseLinksRequest(release.Version, [], [])); Assert.Equal(HttpStatusCode.Conflict, frozenLinks.StatusCode);
        release = await ReleaseTransition(path, release, "Deploying");
        using var archive = await _client.DeleteAsync($"{path}/environments/{environment.Id}"); Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        release = await ReleaseTransition(path, release, result, "Verified deployment outcome"); Assert.NotNull(release.CompletedAtUtc); Assert.Equal("Verified deployment outcome", release.Outcome);
        var links = (await _client.GetFromJsonAsync<ReleaseLinksResponse>($"{path}/{release.Id}/links"))!; Assert.Equal(s.Item.Id, links.WorkItems.Single().Id); Assert.Equal(incident.Id, links.Incidents.Single().Id);
        var timeline = (await _client.GetFromJsonAsync<PagedResponse<ReleaseTimelineResponse>>($"{path}/{release.Id}/timeline"))!; Assert.Equal(5, timeline.TotalCount);
        var dash = (await _client.GetFromJsonAsync<DashboardResponse>($"/api/organizations/{s.Organization.Id}/dashboard"))!; Assert.Equal(1, dash.OpenWorkItems); Assert.Equal(1, dash.CriticalIncidents); Assert.Equal(1, dash.ActiveIncidents); Assert.Single(dash.TeamWorkload); Assert.Equal(s.Recipient.UserId, dash.TeamWorkload[0].UserId);
        Assert.Equal(result == "Successful" ? 1 : 0, dash.SuccessfulReleasesInPeriod); Assert.Equal(result == "Failed" ? 1 : 0, dash.FailedReleasesInPeriod); Assert.Equal(release.Id, dash.RecentReleases.Single().Id);
        Authorize(s.Recipient); var notifications = (await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications"))!;
        Assert.Equal(3, notifications.Items.Count(n => n.ReleaseId == release.Id)); Assert.All(notifications.Items.Where(n => n.ReleaseId == release.Id), n => { Assert.Null(n.IncidentId); Assert.Null(n.WorkItemId); });
    }
    [Theory]
    [InlineData("Admin", true, true)]
    [InlineData("TeamLead", true, true)]
    [InlineData("Engineer", true, false)]
    [InlineData("QA", true, false)]
    [InlineData("Viewer", false, false)]
    public async Task Release_and_environment_permissions_are_scoped_to_current_membership(string role, bool write, bool lead)
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "releases");
        using var e = await _client.PostAsJsonAsync(path + "/environments", new CreateEnvironmentRequest("Staging")); var env = (await e.Content.ReadFromJsonAsync<ReleaseEnvironmentResponse>())!;
        using var c = await _client.PostAsJsonAsync(path, new CreateReleaseRequest("API", "v1", env.Id, null)); var release = (await c.Content.ReadFromJsonAsync<ReleaseResponse>())!;
        using var change = await _client.PutAsJsonAsync($"/api/organizations/{s.Organization.Id}/members/{s.Recipient.UserId}/role", new UpdateOrganizationMemberRoleRequest(role)); Assert.Equal(HttpStatusCode.OK, change.StatusCode); Authorize(s.Recipient);
        using var read = await _client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var create = await _client.PostAsJsonAsync(path, new CreateReleaseRequest("API", "v1", env.Id, null)); Assert.Equal(write ? HttpStatusCode.Created : HttpStatusCode.Forbidden, create.StatusCode);
        using var manage = await _client.PostAsJsonAsync(path + "/environments", new CreateEnvironmentRequest("Canary")); Assert.Equal(lead ? HttpStatusCode.Created : HttpStatusCode.Forbidden, manage.StatusCode);
        using var transition = await _client.PostAsJsonAsync($"{path}/{release.Id}/transitions", new TransitionReleaseRequest(release.Version, "Approved")); Assert.Equal(lead ? HttpStatusCode.OK : HttpStatusCode.Forbidden, transition.StatusCode);
        using var editOthers = await _client.PutAsJsonAsync($"{path}/{release.Id}", new UpdateReleaseRequest((await _client.GetFromJsonAsync<ReleaseResponse>($"{path}/{release.Id}"))!.Version, "Other draft")); Assert.Equal(lead ? HttpStatusCode.Conflict : HttpStatusCode.Forbidden, editOthers.StatusCode);
        using var dashboard = await _client.GetAsync($"/api/organizations/{s.Organization.Id}/dashboard"); Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var metrics = (await dashboard.Content.ReadFromJsonAsync<DashboardResponse>())!;
        Assert.Equal(s.Recipient.UserId, Assert.Single(metrics.TeamWorkload).UserId);
    }
    [Fact]
    public async Task Stale_versions_foreign_links_archived_environments_and_cross_tenant_ids_are_rejected()
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "releases");
        using var e = await _client.PostAsJsonAsync(path + "/environments", new CreateEnvironmentRequest("Testing")); var env = (await e.Content.ReadFromJsonAsync<ReleaseEnvironmentResponse>())!;
        using var c = await _client.PostAsJsonAsync(path, new CreateReleaseRequest("API", "v2", env.Id, null)); var release = (await c.Content.ReadFromJsonAsync<ReleaseResponse>())!; var original = release;
        using var update = await _client.PutAsJsonAsync($"{path}/{release.Id}", new UpdateReleaseRequest(release.Version, "Valid notes")); release = (await update.Content.ReadFromJsonAsync<ReleaseResponse>())!;
        using var stale = await _client.PutAsJsonAsync($"{path}/{release.Id}/links", new SetReleaseLinksRequest(original.Version, [s.Item.Id], [])); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var badLink = await _client.PutAsJsonAsync($"{path}/{release.Id}/links", new SetReleaseLinksRequest(release.Version, [Guid.NewGuid()], [])); Assert.Equal(HttpStatusCode.NotFound, badLink.StatusCode);
        var links = (await _client.GetFromJsonAsync<ReleaseLinksResponse>($"{path}/{release.Id}/links"))!; Assert.Empty(links.WorkItems);
        using var archived = await _client.DeleteAsync($"{path}/environments/{env.Id}"); Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        using var deniedApproval = await _client.PostAsJsonAsync($"{path}/{release.Id}/transitions", new TransitionReleaseRequest(release.Version, "Approved")); Assert.Equal(HttpStatusCode.Conflict, deniedApproval.StatusCode);
        using var deniedCreate = await _client.PostAsJsonAsync(path, new CreateReleaseRequest("API", "v2", env.Id, null)); Assert.Equal(HttpStatusCode.NotFound, deniedCreate.StatusCode);
        await RegisterAndAuthorizeAsync("release-outsider@example.test");
        foreach (var suffix in new[] { "", "/links", "/timeline" }) { using var denied = await _client.GetAsync($"{path}/{release.Id}{suffix}"); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); }
        using var hiddenDashboard = await _client.GetAsync($"/api/organizations/{s.Organization.Id}/dashboard"); Assert.Equal(HttpStatusCode.NotFound, hiddenDashboard.StatusCode);
        Authorize(s.Admin); using var archiveProject = await _client.DeleteAsync($"/api/organizations/{s.Organization.Id}/projects/{s.Item.ProjectId}"); Assert.Equal(HttpStatusCode.NoContent, archiveProject.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<DashboardResponse>($"/api/organizations/{s.Organization.Id}/dashboard"))!.RecentReleases);
    }
    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("status=999")]
    [InlineData("environmentId=invalid")]
    public async Task Release_filters_reject_invalid_boundaries(string query) { var s = await CreateNotificationScenarioAsync(); using var r = await _client.GetAsync($"{s.Path.Replace("work-items", "releases")}?{query}"); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); }
    [Theory]
    [InlineData(0, 1)]
    [InlineData(91, 1)]
    [InlineData(30, 0)]
    [InlineData(30, 2147483647)]
    public async Task Dashboard_bounds_are_validated(int days, int page) { var s = await CreateNotificationScenarioAsync(); using var r = await _client.GetAsync($"/api/organizations/{s.Organization.Id}/dashboard?periodDays={days}&workloadPage={page}"); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); }
    [Theory]
    [InlineData("bad/version")]
    [InlineData(" ")]
    [InlineData("1;DROP")]
    public async Task Release_versions_reject_invalid_names(string version) { var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "releases"); using var e = await _client.PostAsJsonAsync(path + "/environments", new CreateEnvironmentRequest("Testing")); var env = (await e.Content.ReadFromJsonAsync<ReleaseEnvironmentResponse>())!; using var c = await _client.PostAsJsonAsync(path, new CreateReleaseRequest("API", version, env.Id, null)); Assert.Equal(HttpStatusCode.BadRequest, c.StatusCode); }
    [Fact]
    public async Task Maximum_outcome_and_unicode_notification_text_fit_their_persistence_contracts()
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "releases");
        using var e = await _client.PostAsJsonAsync(path + "/environments", new CreateEnvironmentRequest(new string('界', 80))); var env = (await e.Content.ReadFromJsonAsync<ReleaseEnvironmentResponse>())!;
        Authorize(s.Recipient); using var c = await _client.PostAsJsonAsync(path, new CreateReleaseRequest(new string('界', 120), new string('v', 80), env.Id, new string('界', 8000))); Assert.Equal(HttpStatusCode.Created, c.StatusCode); var release = (await c.Content.ReadFromJsonAsync<ReleaseResponse>())!;
        Authorize(s.Admin); release = await ReleaseTransition(path, release, "Approved"); release = await ReleaseTransition(path, release, "Deploying"); release = await ReleaseTransition(path, release, "Failed", new string('界', 4000));
        Assert.Equal(4000, release.Outcome!.Length); Authorize(s.Recipient); var inbox = (await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications"))!; Assert.All(inbox.Items, n => Assert.InRange(n.Message.Length, 1, 300));
    }
    [Fact]
    public async Task Dashboard_uses_resolution_time_and_current_scope_and_excludes_archived_projects()
    {
        var s = await CreateNotificationScenarioAsync();
        var now = _factory.Clock.GetUtcNow().UtcDateTime;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
            var item = await db.WorkItems.SingleAsync(w => w.Id == s.Item.Id);
            foreach (var status in new[] { FlowOps.Domain.Enums.WorkItemStatus.Triaged, FlowOps.Domain.Enums.WorkItemStatus.Assigned,
                FlowOps.Domain.Enums.WorkItemStatus.InProgress, FlowOps.Domain.Enums.WorkItemStatus.CodeReview,
                FlowOps.Domain.Enums.WorkItemStatus.QA, FlowOps.Domain.Enums.WorkItemStatus.Resolved }) item.TransitionTo(status, now.AddDays(-10));
            // Updating metadata today must not pretend this item was resolved today.
            item.UpdateDetails(item.Title, "Recent metadata", item.Priority, null, now);
            await db.SaveChangesAsync();
        }
        var route = $"/api/organizations/{s.Organization.Id}/dashboard";
        var recent = (await _client.GetFromJsonAsync<DashboardResponse>(route + "?periodDays=7"))!;
        Assert.Equal(0, recent.ResolvedWorkItemsInPeriod); Assert.Equal(0, recent.OpenWorkItems); Assert.Empty(recent.TeamWorkload);
        Assert.Equal(1, (await _client.GetFromJsonAsync<DashboardResponse>(route + "?periodDays=30"))!.ResolvedWorkItemsInPeriod);
        using var wrongProject = await _client.GetAsync(route + $"?projectId={Guid.NewGuid()}"); Assert.Equal(HttpStatusCode.NotFound, wrongProject.StatusCode);
        using var archived = await _client.DeleteAsync($"/api/organizations/{s.Organization.Id}/projects/{s.Item.ProjectId}"); Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        Assert.Equal(0, (await _client.GetFromJsonAsync<DashboardResponse>(route))!.ResolvedWorkItemsInPeriod);
        using var hiddenProject = await _client.GetAsync(route + $"?projectId={s.Item.ProjectId}"); Assert.Equal(HttpStatusCode.NotFound, hiddenProject.StatusCode);
    }

    private async Task<ReleaseResponse> ReleaseTransition(string path, ReleaseResponse release, string status, string? outcome = null)
    {
        using var response = await _client.PostAsJsonAsync($"{path}/{release.Id}/transitions", new TransitionReleaseRequest(release.Version, status, outcome));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected release transition {status}; got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); return (await response.Content.ReadFromJsonAsync<ReleaseResponse>())!;
    }
}
