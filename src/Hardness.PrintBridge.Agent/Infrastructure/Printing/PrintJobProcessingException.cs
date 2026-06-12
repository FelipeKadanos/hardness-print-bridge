namespace Hardness.PrintBridge.Agent.Infrastructure.Printing;

public sealed class PrintJobProcessingException : Exception {
    public PrintJobProcessingException(string message, Exception? innerException = null, bool canRetry = true)
        : base(message, innerException) {
        CanRetry = canRetry;
    }

    public bool CanRetry { get; }
}
