namespace Hardness.PrintBridge.Agent.Infrastructure.Printing;

public sealed class PrinterResolutionException(string message)
    : Exception(message);
