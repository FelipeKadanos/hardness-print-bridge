namespace Hardness.PrintBridge.Agent.Application;

public sealed record PrintCallbackRequest {
    public required string FileName { get; init; }
    public required string Status { get; init; }
    public string? RequestedPrinter { get; init; }
    public string? UsedPrinter { get; init; }
    public string? ErrorMessage { get; init; }
}
