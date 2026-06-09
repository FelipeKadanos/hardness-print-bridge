using System.ComponentModel.DataAnnotations;

namespace Hardness.PrintBridge.Agent.Configuration;

public class PrintBridgeOptions {
    public const string SectionName = "PrintBridge";

    public string? QueueRootPath { get; init; }

    [Required]
    public string WatchPath { get; init; } = string.Empty;

    [Required]
    public string ProcessingPath { get; init; } = string.Empty;

    [Required]
    public string PrintedPath { get; init; } = string.Empty;

    [Required]
    public string ErrorPath { get; init; } = string.Empty;

    public string? PrinterName { get; init; }

    [Required]
    public string DefaultPrinterName { get; init; } = string.Empty;

    [Range(100, int.MaxValue)]
    public int PollIntervalMs { get; init; } = 2000;

    public bool RemoteSourceEnabled { get; init; } = false;

    public string? RemoteListUrl { get; init; }

    public string? RemoteDownloadUrlTemplate { get; init; }

    [Range(100, int.MaxValue)]
    public int? RemotePollIntervalMs { get; init; }

    [Range(1000, int.MaxValue)]
    public int RemoteTimeoutMs { get; init; } = 10000;

    [Range(1, 500)]
    public int RemoteMaxFilesPerCycle { get; init; } = 20;

    public bool RemoteAllowInsecureTls { get; init; } = false;

    [Required]
    public string RemoteSeenCachePath { get; init; } = "meta\\remote-seen.json";

    [Required]
    public string LogLevel { get; init; } = "Information";

    [Required]
    public string HardnessCallbackUrl { get; init; } = string.Empty;

    public string? ApiAuthToken { get; init; }
}
