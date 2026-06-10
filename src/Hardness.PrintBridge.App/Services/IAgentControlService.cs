namespace Hardness.PrintBridge.App.Services;

public interface IAgentControlService {
    Task EnsureRunningAsync(CancellationToken cancellationToken);
    Task<AgentHostStatus> GetCurrentStatusAsync(CancellationToken cancellationToken);
    Task RestartAsync(CancellationToken cancellationToken);
}
