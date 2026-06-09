namespace Hardness.PrintBridge.Contracts.Configuration;

public sealed record AgentConfigurationModel {
    public string QueueRootPath { get; init; } = AgentConfigurationDefaults.DefaultQueueRootPath;
    public string DefaultPrinterName { get; init; } = AgentConfigurationDefaults.DefaultPrinterName;
    public string RemoteListUrl { get; init; } = AgentConfigurationDefaults.DefaultRemoteListUrl;
    public string RemoteDownloadUrlTemplate { get; init; } = AgentConfigurationDefaults.DefaultRemoteDownloadUrlTemplate;
    public string HardnessCallbackUrl { get; init; } = AgentConfigurationDefaults.DefaultHardnessCallbackUrl;
    public string ApiAuthToken { get; init; } = string.Empty;
    public bool RemoteSourceEnabled { get; init; } = true;

    public AgentConfigurationFile ToFile() {
        var queueRootPath = NormalizeQueueRootPath(QueueRootPath);

        return new AgentConfigurationFile {
            PrintBridge = new AgentConfigurationSection {
                QueueRootPath = queueRootPath,
                WatchPath = Path.Combine(queueRootPath, AgentConfigurationDefaults.DefaultInboxFolderName),
                ProcessingPath = Path.Combine(queueRootPath, AgentConfigurationDefaults.DefaultProcessingFolderName),
                PrintedPath = Path.Combine(queueRootPath, AgentConfigurationDefaults.DefaultPrintedFolderName),
                ErrorPath = Path.Combine(queueRootPath, AgentConfigurationDefaults.DefaultErrorFolderName),
                DefaultPrinterName = string.IsNullOrWhiteSpace(DefaultPrinterName)
                    ? AgentConfigurationDefaults.DefaultPrinterName
                    : DefaultPrinterName.Trim(),
                RemoteListUrl = NormalizeUrl(RemoteListUrl, AgentConfigurationDefaults.DefaultRemoteListUrl),
                RemoteDownloadUrlTemplate = NormalizeUrl(RemoteDownloadUrlTemplate, AgentConfigurationDefaults.DefaultRemoteDownloadUrlTemplate),
                HardnessCallbackUrl = NormalizeUrl(HardnessCallbackUrl, AgentConfigurationDefaults.DefaultHardnessCallbackUrl),
                ApiAuthToken = ApiAuthToken?.Trim() ?? string.Empty,
                RemoteSourceEnabled = RemoteSourceEnabled
            }
        };
    }

    public static AgentConfigurationModel FromFile(AgentConfigurationFile? file) {
        var section = file?.PrintBridge ?? new AgentConfigurationSection();
        var queueRootPath = !string.IsNullOrWhiteSpace(section.QueueRootPath)
            ? section.QueueRootPath
            : DeriveQueueRootPath(section.WatchPath);

        return new AgentConfigurationModel {
            QueueRootPath = NormalizeQueueRootPath(queueRootPath),
            DefaultPrinterName = string.IsNullOrWhiteSpace(section.DefaultPrinterName)
                ? AgentConfigurationDefaults.DefaultPrinterName
                : section.DefaultPrinterName,
            RemoteListUrl = NormalizeUrl(section.RemoteListUrl, AgentConfigurationDefaults.DefaultRemoteListUrl),
            RemoteDownloadUrlTemplate = NormalizeUrl(section.RemoteDownloadUrlTemplate, AgentConfigurationDefaults.DefaultRemoteDownloadUrlTemplate),
            HardnessCallbackUrl = NormalizeUrl(section.HardnessCallbackUrl, AgentConfigurationDefaults.DefaultHardnessCallbackUrl),
            ApiAuthToken = section.ApiAuthToken ?? string.Empty,
            RemoteSourceEnabled = section.RemoteSourceEnabled
        };
    }

    private static string DeriveQueueRootPath(string? watchPath) {
        if (string.IsNullOrWhiteSpace(watchPath)) {
            return AgentConfigurationDefaults.DefaultQueueRootPath;
        }

        var normalizedWatchPath = NormalizeQueueRootPath(watchPath);
        var folderName = Path.GetFileName(normalizedWatchPath);
        if (folderName.Equals(AgentConfigurationDefaults.DefaultInboxFolderName, StringComparison.OrdinalIgnoreCase)) {
            var parent = Path.GetDirectoryName(normalizedWatchPath);
            if (!string.IsNullOrWhiteSpace(parent)) {
                return parent;
            }
        }

        return normalizedWatchPath;
    }

    private static string NormalizeQueueRootPath(string? queueRootPath) {
        if (string.IsNullOrWhiteSpace(queueRootPath)) {
            return AgentConfigurationDefaults.DefaultQueueRootPath;
        }

        return queueRootPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeUrl(string? value, string fallback) {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}
