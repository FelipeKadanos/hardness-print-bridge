using Hardness.PrintBridge.Contracts.Configuration;

namespace Hardness.PrintBridge.App.Services;

public sealed class JsonAgentConfigurationStore : IAgentConfigurationStore {
    public async Task<AgentConfigurationModel> LoadAsync(CancellationToken cancellationToken) {
        return await UnifiedAppSettingsDocumentStore.LoadAgentConfigurationAsync(cancellationToken);
    }

    public async Task SaveAsync(AgentConfigurationModel configuration, CancellationToken cancellationToken) {
        await UnifiedAppSettingsDocumentStore.SaveAgentConfigurationAsync(configuration, cancellationToken);
    }
}
