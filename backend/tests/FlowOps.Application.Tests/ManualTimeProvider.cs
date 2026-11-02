namespace FlowOps.Application.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private long ticks = initial.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
}
