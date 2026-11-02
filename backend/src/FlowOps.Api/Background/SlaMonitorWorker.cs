using FlowOps.Application.Common.Configuration;
using FlowOps.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlowOps.Api.Background;

public sealed class SlaMonitorWorker(IServiceScopeFactory scopeFactory, IOptions<SlaWorkerOptions> options,
    TimeProvider clock, ILogger<SlaMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.IntervalSeconds), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { logger.LogError(exception, "SLA batch query failed; retrying on the next interval."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids;
        await using (var scope = scopeFactory.CreateAsyncScope())
            ids = await scope.ServiceProvider.GetRequiredService<ISlaProcessor>().GetDueIdsAsync(options.Value.BatchSize, cancellationToken);
        var processed = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // One failed record must not leave a dirty DbContext or notification queue for the next record.
            await using var scope = scopeFactory.CreateAsyncScope();
            try
            {
                if (await scope.ServiceProvider.GetRequiredService<ISlaProcessor>().ProcessOneAsync(id, cancellationToken)) processed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (DbUpdateConcurrencyException) { logger.LogDebug("SLA {SlaId} changed concurrently; a later pass will re-evaluate it.", id); }
            catch (Exception exception) { logger.LogError(exception, "SLA {SlaId} failed; persisted state will be retried next interval.", id); }
        }
        return processed;
    }
}
