using Hardness.PrintBridge.Contracts.Configuration;

namespace Hardness.PrintBridge.App.Services;

public interface IAgentConfigurationStore {
    Task<AgentConfigurationModel> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AgentConfigurationModel configuration, CancellationToken cancellationToken);
}
