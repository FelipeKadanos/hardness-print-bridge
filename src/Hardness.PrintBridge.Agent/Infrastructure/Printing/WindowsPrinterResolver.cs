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
        try {
            var escapedName = printerName.Replace("\\", "\\\\").Replace("'", "''");
            using var searcher = new ManagementObjectSearcher(
                $"SELECT WorkOffline, PrinterStatus, DetectedErrorState, ExtendedPrinterStatus FROM Win32_Printer WHERE Name = '{escapedName}'");
            using var results = searcher.Get();
            var printer = results.Cast<ManagementObject>().FirstOrDefault();
            if (printer is null) {
                throw new PrinterResolutionException($"Could not query status for printer '{printerName}'.");
            }

            var workOffline = Convert.ToBoolean(printer["WorkOffline"] ?? false);
            var printerStatus = Convert.ToInt32(printer["PrinterStatus"] ?? 0);
            var extendedStatus = Convert.ToInt32(printer["ExtendedPrinterStatus"] ?? 0);
            var detectedErrorState = Convert.ToInt32(printer["DetectedErrorState"] ?? 0);

            var isPaused = printerStatus == 7;
            var isOffline = workOffline || printerStatus == 8 || extendedStatus == 7;
            var hasErrorState = detectedErrorState is >= 3 and <= 6;

            if (isPaused || isOffline || hasErrorState) {
                throw new PrinterResolutionException(
                    $"Requested printer '{printerName}' is unavailable (offline/paused/error).");
            }
        } catch (PrinterResolutionException) {
            throw;
        } catch (Exception ex) {
            throw new PrinterResolutionException(
                $"Failed to validate availability of printer '{printerName}': {ex.Message}");
        }
    }
}
