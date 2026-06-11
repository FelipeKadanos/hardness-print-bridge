namespace Hardness.PrintBridge.Contracts.Configuration;

public static class AgentConfigurationDefaults {
    public const string DefaultQueueRootPath = @"C:\Hardness-Print-Brige\print-agent";
    public const string DefaultInboxFolderName = "inbox";
    public const string DefaultProcessingFolderName = "processing";
    public const string DefaultPrintedFolderName = "printed";
    public const string DefaultErrorFolderName = "error";
    public const string DefaultPrinterName = "Microsoft Print to PDF";
    public const int DefaultPollIntervalMs = 10000;
    public const string DefaultRemoteListUrl = "http://localhost/api/rel/list_files?API_AUTH=REPLACE_ME";
    public const string DefaultRemoteDownloadUrlTemplate = "http://localhost/api/rel/select_file?API_AUTH=REPLACE_ME&file={fileName}";
    public const string DefaultHardnessCallbackUrl = "http://localhost/api/rel/callback?API_AUTH=REPLACE_ME";
    public const int DefaultRemotePollIntervalMs = 10000;
    public const int DefaultRemoteTimeoutMs = 10000;
    public const int DefaultRemoteMaxFilesPerCycle = 20;
    public const bool DefaultRemoteAllowInsecureTls = true;
    public const string DefaultRemoteSeenCachePath = @"meta\remote-seen.json";
    public const string DefaultLogLevel = "Information";
}
