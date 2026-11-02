using FlowOps.Contracts.Common;
using FlowOps.Contracts.Incidents;
namespace FlowOps.Application.Common.Interfaces;

public interface IIncidentService
{
    Task<IncidentResponse> CreateAsync(Guid actor, Guid organization, Guid project, CreateIncidentRequest request, CancellationToken ct);
    Task<PagedResponse<IncidentResponse>> ListAsync(Guid actor, Guid organization, Guid project, IncidentListQuery query, CancellationToken ct);
    Task<IncidentResponse> GetAsync(Guid actor, Guid organization, Guid project, Guid incident, CancellationToken ct);
    Task<IncidentResponse> UpdateAsync(Guid actor, Guid organization, Guid project, Guid incident, UpdateIncidentRequest request, CancellationToken ct);
    Task<IncidentResponse> AssignAsync(Guid actor, Guid organization, Guid project, Guid incident, AssignIncidentRequest request, CancellationToken ct);
    Task<IncidentResponse> TransitionAsync(Guid actor, Guid organization, Guid project, Guid incident, TransitionIncidentRequest request, CancellationToken ct);
    Task<IncidentCommentResponse> CommentAsync(Guid actor, Guid organization, Guid project, Guid incident, AddIncidentCommentRequest request, CancellationToken ct);
    Task<PagedResponse<IncidentCommentResponse>> CommentsAsync(Guid actor, Guid organization, Guid project, Guid incident, int page, int pageSize, CancellationToken ct);
    Task<PagedResponse<IncidentTimelineResponse>> TimelineAsync(Guid actor, Guid organization, Guid project, Guid incident, int page, int pageSize, CancellationToken ct);
    Task<IncidentSlaStateResponse> SlaAsync(Guid actor, Guid organization, Guid project, Guid incident, CancellationToken ct);
    Task<IncidentSlaStateResponse> AttachSlaAsync(Guid actor, Guid organization, Guid project, Guid incident, AttachIncidentSlaRequest request, CancellationToken ct);
    Task<IncidentSummaryResponse> SummaryAsync(Guid actor, Guid organization, Guid? project, CancellationToken ct);
}
