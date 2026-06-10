using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.Update;

public sealed class GithubReleaseUpdateService(HttpClient httpClient) : IUpdateService {
    private const string LatestReleaseUrl = "https://api.github.com/repos/FelipeKadanos/hardness-print-bridge/releases/latest";

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken) {
        EnsureHeaders();

        var release = await httpClient.GetFromJsonAsync<GithubReleaseResponse>(LatestReleaseUrl, cancellationToken);
        var currentVersion = GetCurrentVersion();
        if (release is null || string.IsNullOrWhiteSpace(release.TagName)) {
            return new UpdateCheckResult {
                UpdateAvailable = false,
                CurrentVersion = currentVersion
            };
        }

        var latestVersion = release.TagName.Trim().TrimStart('v', 'V');
        var updateAvailable = CompareVersions(currentVersion, latestVersion) < 0;
        var zipAsset = release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        return new UpdateCheckResult {
            UpdateAvailable = updateAvailable,
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            ReleaseName = release.Name,
            DownloadUrl = zipAsset?.BrowserDownloadUrl
        };
    }

    public async Task BeginUpdateAsync(UpdateCheckResult update, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl)) {
            throw new InvalidOperationException("No ZIP asset was found in the latest GitHub Release.");
        }

        var packagePath = await UpdatePackageDownloader.DownloadAsync(httpClient, update.DownloadUrl, cancellationToken);
        var updaterExecutablePath = CopyUpdaterBundleToTemp();
        var restartExecutablePath = Application.ExecutablePath;
        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var arguments = string.Join(' ', [
            "--package", Quote(packagePath),
            "--target", Quote(installDirectory),
            "--restart", Quote(restartExecutablePath),
            "--service", Quote("HardnessPrintBridgeAgent"),
            "--process-id", Environment.ProcessId.ToString()
        ]);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
            FileName = updaterExecutablePath,
            Arguments = arguments,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(updaterExecutablePath) ?? AppContext.BaseDirectory
        });
    }

    private void EnsureHeaders() {
        if (httpClient.DefaultRequestHeaders.UserAgent.Count > 0) {
            return;
        }

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HardnessPrintBridgeApp/1.0");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    private static string GetCurrentVersion() {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion)) {
            return informationalVersion.Split('+')[0].TrimStart('v', 'V');
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static int CompareVersions(string currentVersion, string latestVersion) {
        var current = ParseVersion(currentVersion);
        var latest = ParseVersion(latestVersion);
        return current.CompareTo(latest);
    }

    private static Version ParseVersion(string value) {
        return Version.TryParse(value, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static string CopyUpdaterBundleToTemp() {
        var updaterSourceDirectory = ResolveUpdaterSourceDirectory();
        if (updaterSourceDirectory is null) {
            throw new FileNotFoundException("Updater bundle was not found.");
        }

        var workspacePath = Path.Combine(RuntimePaths.GetUpdateWorkspacePath(), "updater");
        Directory.CreateDirectory(workspacePath);

        foreach (var filePath in Directory.GetFiles(updaterSourceDirectory, "*", SearchOption.TopDirectoryOnly)) {
            var destinationPath = Path.Combine(workspacePath, Path.GetFileName(filePath));
            File.Copy(filePath, destinationPath, overwrite: true);
        }

        var executablePath = Path.Combine(workspacePath, "Hardness.PrintBridge.Updater.exe");
        if (!File.Exists(executablePath)) {
            throw new FileNotFoundException("Updater executable was not found after copying bundle.");
        }

        return executablePath;
    }

    private static string? ResolveUpdaterSourceDirectory() {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Hardness.PrintBridge.Updater.exe"))) {
            return AppContext.BaseDirectory;
        }

        var candidates = new[] {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Hardness.PrintBridge.Updater\bin\Debug\net10.0-windows")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Hardness.PrintBridge.Updater\bin\Release\net10.0-windows"))
        };

        foreach (var directoryPath in candidates) {
            if (Directory.Exists(directoryPath)) {
                if (File.Exists(Path.Combine(directoryPath, "Hardness.PrintBridge.Updater.exe"))) {
                    return directoryPath;
                }
            }
        }

        return null;
    }

    private static string Quote(string value) {
        return $"\"{value}\"";
    }

    private sealed record GithubReleaseResponse(
        string TagName,
        string? Name,
        IReadOnlyList<GithubReleaseAsset> Assets) {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = TagName;

        [JsonPropertyName("name")]
        public string? Name { get; init; } = Name;

        [JsonPropertyName("assets")]
        public IReadOnlyList<GithubReleaseAsset> Assets { get; init; } = Assets;
    }

    private sealed record GithubReleaseAsset(string Name, string BrowserDownloadUrl) {
        [JsonPropertyName("name")]
        public string Name { get; init; } = Name;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = BrowserDownloadUrl;
    }
}
