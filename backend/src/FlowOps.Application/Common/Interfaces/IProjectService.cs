using FlowOps.Contracts.Common;
using FlowOps.Contracts.Projects;

namespace FlowOps.Application.Common.Interfaces;

public interface IProjectService
{
    Task<ProjectResponse> CreateAsync(Guid actorUserId, Guid organizationId, CreateProjectRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<ProjectResponse>> ListAsync(Guid actorUserId, Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ProjectResponse> GetAsync(Guid actorUserId, Guid organizationId, Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectResponse> UpdateAsync(Guid actorUserId, Guid organizationId, Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid actorUserId, Guid organizationId, Guid projectId, CancellationToken cancellationToken = default);
}
