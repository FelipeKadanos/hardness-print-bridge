namespace Hardness.PrintBridge.App.Services;

public sealed record AppSettings {
    public string InstallPath { get; init; } = AppContext.BaseDirectory;
    public bool StartWithWindows { get; init; } = true;
    public bool CheckForUpdatesOnStartup { get; init; } = true;
    public int UpdateCheckIntervalHours { get; init; } = 6;
    public bool MinimizeToTrayOnClose { get; init; } = true;
}
