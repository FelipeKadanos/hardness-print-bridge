using System.Drawing.Printing;
using System.Management;
using System.Runtime.Versioning;
using Hardness.PrintBridge.Agent.Application;
using Hardness.PrintBridge.Agent.Configuration;
using Hardness.PrintBridge.Agent.Domain;
using Microsoft.Extensions.Options;

namespace Hardness.PrintBridge.Agent.Infrastructure.Printing;

[SupportedOSPlatform("windows")]
public sealed class WindowsPrinterResolver(IOptions<PrintBridgeOptions> options) : IPrinterResolver {
    private readonly PrintBridgeOptions _options = options.Value;
    private static readonly TimeSpan[] AvailabilityRetryDelays = [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    public string Resolve(PrintJob printJob) {
        if (!OperatingSystem.IsWindows()) {
            throw new PlatformNotSupportedException("Windows printer resolution is only supported on Windows.");
        }

        var targetPrinter = string.IsNullOrWhiteSpace(printJob.RequestedPrinter)
            ? _options.DefaultPrinterName
            : printJob.RequestedPrinter;

        if (string.IsNullOrWhiteSpace(targetPrinter)) {
            throw new PrinterResolutionException("No target printer configured for this print job.");
        }

        var installedPrinters = PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
        var printerExists = installedPrinters.Any(p =>
            p.Equals(targetPrinter, StringComparison.OrdinalIgnoreCase));

        if (!printerExists) {
            throw new PrinterResolutionException(
                $"Requested printer '{targetPrinter}' was not found on this machine.");
        }

        var resolvedPrinterName = installedPrinters.First(p =>
            p.Equals(targetPrinter, StringComparison.OrdinalIgnoreCase));

        EnsurePrinterIsOnlineAndReady(resolvedPrinterName);

        return resolvedPrinterName;
    }

    private static void EnsurePrinterIsOnlineAndReady(string printerName) {
        PrinterResolutionException? lastAvailabilityException = null;

        for (var attempt = 0; attempt <= AvailabilityRetryDelays.Length; attempt++) {
            try {
                EnsurePrinterIsOnlineAndReadyOnce(printerName);
                return;
            } catch (PrinterResolutionException ex) when (ex.CanRetry && attempt < AvailabilityRetryDelays.Length) {
                lastAvailabilityException = ex;
                Thread.Sleep(AvailabilityRetryDelays[attempt]);
            }
        }

        if (lastAvailabilityException is not null) {
            throw lastAvailabilityException;
        }
    }

    private static void EnsurePrinterIsOnlineAndReadyOnce(string printerName) {
        try {
            var escapedName = printerName.Replace("\\", "\\\\").Replace("'", "''");
            using var searcher = new ManagementObjectSearcher(
                $"SELECT WorkOffline, PrinterStatus, DetectedErrorState, ExtendedPrinterStatus, PrinterState, Default, Availability FROM Win32_Printer WHERE Name = '{escapedName}'");
            using var results = searcher.Get();
            var printer = results.Cast<ManagementObject>().FirstOrDefault();
            if (printer is null) {
                throw new PrinterResolutionException($"Could not query status for printer '{printerName}'.");
            }

            var workOffline = Convert.ToBoolean(printer["WorkOffline"] ?? false);
            var printerStatus = Convert.ToInt32(printer["PrinterStatus"] ?? 0);
            var extendedStatus = Convert.ToInt32(printer["ExtendedPrinterStatus"] ?? 0);
            var detectedErrorState = Convert.ToInt32(printer["DetectedErrorState"] ?? 0);
            var printerState = Convert.ToInt32(printer["PrinterState"] ?? 0);
            var availability = Convert.ToInt32(printer["Availability"] ?? 0);

            var problems = new List<string>();
            if (printerStatus == 7) {
                problems.Add("paused");
            }

            if (workOffline || printerStatus == 8 || extendedStatus == 7) {
                problems.Add("offline");
            }

            if (detectedErrorState is >= 3 and <= 11) {
                problems.Add($"detected-error={DescribeDetectedErrorState(detectedErrorState)}");
            }

            if (printerState != 0) {
                problems.Add($"printer-state={printerState}");
            }

            if (availability != 0 && availability != 3) {
                problems.Add($"availability={availability}");
            }

            if (problems.Count > 0) {
                var reason = string.Join(", ", problems.Distinct(StringComparer.OrdinalIgnoreCase));
                throw new PrinterResolutionException(
                    $"Requested printer '{printerName}' is unavailable ({reason}).",
                    canRetry: true);
            }
        } catch (PrinterResolutionException) {
            throw;
        } catch (Exception ex) {
            throw new PrinterResolutionException(
                $"Failed to validate availability of printer '{printerName}': {ex.Message}",
                canRetry: true);
        }
    }

    private static string DescribeDetectedErrorState(int detectedErrorState) {
        return detectedErrorState switch {
            0 => "unknown",
            1 => "other",
            2 => "no-error",
            3 => "low-paper",
            4 => "no-paper",
            5 => "low-toner",
            6 => "no-toner",
            7 => "door-open",
            8 => "jammed",
            9 => "offline",
            10 => "service-requested",
            11 => "output-bin-full",
            _ => detectedErrorState.ToString()
        };
    }
}
