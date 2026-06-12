using Hardness.PrintBridge.Agent.Domain;

namespace Hardness.PrintBridge.Agent.Application;

public interface IDocumentPrintFallbackClient {
    bool CanPrint(PrintJob printJob);
    void Print(string printerName, PrintJob printJob);
}
