namespace Hardness.PrintBridge.App.Update;

public sealed record UpdateCheckResult {
    public required bool UpdateAvailable { get; init; }
    public required string CurrentVersion { get; init; }
    public string? LatestVersion { get; init; }
    public string? ReleaseName { get; init; }
    public string? DownloadUrl { get; init; }
}
