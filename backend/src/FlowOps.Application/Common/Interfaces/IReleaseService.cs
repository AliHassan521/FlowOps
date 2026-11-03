using FlowOps.Contracts.Common;
using FlowOps.Contracts.Releases;
namespace FlowOps.Application.Common.Interfaces;

public interface IReleaseService
{
    Task<ReleaseEnvironmentResponse> CreateEnvironmentAsync(Guid actor, Guid organization, Guid project, CreateEnvironmentRequest request, CancellationToken ct);
    Task<PagedResponse<ReleaseEnvironmentResponse>> EnvironmentsAsync(Guid actor, Guid organization, Guid project, int page, int pageSize, bool activeOnly, CancellationToken ct);
    Task ArchiveEnvironmentAsync(Guid actor, Guid organization, Guid project, Guid environment, CancellationToken ct);
    Task<ReleaseResponse> CreateAsync(Guid actor, Guid organization, Guid project, CreateReleaseRequest request, CancellationToken ct);
    Task<PagedResponse<ReleaseResponse>> ListAsync(Guid actor, Guid organization, Guid project, ReleaseListQuery query, CancellationToken ct);
    Task<ReleaseResponse> GetAsync(Guid actor, Guid organization, Guid project, Guid release, CancellationToken ct);
    Task<ReleaseResponse> UpdateAsync(Guid actor, Guid organization, Guid project, Guid release, UpdateReleaseRequest request, CancellationToken ct);
    Task<ReleaseResponse> SetLinksAsync(Guid actor, Guid organization, Guid project, Guid release, SetReleaseLinksRequest request, CancellationToken ct);
    Task<ReleaseLinksResponse> LinksAsync(Guid actor, Guid organization, Guid project, Guid release, CancellationToken ct);
    Task<ReleaseResponse> TransitionAsync(Guid actor, Guid organization, Guid project, Guid release, TransitionReleaseRequest request, CancellationToken ct);
    Task<PagedResponse<ReleaseTimelineResponse>> TimelineAsync(Guid actor, Guid organization, Guid project, Guid release, int page, int pageSize, CancellationToken ct);
}
