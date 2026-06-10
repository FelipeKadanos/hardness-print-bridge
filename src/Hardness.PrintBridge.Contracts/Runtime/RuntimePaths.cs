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

    public static string GetAgentLogDirectory(string? baseDirectory = null) {
        return Path.Combine(GetAgentRuntimeRoot(baseDirectory), "logs");
    }

    public static string GetAgentLogPath(string? baseDirectory = null) {
        return Path.Combine(GetAgentLogDirectory(baseDirectory), "agent.log");
    }

    private static string GetAgentRuntimeRoot(string? baseDirectory = null) {
        if (!string.IsNullOrWhiteSpace(baseDirectory)) {
            return baseDirectory;
        }

        return TryFindSolutionRoot(AppContext.BaseDirectory)
            ?? TryFindSolutionRoot(Environment.CurrentDirectory)
            ?? AppContext.BaseDirectory;
    }

    private static string? TryFindSolutionRoot(string startPath) {
        var directory = new DirectoryInfo(startPath);
        if (!directory.Exists) {
            directory = directory.Parent;
        }

        while (directory is not null) {
            var solutionPath = Path.Combine(directory.FullName, "Hardness.PrintBridge.slnx");
            if (File.Exists(solutionPath)) {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
