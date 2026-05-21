using Hardness.PrintBridge.Agent.Domain;

namespace Hardness.PrintBridge.Agent.Application;

public interface IPrintJobParser {
    PrintJob ParseEtq(string filePath);
}
