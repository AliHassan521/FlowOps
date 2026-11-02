using System.Net;
using System.Net.Http.Json;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Incidents;
using FlowOps.Contracts.Organizations;
using FlowOps.Contracts.Sla;
using FlowOps.Contracts.Notifications;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace FlowOps.Application.Tests;

public sealed partial class AuthApiTests
{
    [Fact]
    public async Task Incident_complete_flow_records_resolution_timeline_comments_notifications_and_metrics()
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "incidents");
        using var create = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("API outage", "Elevated errors", "Public API", "Critical", s.Recipient.UserId));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode); var item = (await create.Content.ReadFromJsonAsync<IncidentResponse>())!;
        Assert.StartsWith("INC-", item.Number); Assert.Equal("Open", item.Status);
        var stale = item;
        using var edit = await _client.PutAsJsonAsync($"{path}/{item.Id}", new UpdateIncidentRequest(item.Version, "API outage confirmed", null, "Public API", "Critical"));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode); item = (await edit.Content.ReadFromJsonAsync<IncidentResponse>())!;
        using var oldEdit = await _client.PutAsJsonAsync($"{path}/{item.Id}", new UpdateIncidentRequest(stale.Version, "Stale overwrite", null, "Public API", "Low"));
        Assert.Equal(HttpStatusCode.Conflict, oldEdit.StatusCode);
        Authorize(s.Recipient);
        using var comment = await _client.PostAsJsonAsync($"{path}/{item.Id}/comments", new AddIncidentCommentRequest("<script>alert('data')</script>"));
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        foreach (var status in new[] { "Investigating", "Mitigated" }) item = await IncidentTransition(path, item, status);
        using var missingResolution = await _client.PostAsJsonAsync($"{path}/{item.Id}/transitions", new TransitionIncidentRequest(item.Version, "Resolved"));
        Assert.Equal(HttpStatusCode.BadRequest, missingResolution.StatusCode);
        item = await IncidentTransition(path, item, "Resolved", "Rolled back deployment", "Invalid configuration");
        Assert.NotNull(item.EndedAtUtc); Assert.Equal(s.Recipient.UserId, item.ResolvedByUserId);
        using var deniedClose = await _client.PostAsJsonAsync($"{path}/{item.Id}/transitions", new TransitionIncidentRequest(item.Version, "Closed"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedClose.StatusCode);
        Authorize(s.Admin); item = await IncidentTransition(path, item, "Closed");
        var timeline = (await _client.GetFromJsonAsync<PagedResponse<IncidentTimelineResponse>>($"{path}/{item.Id}/timeline"))!;
        Assert.Equal(7, timeline.TotalCount); Assert.Equal(4, timeline.Items.Count(e => e.Action == "Incident.StatusChanged"));
        Assert.Equal("<script>alert('data')</script>", (await _client.GetFromJsonAsync<PagedResponse<IncidentCommentResponse>>($"{path}/{item.Id}/comments"))!.Items.Single().Body);
        var summary = (await _client.GetFromJsonAsync<IncidentSummaryResponse>($"{path}/summary"))!;
        Assert.Equal(0, summary.ActiveCount); Assert.Equal(1, summary.ResolvedCount);
        using var forbiddenRewrite = await _client.PutAsJsonAsync($"{path}/{item.Id}", new UpdateIncidentRequest(item.Version, "Rewrite history", null, "API", "Low"));
        Assert.Equal(HttpStatusCode.Conflict, forbiddenRewrite.StatusCode);
        Authorize(s.Recipient);
        var inbox = (await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications"))!;
        Assert.Contains(inbox.Items, n => n.IncidentId == item.Id && n.WorkItemId == null && n.Kind == "Incident.Assigned");
        Assert.Contains(inbox.Items, n => n.WorkItemId == s.Item.Id && n.IncidentId == null);
    }
    [Theory]
    [InlineData("Admin", true, true)]
    [InlineData("TeamLead", true, true)]
    [InlineData("Engineer", true, false)]
    [InlineData("QA", true, false)]
    [InlineData("Viewer", false, false)]
    public async Task Incident_permissions_apply_to_creation_assignment_SLA_and_comments(string role, bool write, bool manage)
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "incidents");
        using var created = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("Incident permissions", null, "API", "High", s.Admin.UserId));
        var item = (await created.Content.ReadFromJsonAsync<IncidentResponse>())!;
        using var changed = await _client.PutAsJsonAsync($"/api/organizations/{s.Organization.Id}/members/{s.Recipient.UserId}/role", new UpdateOrganizationMemberRoleRequest(role));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode); Authorize(s.Recipient);
        using var read = await _client.GetAsync($"{path}/{item.Id}"); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var unassigned = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("Unassigned incident", null, "API", "Low")); Assert.Equal(write ? HttpStatusCode.Created : HttpStatusCode.Forbidden, unassigned.StatusCode);
        using var bypass = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("Assignment bypass", null, "API", "High", s.Admin.UserId)); Assert.Equal(manage ? HttpStatusCode.Created : HttpStatusCode.Forbidden, bypass.StatusCode);
        using var assign = await _client.PutAsJsonAsync($"{path}/{item.Id}/owner", new AssignIncidentRequest(item.Version, s.Recipient.UserId)); Assert.Equal(manage ? HttpStatusCode.OK : HttpStatusCode.Forbidden, assign.StatusCode);
        using var comment = await _client.PostAsJsonAsync($"{path}/{item.Id}/comments", new AddIncidentCommentRequest("Investigation note")); Assert.Equal(write ? HttpStatusCode.Created : HttpStatusCode.Forbidden, comment.StatusCode);
        if (!manage) { using var sla = await _client.PostAsJsonAsync($"{path}/{item.Id}/sla", new AttachIncidentSlaRequest(item.Version, Guid.NewGuid())); Assert.Equal(HttpStatusCode.Forbidden, sla.StatusCode); }
    }
    [Fact]
    public async Task Incident_SLA_shares_the_worker_without_duplicate_events_or_reset_and_completion_stops_its_clock()
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "incidents");
        using var created = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("Monitored outage", null, "API", "High", s.Recipient.UserId)); var item = (await created.Content.ReadFromJsonAsync<IncidentResponse>())!;
        using var policyResponse = await _client.PostAsJsonAsync($"/api/organizations/{s.Organization.Id}/sla-policies", new CreateSlaPolicyRequest("Incident response", 2, 1)); var policy = (await policyResponse.Content.ReadFromJsonAsync<SlaPolicyResponse>())!;
        using var attached = await _client.PostAsJsonAsync($"{path}/{item.Id}/sla", new AttachIncidentSlaRequest(item.Version, policy.Id)); Assert.Equal(HttpStatusCode.Created, attached.StatusCode);
        using var workAttached = await _client.PostAsJsonAsync($"{s.Path}/{s.Item.Id}/sla", new AttachWorkItemSlaRequest(policy.Id)); Assert.Equal(HttpStatusCode.Created, workAttached.StatusCode);
        item = (await _client.GetFromJsonAsync<IncidentResponse>($"{path}/{item.Id}"))!;
        using var reset = await _client.PostAsJsonAsync($"{path}/{item.Id}/sla", new AttachIncidentSlaRequest(item.Version, policy.Id)); Assert.Equal(HttpStatusCode.Conflict, reset.StatusCode);
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await ProcessAllDue(); await ProcessAllDue();
        Assert.Equal(1, (await _client.GetFromJsonAsync<IncidentSummaryResponse>($"{path}/summary"))!.AtRiskCount);
        using var archive = await _client.DeleteAsync($"/api/organizations/{s.Organization.Id}/sla-policies/{policy.Id}"); Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        _factory.Clock.Advance(TimeSpan.FromMinutes(1)); await ProcessAllDue(); await ProcessAllDue();
        var state = (await _client.GetFromJsonAsync<IncidentSlaStateResponse>($"{path}/{item.Id}/sla"))!; Assert.Equal("Breached", state.Sla!.Status);
        item = (await _client.GetFromJsonAsync<IncidentResponse>($"{path}/{item.Id}"))!;
        item = await IncidentTransition(path, item, "Investigating"); item = await IncidentTransition(path, item, "Mitigated"); item = await IncidentTransition(path, item, "Resolved", "Recovered service");
        var completed = (await _client.GetFromJsonAsync<IncidentSlaStateResponse>($"{path}/{item.Id}/sla"))!.Sla!; Assert.NotNull(completed.CompletedAtUtc); Assert.NotNull(completed.BreachedAtUtc);
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.EntityId == item.Id && a.Action == "Incident.SlaWarning"));
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.EntityId == item.Id && a.Action == "Incident.SlaBreached"));
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.EntityId == s.Item.Id && a.Action == "Sla.Breached"));
    }
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task Incident_resolution_measures_early_and_late_completion_without_worker_ticks(int minutes, bool breached)
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "incidents");
        using var create = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("Resolution timing", null, "API", "High", s.Recipient.UserId)); var item = (await create.Content.ReadFromJsonAsync<IncidentResponse>())!;
        using var p = await _client.PostAsJsonAsync($"/api/organizations/{s.Organization.Id}/sla-policies", new CreateSlaPolicyRequest("Resolution deadline", 2, 0)); var policy = (await p.Content.ReadFromJsonAsync<SlaPolicyResponse>())!;
        using var attach = await _client.PostAsJsonAsync($"{path}/{item.Id}/sla", new AttachIncidentSlaRequest(item.Version, policy.Id)); Assert.Equal(HttpStatusCode.Created, attach.StatusCode);
        item = (await _client.GetFromJsonAsync<IncidentResponse>($"{path}/{item.Id}"))!; item = await IncidentTransition(path, item, "Investigating"); item = await IncidentTransition(path, item, "Mitigated");
        _factory.Clock.Advance(TimeSpan.FromMinutes(minutes)); item = await IncidentTransition(path, item, "Resolved", "Service recovered");
        var state = (await _client.GetFromJsonAsync<IncidentSlaStateResponse>($"{path}/{item.Id}/sla"))!.Sla!; Assert.Equal(breached ? "Breached" : "Met", state.Status); Assert.Equal(breached, state.BreachedAtUtc.HasValue);
        _factory.Clock.Advance(TimeSpan.FromDays(1)); await ProcessAllDue();
        Assert.Equal(state, (await _client.GetFromJsonAsync<IncidentSlaStateResponse>($"{path}/{item.Id}/sla"))!.Sla);
    }
    [Theory]
    [InlineData("")]
    [InlineData("/comments")]
    [InlineData("/timeline")]
    [InlineData("/sla")]
    public async Task Incidents_are_not_visible_to_outsiders_or_foreign_projects(string suffix)
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "incidents");
        using var c = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("Private incident", null, "API", "Medium")); var item = (await c.Content.ReadFromJsonAsync<IncidentResponse>())!;
        await RegisterAndAuthorizeAsync("incident-outsider@example.test");
        using var denied = await _client.GetAsync($"{path}/{item.Id}{suffix}"); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Authorize(s.Admin); var wrongPath = path.Replace(s.Item.ProjectId.ToString(), Guid.NewGuid().ToString());
        using var swapped = await _client.GetAsync($"{wrongPath}/{item.Id}{suffix}"); Assert.Equal(HttpStatusCode.NotFound, swapped.StatusCode);
        using var archive = await _client.DeleteAsync($"/api/organizations/{s.Organization.Id}/projects/{s.Item.ProjectId}"); Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        using var archived = await _client.GetAsync($"{path}/{item.Id}{suffix}"); Assert.Equal(HttpStatusCode.NotFound, archived.StatusCode);
        Assert.Equal(0, (await _client.GetFromJsonAsync<IncidentSummaryResponse>($"/api/organizations/{s.Organization.Id}/incident-summary"))!.ActiveCount);
    }
    [Theory]
    [InlineData("page=0")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("pageSize=101")]
    [InlineData("status=999")]
    [InlineData("severity=0")]
    [InlineData("ownerUserId=invalid")]
    public async Task Incident_filters_validate_pagination_and_enum_names(string query)
    {
        var s = await CreateNotificationScenarioAsync(); using var response = await _client.GetAsync($"{s.Path.Replace("work-items", "incidents")}?{query}"); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task A_stale_incident_transition_rolls_back_audit_and_notification_side_effects()
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "incidents");
        using var c = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("Concurrency test", null, "API", "High", s.Recipient.UserId)); var item = (await c.Content.ReadFromJsonAsync<IncidentResponse>())!;
        using var staleScope = _factory.Services.CreateScope(); var staleDb = staleScope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
        await staleDb.Incidents.SingleAsync(i => i.Id == item.Id);
        item = await IncidentTransition(path, item, "Investigating");
        var service = staleScope.ServiceProvider.GetRequiredService<IIncidentService>();
        var staleVersion = staleDb.Incidents.Local.Single().Version;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.TransitionAsync(s.Admin.UserId, s.Organization.Id, s.Item.ProjectId, item.Id, new TransitionIncidentRequest(staleVersion, "Investigating"), default));
        using var verifyScope = _factory.Services.CreateScope(); var db = verifyScope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.EntityId == item.Id && a.Action == "Incident.StatusChanged"));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.IncidentId == item.Id && n.Kind == "Incident.StatusChanged"));
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{invalid")]
    [InlineData("{\"title\":\"Bad severity\",\"affectedService\":\"API\",\"severity\":\"99\"}")]
    [InlineData("{\"title\":null,\"affectedService\":\"API\",\"severity\":\"High\"}")]
    [InlineData("""{"title":"Missing service","affectedService":" ","severity":"High"}""")]
    public async Task Incident_creation_rejects_malformed_and_invalid_bodies(string json)
    {
        var s = await CreateNotificationScenarioAsync(); using var response = await _client.PostAsync(s.Path.Replace("work-items", "incidents"), new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task Incident_owner_must_be_a_current_writable_member_and_overposted_fields_are_ignored()
    {
        var s = await CreateNotificationScenarioAsync(); var path = s.Path.Replace("work-items", "incidents");
        using var invalid = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("Foreign owner", null, "API", "High", Guid.NewGuid())); Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        using var viewer = await _client.PutAsJsonAsync($"/api/organizations/{s.Organization.Id}/members/{s.Recipient.UserId}/role", new UpdateOrganizationMemberRoleRequest("Viewer")); Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);
        using var invalidViewer = await _client.PostAsJsonAsync(path, new CreateIncidentRequest("Viewer owner", null, "API", "High", s.Recipient.UserId)); Assert.Equal(HttpStatusCode.NotFound, invalidViewer.StatusCode);
        using var overpost = await _client.PostAsJsonAsync(path, new { title = "Overpost attempt", affectedService = "API", severity = "High", status = "Closed", reporterUserId = s.Recipient.UserId, endedAtUtc = DateTime.UtcNow, rootCause = "Fake" });
        Assert.Equal(HttpStatusCode.Created, overpost.StatusCode); var item = (await overpost.Content.ReadFromJsonAsync<IncidentResponse>())!; Assert.Equal("Open", item.Status); Assert.Equal(s.Admin.UserId, item.ReporterUserId); Assert.Null(item.EndedAtUtc); Assert.Null(item.RootCause);
        using var archived = await _client.DeleteAsync($"/api/organizations/{s.Organization.Id}/members/{s.Recipient.UserId}"); Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        Authorize(s.Recipient); using var revoked = await _client.GetAsync($"{path}/{item.Id}"); Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
    }
    private async Task<IncidentResponse> IncidentTransition(string path, IncidentResponse item, string status, string? resolution = null, string? rootCause = null)
    {
        using var response = await _client.PostAsJsonAsync($"{path}/{item.Id}/transitions", new TransitionIncidentRequest(item.Version, status, resolution, rootCause));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected transition {status}; got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<IncidentResponse>())!;
    }
    private async Task ProcessAllDue()
    {
        IReadOnlyList<Guid> ids; using (var scope = _factory.Services.CreateScope()) ids = await scope.ServiceProvider.GetRequiredService<ISlaProcessor>().GetDueIdsAsync(100, default);
        foreach (var id in ids) { using var scope = _factory.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<ISlaProcessor>().ProcessOneAsync(id, default); }
    }
}
