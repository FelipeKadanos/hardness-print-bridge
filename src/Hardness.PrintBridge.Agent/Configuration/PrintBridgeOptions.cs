using System.ComponentModel.DataAnnotations;

namespace Hardness.PrintBridge.Agent.Configuration;

public class PrintBridgeOptions {
    public const string SectionName = "PrintBridge";

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

    [Required]
    public string LogLevel { get; init; } = "Information";

    [Required]
    public string HardnessCallbackUrl { get; init; } = string.Empty;

    public string? HardnessCallbackToken { get; init; }
}
