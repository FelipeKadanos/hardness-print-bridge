using Hardness.PrintBridge.Agent.Domain;

namespace Hardness.PrintBridge.Agent.Application;

public interface IPrinterResolver {
    string Resolve(PrintJob printJob);
}
