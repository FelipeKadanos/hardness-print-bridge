namespace Hardness.PrintBridge.Contracts.Configuration;

public sealed class AgentConfigurationFile {
    public AgentConfigurationSection PrintBridge { get; init; } = new();
}

public sealed class AgentConfigurationSection {
    public string QueueRootPath { get; init; } = AgentConfigurationDefaults.DefaultQueueRootPath;
    public string WatchPath { get; init; } = Path.Combine(AgentConfigurationDefaults.DefaultQueueRootPath, AgentConfigurationDefaults.DefaultInboxFolderName);
    public string ProcessingPath { get; init; } = Path.Combine(AgentConfigurationDefaults.DefaultQueueRootPath, AgentConfigurationDefaults.DefaultProcessingFolderName);
    public string PrintedPath { get; init; } = Path.Combine(AgentConfigurationDefaults.DefaultQueueRootPath, AgentConfigurationDefaults.DefaultPrintedFolderName);
    public string ErrorPath { get; init; } = Path.Combine(AgentConfigurationDefaults.DefaultQueueRootPath, AgentConfigurationDefaults.DefaultErrorFolderName);
    public string DefaultPrinterName { get; init; } = AgentConfigurationDefaults.DefaultPrinterName;
    public string RemoteListUrl { get; init; } = AgentConfigurationDefaults.DefaultRemoteListUrl;
    public string RemoteDownloadUrlTemplate { get; init; } = AgentConfigurationDefaults.DefaultRemoteDownloadUrlTemplate;
    public string HardnessCallbackUrl { get; init; } = AgentConfigurationDefaults.DefaultHardnessCallbackUrl;
    public string ApiAuthToken { get; init; } = string.Empty;
    public bool RemoteSourceEnabled { get; init; } = true;
}
