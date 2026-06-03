namespace Hardness.PrintBridge.App.Services;

public interface IAgentControlService {
    Task RestartAsync(CancellationToken cancellationToken);
}
