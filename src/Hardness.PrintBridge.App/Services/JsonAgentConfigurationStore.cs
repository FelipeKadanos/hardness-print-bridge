using System.Text.Json;
using Hardness.PrintBridge.Contracts.Configuration;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.Services;

public sealed class JsonAgentConfigurationStore : IAgentConfigurationStore {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true
    };

    public async Task<AgentConfigurationModel> LoadAsync(CancellationToken cancellationToken) {
        var configurationPath = RuntimePaths.GetAgentConfigurationPath();
        if (!File.Exists(configurationPath)) {
            return new AgentConfigurationModel();
        }

        await using var stream = File.OpenRead(configurationPath);
        var file = await JsonSerializer.DeserializeAsync<AgentConfigurationFile>(stream, JsonOptions, cancellationToken);
        return AgentConfigurationModel.FromFile(file);
    }

    public async Task SaveAsync(AgentConfigurationModel configuration, CancellationToken cancellationToken) {
        var configurationPath = RuntimePaths.GetAgentConfigurationPath();
        var directoryPath = Path.GetDirectoryName(configurationPath);
        if (!string.IsNullOrWhiteSpace(directoryPath)) {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{configurationPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(configuration.ToFile(), JsonOptions),
            cancellationToken);

        File.Move(tempPath, configurationPath, overwrite: true);
    }
}
