using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.Update;

internal static class UpdatePackageDownloader {
    public static async Task<string> DownloadAsync(
        HttpClient httpClient,
        string downloadUrl,
        CancellationToken cancellationToken) {
        var workspacePath = RuntimePaths.GetUpdateWorkspacePath();
        Directory.CreateDirectory(workspacePath);

        var zipPath = Path.Combine(workspacePath, "package.zip");
        await using var downloadStream = await httpClient.GetStreamAsync(downloadUrl, cancellationToken);
        await using var fileStream = File.Create(zipPath);
        await downloadStream.CopyToAsync(fileStream, cancellationToken);
        return zipPath;
    }
}
