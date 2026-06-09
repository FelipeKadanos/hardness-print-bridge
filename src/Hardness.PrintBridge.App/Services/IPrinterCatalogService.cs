namespace Hardness.PrintBridge.App.Services;

public interface IPrinterCatalogService {
    IReadOnlyList<string> GetInstalledPrinters();
}
