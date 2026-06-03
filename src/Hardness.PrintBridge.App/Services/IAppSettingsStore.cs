namespace Hardness.PrintBridge.App.Services;

public interface IAppSettingsStore {
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
