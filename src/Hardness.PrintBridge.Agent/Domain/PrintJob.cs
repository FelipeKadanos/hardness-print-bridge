namespace Hardness.PrintBridge.Agent.Domain;

public sealed record PrintJob {
    public required string FileName { get; init; }
    public required string SourcePath { get; init; }
    public required byte[] RawPayload { get; init; }
    public string? RequestedPrinter { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
