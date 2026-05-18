using System.Drawing.Printing;
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

        return installedPrinters.First(p =>
            p.Equals(targetPrinter, StringComparison.OrdinalIgnoreCase));
    }
}
