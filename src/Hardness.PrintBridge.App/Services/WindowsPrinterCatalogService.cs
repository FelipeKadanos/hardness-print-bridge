using System.Drawing.Printing;

namespace Hardness.PrintBridge.App.Services;

public sealed class WindowsPrinterCatalogService : IPrinterCatalogService {
    public IReadOnlyList<string> GetInstalledPrinters() {
        return PrinterSettings.InstalledPrinters
            .Cast<string>()
            .OrderBy(static printerName => printerName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
