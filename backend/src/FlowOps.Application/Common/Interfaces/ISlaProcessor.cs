namespace FlowOps.Application.Common.Interfaces;

public interface ISlaProcessor
{
    Task<IReadOnlyList<Guid>> GetDueIdsAsync(int batchSize, CancellationToken cancellationToken);
    Task<bool> ProcessOneAsync(Guid slaId, CancellationToken cancellationToken);
}
