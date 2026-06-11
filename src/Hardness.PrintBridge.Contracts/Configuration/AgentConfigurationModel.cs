namespace Hardness.PrintBridge.Contracts.Configuration;

public sealed record AgentConfigurationModel {
    public string QueueRootPath { get; init; } = AgentConfigurationDefaults.DefaultQueueRootPath;
    public string WatchPath { get; init; } = Path.Combine(AgentConfigurationDefaults.DefaultQueueRootPath, AgentConfigurationDefaults.DefaultInboxFolderName);
    public string ProcessingPath { get; init; } = Path.Combine(AgentConfigurationDefaults.DefaultQueueRootPath, AgentConfigurationDefaults.DefaultProcessingFolderName);
    public string PrintedPath { get; init; } = Path.Combine(AgentConfigurationDefaults.DefaultQueueRootPath, AgentConfigurationDefaults.DefaultPrintedFolderName);
    public string ErrorPath { get; init; } = Path.Combine(AgentConfigurationDefaults.DefaultQueueRootPath, AgentConfigurationDefaults.DefaultErrorFolderName);
    public string PrinterName { get; init; } = string.Empty;
    public string DefaultPrinterName { get; init; } = AgentConfigurationDefaults.DefaultPrinterName;
    public int PollIntervalMs { get; init; } = AgentConfigurationDefaults.DefaultPollIntervalMs;
    public string RemoteListUrl { get; init; } = AgentConfigurationDefaults.DefaultRemoteListUrl;
    public string RemoteDownloadUrlTemplate { get; init; } = AgentConfigurationDefaults.DefaultRemoteDownloadUrlTemplate;
    public int? RemotePollIntervalMs { get; init; } = AgentConfigurationDefaults.DefaultRemotePollIntervalMs;
    public int RemoteTimeoutMs { get; init; } = AgentConfigurationDefaults.DefaultRemoteTimeoutMs;
    public int RemoteMaxFilesPerCycle { get; init; } = AgentConfigurationDefaults.DefaultRemoteMaxFilesPerCycle;
    public bool RemoteAllowInsecureTls { get; init; } = AgentConfigurationDefaults.DefaultRemoteAllowInsecureTls;
    public string RemoteSeenCachePath { get; init; } = AgentConfigurationDefaults.DefaultRemoteSeenCachePath;
    public string LogLevel { get; init; } = AgentConfigurationDefaults.DefaultLogLevel;
    public string HardnessCallbackUrl { get; init; } = AgentConfigurationDefaults.DefaultHardnessCallbackUrl;
    public string ApiAuthToken { get; init; } = string.Empty;
    public bool RemoteSourceEnabled { get; init; } = true;

    public AgentConfigurationSection ToSection() {
        var queueRootPath = NormalizeQueueRootPath(QueueRootPath);
        return new AgentConfigurationSection {
            QueueRootPath = queueRootPath,
            WatchPath = NormalizePath(WatchPath, Path.Combine(queueRootPath, AgentConfigurationDefaults.DefaultInboxFolderName)),
            ProcessingPath = NormalizePath(ProcessingPath, Path.Combine(queueRootPath, AgentConfigurationDefaults.DefaultProcessingFolderName)),
            PrintedPath = NormalizePath(PrintedPath, Path.Combine(queueRootPath, AgentConfigurationDefaults.DefaultPrintedFolderName)),
            ErrorPath = NormalizePath(ErrorPath, Path.Combine(queueRootPath, AgentConfigurationDefaults.DefaultErrorFolderName)),
            PrinterName = PrinterName?.Trim() ?? string.Empty,
            DefaultPrinterName = string.IsNullOrWhiteSpace(DefaultPrinterName)
                ? AgentConfigurationDefaults.DefaultPrinterName
                : DefaultPrinterName.Trim(),
            PollIntervalMs = PollIntervalMs <= 0 ? AgentConfigurationDefaults.DefaultPollIntervalMs : PollIntervalMs,
            RemoteListUrl = NormalizeUrl(RemoteListUrl, AgentConfigurationDefaults.DefaultRemoteListUrl),
            RemoteDownloadUrlTemplate = NormalizeUrl(RemoteDownloadUrlTemplate, AgentConfigurationDefaults.DefaultRemoteDownloadUrlTemplate),
            RemotePollIntervalMs = RemotePollIntervalMs is > 0
                ? RemotePollIntervalMs
                : AgentConfigurationDefaults.DefaultRemotePollIntervalMs,
            RemoteTimeoutMs = RemoteTimeoutMs <= 0 ? AgentConfigurationDefaults.DefaultRemoteTimeoutMs : RemoteTimeoutMs,
            RemoteMaxFilesPerCycle = RemoteMaxFilesPerCycle <= 0 ? AgentConfigurationDefaults.DefaultRemoteMaxFilesPerCycle : RemoteMaxFilesPerCycle,
            RemoteAllowInsecureTls = RemoteAllowInsecureTls,
            RemoteSeenCachePath = NormalizePath(RemoteSeenCachePath, AgentConfigurationDefaults.DefaultRemoteSeenCachePath),
            LogLevel = NormalizeValue(LogLevel, AgentConfigurationDefaults.DefaultLogLevel),
            HardnessCallbackUrl = NormalizeUrl(HardnessCallbackUrl, AgentConfigurationDefaults.DefaultHardnessCallbackUrl),
            ApiAuthToken = ApiAuthToken?.Trim() ?? string.Empty,
            RemoteSourceEnabled = RemoteSourceEnabled
        };
    }

