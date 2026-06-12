using System.Security.AccessControl;
using System.Security.Principal;

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

    public static string GetLegacyAgentConfigurationPath() {
        return Path.Combine(GetProgramDataRoot(), "config", "agent-settings.json");
    }

    public static string GetLegacyAppSettingsPath() {
        return Path.Combine(GetAppDataRoot(), "app", "settings.json");
    }

    public static string GetInstalledAppSettingsPath(string? baseDirectory = null) {
        return Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "appsettings.json");
    }

    public static string GetSharedAppSettingsPath() {
        return Path.Combine(GetProgramDataRoot(), "config", "appsettings.json");
    }

    public static string GetGlobalAppSettingsPath(string? baseDirectory = null) {
        if (!string.IsNullOrWhiteSpace(baseDirectory)) {
            return Path.Combine(baseDirectory, "appsettings.json");
        }

        var solutionRoot = TryFindSolutionRoot(AppContext.BaseDirectory)
            ?? TryFindSolutionRoot(Environment.CurrentDirectory);
        if (!string.IsNullOrWhiteSpace(solutionRoot)) {
            return Path.Combine(solutionRoot, "appsettings.json");
        }

        return GetSharedAppSettingsPath();
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

    public static void EnsureSharedConfigDirectoryExists() {
        var directoryPath = Path.GetDirectoryName(GetSharedAppSettingsPath());
        if (string.IsNullOrWhiteSpace(directoryPath)) {
            return;
        }

        Directory.CreateDirectory(directoryPath);
        TryGrantBuiltinUsersModify(directoryPath);
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

    private static void TryGrantBuiltinUsersModify(string directoryPath) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        try {
            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();
            var builtinUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var modifyRule = new FileSystemAccessRule(
                builtinUsersSid,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            security.AddAccessRule(modifyRule);
            directoryInfo.SetAccessControl(security);
        } catch {
        }
    }
}
