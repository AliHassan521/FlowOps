namespace FlowOps.Application.Common.Configuration;

public sealed class SlaWorkerOptions
{
    public const string SectionName = "SlaWorker";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 100;
}
