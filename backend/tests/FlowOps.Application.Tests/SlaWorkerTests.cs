using FlowOps.Api.Background;
using FlowOps.Application.Common.Configuration;
using FlowOps.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowOps.Application.Tests;

public sealed class SlaWorkerTests
{
    [Fact]
    public async Task Batch_is_bounded_and_each_record_has_an_independent_scope_even_when_one_fails()
    {
        var state = new State();
        var services = new ServiceCollection(); services.AddSingleton(state); services.AddScoped<ISlaProcessor, Processor>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var worker = new SlaMonitorWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SlaWorkerOptions { BatchSize = 2 }), TimeProvider.System, NullLogger<SlaMonitorWorker>.Instance);
        Assert.Equal(1, await worker.RunOnceAsync(default));
        Assert.Equal(3, state.Scopes); // one query scope and one per selected record
        Assert.Equal(2, state.Processed.Count);
        Assert.DoesNotContain(state.Ids[2], state.Processed);
    }

    [Fact]
    public async Task Cancelled_batch_does_not_process_records()
    {
        var state = new State(); var services = new ServiceCollection(); services.AddSingleton(state); services.AddScoped<ISlaProcessor, Processor>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new SlaMonitorWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SlaWorkerOptions()), TimeProvider.System, NullLogger<SlaMonitorWorker>.Instance);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunOnceAsync(cancelled.Token));
        Assert.Empty(state.Processed);
    }

    [Fact]
    public async Task A_failing_first_record_does_not_starve_later_records_when_batch_size_is_one()
    {
        var state = new State(); var services = new ServiceCollection(); services.AddSingleton(state); services.AddScoped<ISlaProcessor, Processor>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new SlaMonitorWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SlaWorkerOptions { BatchSize = 1 }), TimeProvider.System, NullLogger<SlaMonitorWorker>.Instance);
        Assert.Equal(0, await worker.RunOnceAsync(default));
        Assert.Equal(1, await worker.RunOnceAsync(default));
        Assert.Equal(new[] { state.Ids[0], state.Ids[1] }, state.Processed);
    }

    [Fact]
    public async Task A_failed_record_is_retried_after_its_cooldown()
    {
        var state = new State(); var services = new ServiceCollection(); services.AddSingleton(state); services.AddScoped<ISlaProcessor, Processor>();
        await using var provider = services.BuildServiceProvider();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var worker = new SlaMonitorWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SlaWorkerOptions { BatchSize = 1 }), clock, NullLogger<SlaMonitorWorker>.Instance);
        await worker.RunOnceAsync(default);
        clock.Advance(TimeSpan.FromMinutes(2));
        await worker.RunOnceAsync(default);
        Assert.Equal(new[] { state.Ids[0], state.Ids[0] }, state.Processed);
    }

    private sealed class State
    {
        public Guid[] Ids { get; } = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        public int Scopes;
        public List<Guid> Processed { get; } = [];
    }
    private sealed class Processor : ISlaProcessor
    {
        private readonly State state;
        public Processor(State state) { this.state = state; state.Scopes++; }
        public Task<IReadOnlyList<Guid>> GetDueIdsAsync(int batchSize, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? excludedIds = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Guid>>(state.Ids.Where(id => excludedIds?.Contains(id) != true).Take(batchSize).ToArray());
        }
        public Task<bool> ProcessOneAsync(Guid slaId, CancellationToken cancellationToken)
        {
            state.Processed.Add(slaId);
            if (slaId == state.Ids[0]) throw new IOException("Simulated record failure.");
            return Task.FromResult(true);
        }
    }
}
