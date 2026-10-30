using FlowOps.Contracts.Common;
using FlowOps.Contracts.WorkItems;

namespace FlowOps.Application.Common.Interfaces;

public interface IWorkItemService
{
    Task<WorkItemResponse> CreateAsync(Guid actorUserId, Guid organizationId, Guid projectId, CreateWorkItemRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<WorkItemResponse>> ListAsync(Guid actorUserId, Guid organizationId, Guid projectId, WorkItemListQuery query, CancellationToken cancellationToken = default);
    Task<WorkItemResponse> GetAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId, CancellationToken cancellationToken = default);
    Task<WorkItemResponse> UpdateAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId, UpdateWorkItemRequest request, CancellationToken cancellationToken = default);
    Task<WorkItemResponse> AssignAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId, AssignWorkItemRequest request, CancellationToken cancellationToken = default);
    Task<WorkItemResponse> TransitionAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId, TransitionWorkItemRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<WorkItemCommentResponse>> ListCommentsAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<WorkItemCommentResponse> AddCommentAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId, AddWorkItemCommentRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<WorkItemHistoryResponse>> ListHistoryAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId, int page, int pageSize, CancellationToken cancellationToken = default);
}
