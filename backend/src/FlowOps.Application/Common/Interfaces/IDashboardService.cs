using FlowOps.Contracts.Dashboard;
namespace FlowOps.Application.Common.Interfaces;

public interface IDashboardService
{
    Task<DashboardResponse> GetAsync(Guid actor, Guid organization, Guid? project, int periodDays, int workloadPage, CancellationToken ct);
}
