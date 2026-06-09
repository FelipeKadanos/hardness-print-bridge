namespace Hardness.PrintBridge.Contracts.Runtime;

public static class RuntimePaths {
    public static string GetProgramDataRoot() {
        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(commonAppData, "HardnessPrintBridge");
    }

    public static string GetAppDataRoot() {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "HardnessPrintBridge");
    }

    public static string GetAgentStatusPath() {
        return Path.Combine(GetProgramDataRoot(), "status", "agent-status.json");
    }

    public static string GetAgentConfigurationPath() {
        return Path.Combine(GetProgramDataRoot(), "config", "agent-settings.json");
    }

    public static string GetAppSettingsPath() {
        return Path.Combine(GetAppDataRoot(), "app", "settings.json");
    }

    public static string GetUpdateWorkspacePath() {
        return Path.Combine(GetAppDataRoot(), "update");
    }
}
