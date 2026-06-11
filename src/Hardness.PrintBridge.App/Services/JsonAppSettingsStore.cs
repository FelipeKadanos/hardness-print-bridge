namespace Hardness.PrintBridge.App.Services;

public sealed class JsonAppSettingsStore : IAppSettingsStore {
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken) {
        return await UnifiedAppSettingsDocumentStore.LoadAppSettingsAsync(cancellationToken);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) {
        await UnifiedAppSettingsDocumentStore.SaveAppSettingsAsync(settings, cancellationToken);
    }
}
