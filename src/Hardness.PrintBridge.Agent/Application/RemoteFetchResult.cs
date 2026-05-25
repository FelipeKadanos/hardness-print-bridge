namespace Hardness.PrintBridge.Agent.Application;

public sealed record RemoteFetchResult {
    public static readonly RemoteFetchResult Disabled = new() { DisabledByConfig = true };

    public bool DisabledByConfig { get; init; }
    public bool SkippedBySchedule { get; init; }
    public int ListedCount { get; init; }
    public int DownloadedCount { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount { get; init; }
    public bool BackoffApplied { get; init; }
}
