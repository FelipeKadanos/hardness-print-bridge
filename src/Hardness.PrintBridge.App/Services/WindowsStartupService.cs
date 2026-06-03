using Microsoft.Win32;

namespace Hardness.PrintBridge.App.Services;

public sealed class WindowsStartupService : IStartupService {
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "HardnessPrintBridge";

    public bool IsEnabled() {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: false);
        var value = key?.GetValue(AppName) as string;
        return string.Equals(value, BuildCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled) {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryRunPath);
        if (enabled) {
            key.SetValue(AppName, BuildCommand());
            return;
        }

        key.DeleteValue(AppName, throwOnMissingValue: false);
    }

    private static string BuildCommand() {
        return $"\"{Application.ExecutablePath}\"";
    }
}
