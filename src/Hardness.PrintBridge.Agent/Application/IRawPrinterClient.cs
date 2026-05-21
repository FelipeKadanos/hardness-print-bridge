namespace Hardness.PrintBridge.Agent.Application;

public interface IRawPrinterClient {
    void Print(string printerName, byte[] payload, string documentName);
}
