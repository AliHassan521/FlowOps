using FlowOps.Contracts.Releases;
namespace FlowOps.Contracts.Dashboard;

public sealed record DashboardResponse(DateTime ServerTimeUtc, int PeriodDays, long OpenWorkItems, long CriticalWorkItems,
    long ActiveIncidents, long CriticalIncidents, long UnassignedWorkItems, long SlaAtRisk, long SlaBreached, long SlaMet,
    long ResolvedWorkItemsInPeriod, long ResolvedIncidentsInPeriod, long SuccessfulReleasesInPeriod, long FailedReleasesInPeriod,
    IReadOnlyList<ReleaseResponse> RecentReleases, IReadOnlyList<TeamWorkloadResponse> TeamWorkload,
    int WorkloadPage, long WorkloadTotalCount);
public sealed record TeamWorkloadResponse(Guid UserId, string FullName, long OpenWorkItems, long CriticalWorkItems, long ActiveIncidents);
