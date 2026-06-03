namespace Hardness.PrintBridge.App.Services;

public interface IStartupService {
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