    public AgentConfigurationFile ToFile() {
        return new AgentConfigurationFile {
            PrintBridge = ToSection()
        };
    }

    public static AgentConfigurationModel FromSection(AgentConfigurationSection? section) {
        section ??= new AgentConfigurationSection();
        var queueRootPath = !string.IsNullOrWhiteSpace(section.QueueRootPath)
            ? section.QueueRootPath
            : DeriveQueueRootPath(section.WatchPath);

        var normalizedQueueRootPath = NormalizeQueueRootPath(queueRootPath);
        return new AgentConfigurationModel {
            QueueRootPath = normalizedQueueRootPath,
            WatchPath = NormalizePath(section.WatchPath, Path.Combine(normalizedQueueRootPath, AgentConfigurationDefaults.DefaultInboxFolderName)),
            ProcessingPath = NormalizePath(section.ProcessingPath, Path.Combine(normalizedQueueRootPath, AgentConfigurationDefaults.DefaultProcessingFolderName)),
            PrintedPath = NormalizePath(section.PrintedPath, Path.Combine(normalizedQueueRootPath, AgentConfigurationDefaults.DefaultPrintedFolderName)),
            ErrorPath = NormalizePath(section.ErrorPath, Path.Combine(normalizedQueueRootPath, AgentConfigurationDefaults.DefaultErrorFolderName)),
            PrinterName = section.PrinterName ?? string.Empty,
            DefaultPrinterName = NormalizeValue(section.DefaultPrinterName, AgentConfigurationDefaults.DefaultPrinterName),
            PollIntervalMs = section.PollIntervalMs <= 0 ? AgentConfigurationDefaults.DefaultPollIntervalMs : section.PollIntervalMs,
            RemoteListUrl = NormalizeUrl(section.RemoteListUrl, AgentConfigurationDefaults.DefaultRemoteListUrl),
            RemoteDownloadUrlTemplate = NormalizeUrl(section.RemoteDownloadUrlTemplate, AgentConfigurationDefaults.DefaultRemoteDownloadUrlTemplate),
            RemotePollIntervalMs = section.RemotePollIntervalMs is > 0
                ? section.RemotePollIntervalMs
                : AgentConfigurationDefaults.DefaultRemotePollIntervalMs,
            RemoteTimeoutMs = section.RemoteTimeoutMs <= 0 ? AgentConfigurationDefaults.DefaultRemoteTimeoutMs : section.RemoteTimeoutMs,
            RemoteMaxFilesPerCycle = section.RemoteMaxFilesPerCycle <= 0 ? AgentConfigurationDefaults.DefaultRemoteMaxFilesPerCycle : section.RemoteMaxFilesPerCycle,
            RemoteAllowInsecureTls = section.RemoteAllowInsecureTls,
            RemoteSeenCachePath = NormalizePath(section.RemoteSeenCachePath, AgentConfigurationDefaults.DefaultRemoteSeenCachePath),
            LogLevel = NormalizeValue(section.LogLevel, AgentConfigurationDefaults.DefaultLogLevel),
            HardnessCallbackUrl = NormalizeUrl(section.HardnessCallbackUrl, AgentConfigurationDefaults.DefaultHardnessCallbackUrl),
            ApiAuthToken = section.ApiAuthToken ?? string.Empty,
            RemoteSourceEnabled = section.RemoteSourceEnabled
        };
    }

    public static AgentConfigurationModel FromFile(AgentConfigurationFile? file) {
        return FromSection(file?.PrintBridge);
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

    private static string NormalizePath(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }

        return value.Trim();
    }

    private static string NormalizeUrl(string? value, string fallback) {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static string NormalizeValue(string? value, string fallback) {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}
