namespace Hardness.PrintBridge.App.Update;

public interface IUpdateService {
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken);
    Task BeginUpdateAsync(UpdateCheckResult update, CancellationToken cancellationToken);
}
