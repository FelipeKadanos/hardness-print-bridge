namespace Hardness.PrintBridge.Agent.Infrastructure.Printing;

public sealed class PrintJobProcessingException(string message, Exception? innerException = null)
    : Exception(message, innerException);
