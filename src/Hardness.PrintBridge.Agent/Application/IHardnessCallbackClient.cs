namespace Hardness.PrintBridge.Agent.Application;

public interface IHardnessCallbackClient {
    Task SendAsync(PrintCallbackRequest request, CancellationToken cancellationToken);
}
