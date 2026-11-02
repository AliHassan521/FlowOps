using FlowOps.Application.Common.Configuration;
using FlowOps.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlowOps.Api.Background;

public sealed class SlaMonitorWorker(IServiceScopeFactory scopeFactory, IOptions<SlaWorkerOptions> options,
    TimeProvider clock, ILogger<SlaMonitorWorker> logger) : BackgroundService
{
    private readonly Dictionary<Guid, DateTimeOffset> retryAfter = [];
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
        var now = clock.GetUtcNow();
        foreach (var id in retryAfter.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            retryAfter.Remove(id);
        IReadOnlyList<Guid> ids;
        await using (var scope = scopeFactory.CreateAsyncScope())
            ids = await scope.ServiceProvider.GetRequiredService<ISlaProcessor>().GetDueIdsAsync(options.Value.BatchSize, cancellationToken, retryAfter.Keys.ToArray());
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
            catch (Exception exception)
            {
                // Skip poison records briefly so a full failing batch cannot starve
                // later deadlines. Bound memory; persisted state remains retryable.
                if (retryAfter.Count >= 1000)
                    retryAfter.Remove(retryAfter.MinBy(pair => pair.Value).Key);
                retryAfter[id] = clock.GetUtcNow().AddSeconds(Math.Max(60, options.Value.IntervalSeconds * 2));
                logger.LogError(exception, "SLA {SlaId} failed; retrying after a cooldown.", id);
            }
        }
        return processed;
    }
}
