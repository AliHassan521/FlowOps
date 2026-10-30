using FlowOps.Contracts.Common;
using FlowOps.Contracts.Organizations;

namespace FlowOps.Application.Common.Interfaces;

public interface IOrganizationService
{
    Task<OrganizationResponse> CreateAsync(Guid actorUserId, CreateOrganizationRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<OrganizationResponse>> ListForUserAsync(Guid actorUserId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<OrganizationResponse> GetAsync(Guid actorUserId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationResponse> UpdateAsync(Guid actorUserId, Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid actorUserId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<PagedResponse<OrganizationMemberResponse>> ListMembersAsync(Guid actorUserId, Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<OrganizationMemberResponse> AddMemberAsync(Guid actorUserId, Guid organizationId, AddOrganizationMemberRequest request, CancellationToken cancellationToken = default);
    Task<OrganizationMemberResponse> UpdateMemberRoleAsync(Guid actorUserId, Guid organizationId, Guid memberUserId, string roleName, CancellationToken cancellationToken = default);
    Task DeactivateMemberAsync(Guid actorUserId, Guid organizationId, Guid memberUserId, CancellationToken cancellationToken = default);
}
