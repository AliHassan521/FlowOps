using System.Security.Claims;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Sla;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOps.Api.Controllers;

[ApiController, Authorize, Route("api/organizations/{organizationId:guid}/projects/{projectId:guid}/work-items/{workItemId:guid}/sla")]
public sealed class WorkItemSlasController(ISlaService sla) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId, Guid projectId, Guid workItemId, CancellationToken cancellationToken) =>
        Ok(await sla.GetWorkItemAsync(UserId, organizationId, projectId, workItemId, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Attach(Guid organizationId, Guid projectId, Guid workItemId,
        AttachWorkItemSlaRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created,
            await sla.AttachAsync(UserId, organizationId, projectId, workItemId, request.PolicyId, cancellationToken));

    private Guid UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new UnauthorizedAccessException("A valid user identity is required.");
}
