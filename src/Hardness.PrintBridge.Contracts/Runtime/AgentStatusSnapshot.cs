namespace Hardness.PrintBridge.Contracts.Runtime;

public sealed record AgentStatusSnapshot {
    public required string State { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public string? Message { get; init; }
    public int? ProcessedCount { get; init; }
    public int? FailedCount { get; init; }
    public int? RemoteDownloaded { get; init; }
    public int? RemoteSkipped { get; init; }
    public int? RemoteFailed { get; init; }
}
