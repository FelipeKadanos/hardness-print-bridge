using System.Text.Json;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.Services;

public sealed class JsonAppSettingsStore : IAppSettingsStore {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken) {
        var settingsPath = RuntimePaths.GetAppSettingsPath();
        if (!File.Exists(settingsPath)) {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) {
        var settingsPath = RuntimePaths.GetAppSettingsPath();
        var directoryPath = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directoryPath)) {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{settingsPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(settings, JsonOptions),
            cancellationToken);

        File.Move(tempPath, settingsPath, overwrite: true);
    }
}
