using FlowOps.Contracts.Common;
using FlowOps.Contracts.Sla;
using FlowOps.Domain.Entities;

namespace FlowOps.Application.Common.Interfaces;

public interface ISlaService
{
    Task<SlaPolicyResponse> CreatePolicyAsync(Guid actorId, Guid organizationId, CreateSlaPolicyRequest request, CancellationToken cancellationToken);
    Task<PagedResponse<SlaPolicyResponse>> ListPoliciesAsync(Guid actorId, Guid organizationId, int page, int pageSize, bool activeOnly, CancellationToken cancellationToken);
    Task ArchivePolicyAsync(Guid actorId, Guid organizationId, Guid policyId, CancellationToken cancellationToken);
    Task<WorkItemSlaStateResponse> GetWorkItemAsync(Guid actorId, Guid organizationId, Guid projectId, Guid workItemId, CancellationToken cancellationToken);
    Task<WorkItemSlaStateResponse> AttachAsync(Guid actorId, Guid organizationId, Guid projectId, Guid workItemId, Guid policyId, CancellationToken cancellationToken);
    Task<SlaSummaryResponse> SummaryAsync(Guid actorId, Guid organizationId, CancellationToken cancellationToken);
    // Stages completion in the caller's work-item transaction; does not save or publish.
    Task CompleteForResolutionAsync(WorkItem workItem, Guid organizationId, CancellationToken cancellationToken);
}
