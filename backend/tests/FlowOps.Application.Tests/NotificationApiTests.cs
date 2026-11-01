using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FlowOps.Contracts.Auth;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Notifications;
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
    public async Task Notifications_are_persisted_scoped_paged_and_marked_read_without_duplicates()
    {
        var scenario = await CreateNotificationScenarioAsync();
        Authorize(scenario.Recipient);
        var page = await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications?pageSize=1");
        Assert.NotNull(page);
        var assigned = Assert.Single(page.Items);
        Assert.Equal("WorkItem.Assigned", assigned.Kind);
        Assert.Equal(scenario.Item.Id, assigned.WorkItemId);
        Assert.Equal(DateTimeKind.Utc, assigned.CreatedAtUtc.Kind);
        Assert.Equal(1, (await _client.GetFromJsonAsync<NotificationSummaryResponse>("/api/notifications/summary"))!.UnreadCount);

        Authorize(scenario.Admin);
        Assert.Empty((await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications"))!.Items);
        using var denied = await _client.PutAsJsonAsync($"/api/notifications/{assigned.Id}/read", new { });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var noChange = await _client.PutAsJsonAsync($"{scenario.Path}/{scenario.Item.Id}/assignee", new AssignWorkItemRequest(scenario.Recipient.UserId));
        Assert.Equal(HttpStatusCode.OK, noChange.StatusCode);
        using var transition = await _client.PostAsJsonAsync($"{scenario.Path}/{scenario.Item.Id}/transitions", new TransitionWorkItemRequest("Triaged"));
        Assert.Equal(HttpStatusCode.OK, transition.StatusCode);
        using var comment = await _client.PostAsJsonAsync($"{scenario.Path}/{scenario.Item.Id}/comments", new AddWorkItemCommentRequest("Please verify the acceptance criteria."));
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        Authorize(scenario.Recipient);
        var updated = await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications?pageSize=1&page=2");
        Assert.Equal(3, updated!.TotalCount);
        Assert.Single(updated.Items);
        using var read = await _client.PutAsJsonAsync($"/api/notifications/{assigned.Id}/read", new { });
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        using var readAgain = await _client.PutAsJsonAsync($"/api/notifications/{assigned.Id}/read", new { });
        Assert.Equal(HttpStatusCode.NoContent, readAgain.StatusCode);
        Assert.Equal(2, (await _client.GetFromJsonAsync<NotificationSummaryResponse>("/api/notifications/summary"))!.UnreadCount);
        using var allRead = await _client.PutAsJsonAsync("/api/notifications/read-all", new { });
        Assert.Equal(HttpStatusCode.NoContent, allRead.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications?unreadOnly=true"))!.Items);
        Assert.Equal(3, (await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications"))!.TotalCount);
        using var invalidPage = await _client.GetAsync("/api/notifications?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
    }

    [Fact]
    public async Task Notification_access_follows_current_membership_and_archival()
    {
        var scenario = await CreateNotificationScenarioAsync();
        Authorize(scenario.Recipient);
        var notification = Assert.Single((await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications"))!.Items);
        Authorize(scenario.Admin);
        using var revoke = await _client.DeleteAsync($"/api/organizations/{scenario.Organization.Id}/members/{scenario.Recipient.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Authorize(scenario.Recipient);
        Assert.Empty((await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications"))!.Items);
        Assert.Equal(0, (await _client.GetFromJsonAsync<NotificationSummaryResponse>("/api/notifications/summary"))!.UnreadCount);
        using var read = await _client.PutAsJsonAsync($"/api/notifications/{notification.Id}/read", new { });
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Authorize(scenario.Admin);
        using var comment = await _client.PostAsJsonAsync($"{scenario.Path}/{scenario.Item.Id}/comments", new AddWorkItemCommentRequest("After revocation"));
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
        Assert.Equal(1, await db.Notifications.CountAsync());
        Assert.NotNull(await db.Notifications.SingleAsync());
    }

    [Fact]
    public async Task Hub_requires_authentication_and_trusted_origins_and_query_tokens_are_hub_only()
    {
        using var anonymousRequest = new HttpRequestMessage(HttpMethod.Post, "/api/hubs/notifications/negotiate?negotiateVersion=1");
        anonymousRequest.Headers.Add("Origin", "https://localhost");
        using var anonymous = await _client.SendAsync(anonymousRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var auth = await RegisterAndAuthorizeAsync("hub-auth@example.com");
        using var hostileRequest = new HttpRequestMessage(HttpMethod.Post, "/api/hubs/notifications/negotiate?negotiateVersion=1");
        hostileRequest.Headers.Add("Origin", "https://hostile.example");
        using var hostile = await _client.SendAsync(hostileRequest);
        Assert.Equal(HttpStatusCode.Forbidden, hostile.StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;
        using var queryToken = await _client.GetAsync($"/api/notifications?access_token={auth.AccessToken}");
        Assert.Equal(HttpStatusCode.Unauthorized, queryToken.StatusCode);
    }

    [Fact]
    public async Task SignalR_delivers_only_a_recipient_hint_after_persistence_and_offline_notifications_survive()
    {
        var scenario = await CreateNotificationScenarioAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var recipient = await ConnectNotificationsAsync(scenario.Recipient.AccessToken, timeout.Token);
        using var actor = await ConnectNotificationsAsync(scenario.Admin.AccessToken, timeout.Token);
        Authorize(scenario.Admin);
        using var transition = await _client.PostAsJsonAsync($"{scenario.Path}/{scenario.Item.Id}/transitions", new TransitionWorkItemRequest("Triaged"));
        Assert.Equal(HttpStatusCode.OK, transition.StatusCode);
        var message = await ReceiveHubRecordAsync(recipient, timeout.Token);
        using var payload = JsonDocument.Parse(message);
        Assert.Equal("NotificationsChanged", payload.RootElement.GetProperty("target").GetString());
        Assert.Empty(payload.RootElement.GetProperty("arguments").EnumerateArray());
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>().Notifications.CountAsync());
        using var noActorMessage = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReceiveHubRecordAsync(actor, noActorMessage.Token));
        recipient.Abort();
        using var comment = await _client.PostAsJsonAsync($"{scenario.Path}/{scenario.Item.Id}/comments", new AddWorkItemCommentRequest("While disconnected"));
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        Authorize(scenario.Recipient);
        Assert.Equal(3, (await _client.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/notifications"))!.TotalCount);
    }

    private void Authorize(AuthResponse user) => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

    private async Task<(AuthResponse Admin, AuthResponse Recipient, OrganizationResponse Organization, WorkItemResponse Item, string Path)> CreateNotificationScenarioAsync()
    {
        var admin = await RegisterAndAuthorizeAsync("notification-admin@example.com");
        var recipient = await RegisterAndAuthorizeAsync("notification-recipient@example.com");
        Authorize(admin);
        using var orgResponse = await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Notification Team", "notification-team", null));
        var org = (await orgResponse.Content.ReadFromJsonAsync<OrganizationResponse>())!;
        using var member = await _client.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new AddOrganizationMemberRequest(recipient.Email, "Engineer"));
        Assert.Equal(HttpStatusCode.OK, member.StatusCode);
        using var projectResponse = await _client.PostAsJsonAsync($"/api/organizations/{org.Id}/projects", new CreateProjectRequest("Delivery", "DEL", null));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>())!;
        var path = $"/api/organizations/{org.Id}/projects/{project.Id}/work-items";
        using var itemResponse = await _client.PostAsJsonAsync(path, new CreateWorkItemRequest("Task", "Verify notifications", null, "Medium", recipient.UserId, null));
        Assert.Equal(HttpStatusCode.Created, itemResponse.StatusCode);
        return (admin, recipient, org, (await itemResponse.Content.ReadFromJsonAsync<WorkItemResponse>())!, path);
    }

    private async Task<WebSocket> ConnectNotificationsAsync(string token, CancellationToken cancellationToken)
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["Origin"] = "https://localhost";
        var socket = await client.ConnectAsync(new Uri($"wss://localhost/api/hubs/notifications?access_token={token}"), cancellationToken);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, cancellationToken);
        Assert.Equal("{}", await ReceiveHubRecordAsync(socket, cancellationToken));
        return socket;
    }

    private static async Task<string> ReceiveHubRecordAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();
        while (true)
        {
            var response = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (response.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Hub closed unexpectedly.");
            text.Append(Encoding.UTF8.GetString(buffer, 0, response.Count));
            var value = text.ToString();
            var end = value.IndexOf('\u001e');
            if (end >= 0) return value[..end];
        }
    }
}
