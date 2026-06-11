using System.Text.Json;
using System.Text.Json.Nodes;
using Hardness.PrintBridge.Contracts.Configuration;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.Services;

internal static class UnifiedAppSettingsDocumentStore {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true
    };

    public static async Task<AppSettings> LoadAppSettingsAsync(CancellationToken cancellationToken) {
        var document = await LoadDocumentAsync(cancellationToken);
        var section = document["App"];
        var settings = section?.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
        return string.IsNullOrWhiteSpace(settings.InstallPath)
            ? settings with { InstallPath = AppContext.BaseDirectory }
            : settings;
    }

    public static async Task SaveAppSettingsAsync(AppSettings settings, CancellationToken cancellationToken) {
        var document = await LoadDocumentAsync(cancellationToken);
        document["App"] = JsonSerializer.SerializeToNode(settings, JsonOptions);
        await SaveDocumentAsync(document, cancellationToken);
    }

    public static async Task<AgentConfigurationModel> LoadAgentConfigurationAsync(CancellationToken cancellationToken) {
        var document = await LoadDocumentAsync(cancellationToken);
        var section = document["PrintBridge"];
        var printBridge = section?.Deserialize<AgentConfigurationSection>(JsonOptions);
        return AgentConfigurationModel.FromSection(printBridge);
    }

    public static async Task SaveAgentConfigurationAsync(AgentConfigurationModel configuration, CancellationToken cancellationToken) {
        var document = await LoadDocumentAsync(cancellationToken);
        document["PrintBridge"] = JsonSerializer.SerializeToNode(configuration.ToSection(), JsonOptions);
        await SaveDocumentAsync(document, cancellationToken);
    }

    private static async Task<JsonObject> LoadDocumentAsync(CancellationToken cancellationToken) {
        var path = RuntimePaths.GetGlobalAppSettingsPath();
        if (File.Exists(path)) {
            await using var stream = File.OpenRead(path);
            var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
            return node as JsonObject ?? new JsonObject();
        }

        var migrated = await TryBuildMigratedDocumentAsync(cancellationToken);
        if (migrated is not null) {
            await SaveDocumentAsync(migrated, cancellationToken);
            return migrated;
        }

        return new JsonObject();
    }

    private static async Task SaveDocumentAsync(JsonObject document, CancellationToken cancellationToken) {
        var path = RuntimePaths.GetGlobalAppSettingsPath();
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath)) {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            tempPath,
            document.ToJsonString(JsonOptions),
            cancellationToken);

        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task<JsonObject?> TryBuildMigratedDocumentAsync(CancellationToken cancellationToken) {
        var document = new JsonObject();
        var migrated = false;

        var legacyAgentConfigurationPath = RuntimePaths.GetLegacyAgentConfigurationPath();
        if (File.Exists(legacyAgentConfigurationPath)) {
            await using var stream = File.OpenRead(legacyAgentConfigurationPath);
            var legacyFile = await JsonSerializer.DeserializeAsync<AgentConfigurationFile>(stream, JsonOptions, cancellationToken);
            document["PrintBridge"] = JsonSerializer.SerializeToNode(
                AgentConfigurationModel.FromFile(legacyFile).ToSection(),
                JsonOptions);
            migrated = true;
        }

        var legacyAppSettingsPath = RuntimePaths.GetLegacyAppSettingsPath();
        if (File.Exists(legacyAppSettingsPath)) {
            await using var stream = File.OpenRead(legacyAppSettingsPath);
            var legacySettings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
            document["App"] = JsonSerializer.SerializeToNode(legacySettings ?? new AppSettings(), JsonOptions);
            migrated = true;
        }

        return migrated ? document : null;
    }
}
