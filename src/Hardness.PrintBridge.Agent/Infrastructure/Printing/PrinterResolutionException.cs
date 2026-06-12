namespace Hardness.PrintBridge.Agent.Infrastructure.Printing;

public sealed class PrinterResolutionException : Exception {
    public PrinterResolutionException(string message, bool canRetry = false)
        : base(message) {
        CanRetry = canRetry;
    }

    public bool CanRetry { get; }
}
