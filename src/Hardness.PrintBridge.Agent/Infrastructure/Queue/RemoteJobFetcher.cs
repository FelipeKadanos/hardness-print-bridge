using System.Text.Json;
using Hardness.PrintBridge.Agent.Application;
using Hardness.PrintBridge.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace Hardness.PrintBridge.Agent.Infrastructure.Queue;

public sealed class RemoteJobFetcher(
    HttpClient httpClient,
    IOptions<PrintBridgeOptions> options,
    ILogger<RemoteJobFetcher> logger) : IRemoteJobFetcher {
    private readonly PrintBridgeOptions _options = options.Value;
    private readonly SemaphoreSlim _seenLock = new(1, 1);
    private DateTimeOffset _nextRemotePollAtUtc = DateTimeOffset.MinValue;
    private int _consecutiveFailures = 0;

    public async Task<RemoteFetchResult> FetchAsync(CancellationToken cancellationToken) {
        if (!_options.RemoteSourceEnabled) {
            return RemoteFetchResult.Disabled;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _nextRemotePollAtUtc) {
            return new RemoteFetchResult { SkippedBySchedule = true };
        }

        var result = new RemoteFetchResult();
        try {
            logger.LogInformation(
                "Remote fetch cycle started. ListUrl='{RemoteListUrl}', MaxFilesPerCycle={MaxFilesPerCycle}.",
                SanitizeUrlForLogs(_options.RemoteListUrl),
                _options.RemoteMaxFilesPerCycle);

            var listResponse = await ListRemoteFilesAsync(cancellationToken);
            var remoteFiles = listResponse.Files
                .Where(static f => !string.IsNullOrWhiteSpace(f.Name))
                .Where(static f => f.Name.EndsWith(".etq", StringComparison.OrdinalIgnoreCase))
                .Take(_options.RemoteMaxFilesPerCycle)
                .ToArray();

            result = result with { ListedCount = remoteFiles.Length };
            logger.LogInformation("Remote list returned {ListedCount} candidate .etq file(s).", remoteFiles.Length);
            if (remoteFiles.Length == 0) {
                SetNextPollWithoutBackoff();
                logger.LogInformation("Remote fetch cycle finished with no files.");
                return result;
            }

            var seen = await LoadSeenSetAsync(cancellationToken);
            foreach (var remoteFile in remoteFiles) {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = remoteFile.Name.Trim();

                if (ShouldSkip(fileName, seen)) {
                    logger.LogDebug("Remote file '{FileName}' skipped (already present/seen).", fileName);
                    result = result with { SkippedCount = result.SkippedCount + 1 };
                    continue;
                }

                try {
                    var bytes = await DownloadFileAsync(fileName, cancellationToken);
                    await SaveToInboxAtomicallyAsync(fileName, bytes, cancellationToken);
                    seen.Add(fileName);
                    logger.LogInformation("Remote file '{FileName}' downloaded to inbox ({ByteCount} bytes).", fileName, bytes.Length);
                    result = result with { DownloadedCount = result.DownloadedCount + 1 };
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    logger.LogError(ex, "Failed to fetch remote file '{FileName}'.", fileName);
                    result = result with { FailedCount = result.FailedCount + 1 };
                }
            }

            await SaveSeenSetAsync(seen, cancellationToken);
            if (result.FailedCount > 0) {
                ApplyBackoff();
                logger.LogWarning(
                    "Remote fetch cycle finished with failures. Downloaded={DownloadedCount}, Skipped={SkippedCount}, Failed={FailedCount}. Backoff applied.",
                    result.DownloadedCount,
                    result.SkippedCount,
                    result.FailedCount);
                return result with { BackoffApplied = true };
            }

            _consecutiveFailures = 0;
            SetNextPollWithoutBackoff();
            logger.LogInformation(
                "Remote fetch cycle finished successfully. Downloaded={DownloadedCount}, Skipped={SkippedCount}, Failed={FailedCount}.",
                result.DownloadedCount,
                result.SkippedCount,
                result.FailedCount);
            return result;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogError(ex, "Remote fetch cycle failed.");
            ApplyBackoff();
            return result with { FailedCount = result.FailedCount + 1, BackoffApplied = true };
        }
    }

    private async Task<RemoteListResponse> ListRemoteFilesAsync(CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(_options.RemoteListUrl)) {
            throw new InvalidOperationException("RemoteListUrl is required when remote source is enabled.");
        }

        var response = await httpClient.GetAsync(_options.RemoteListUrl, cancellationToken);
        logger.LogDebug("Remote list HTTP status: {StatusCode}.", (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseRemoteListPayload(rawJson);
    }

    private async Task<byte[]> DownloadFileAsync(string fileName, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(_options.RemoteDownloadUrlTemplate)) {
            throw new InvalidOperationException("RemoteDownloadUrlTemplate is required when remote source is enabled.");
        }

        var encodedName = Uri.EscapeDataString(fileName);
        var downloadUrl = _options.RemoteDownloadUrlTemplate.Replace("{fileName}", encodedName, StringComparison.Ordinal);
        var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
        logger.LogDebug("Remote download status for '{FileName}': {StatusCode}.", fileName, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task SaveToInboxAtomicallyAsync(string fileName, byte[] content, CancellationToken cancellationToken) {
        Directory.CreateDirectory(_options.WatchPath);

        var finalPath = Path.Combine(_options.WatchPath, fileName);
        if (File.Exists(finalPath)) {
            return;
        }

        var tempPath = Path.Combine(_options.WatchPath, $"{fileName}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken);

        try {
            File.Move(tempPath, finalPath, overwrite: false);
        } catch (IOException) {
            if (!File.Exists(finalPath)) {
                throw;
            }
        } finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }

    private bool ShouldSkip(string fileName, HashSet<string> seen) {
        if (seen.Contains(fileName)) {
            return true;
        }

        return File.Exists(Path.Combine(_options.WatchPath, fileName))
            || File.Exists(Path.Combine(_options.ProcessingPath, fileName))
            || File.Exists(Path.Combine(_options.PrintedPath, fileName))
            || File.Exists(Path.Combine(_options.ErrorPath, fileName));
    }

    private async Task<HashSet<string>> LoadSeenSetAsync(CancellationToken cancellationToken) {
        await _seenLock.WaitAsync(cancellationToken);
        try {
            var path = ResolveSeenCachePath();
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) {
                Directory.CreateDirectory(parent);
            }

            if (!File.Exists(path)) {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            await using var stream = File.OpenRead(path);
            var payload = await JsonSerializer.DeserializeAsync<RemoteSeenCache>(
                stream,
                cancellationToken: cancellationToken);

            return payload?.Files is { Count: > 0 }
                ? new HashSet<string>(payload.Files, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        } finally {
            _seenLock.Release();
        }
    }

    private async Task SaveSeenSetAsync(HashSet<string> seen, CancellationToken cancellationToken) {
        await _seenLock.WaitAsync(cancellationToken);
        try {
            var path = ResolveSeenCachePath();
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) {
                Directory.CreateDirectory(parent);
            }

            var payload = new RemoteSeenCache { Files = seen.OrderBy(static x => x).ToList() };
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, payload, cancellationToken: cancellationToken);
        } finally {
            _seenLock.Release();
        }
    }

    private string ResolveSeenCachePath() {
        if (Path.IsPathRooted(_options.RemoteSeenCachePath)) {
            return _options.RemoteSeenCachePath;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.RemoteSeenCachePath));
    }

    private void SetNextPollWithoutBackoff() {
        var interval = _options.RemotePollIntervalMs ?? _options.PollIntervalMs;
        _nextRemotePollAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(interval);
    }

    private void ApplyBackoff() {
        _consecutiveFailures++;
        var baseInterval = _options.RemotePollIntervalMs ?? _options.PollIntervalMs;
        var multiplier = Math.Min(_consecutiveFailures, 5);
        var delayMs = baseInterval * multiplier;
        _nextRemotePollAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
    }

    private sealed class RemoteSeenCache {
        public List<string> Files { get; init; } = [];
    }

    private static RemoteListResponse ParseRemoteListPayload(string rawJson) {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        var files = new List<RemoteFileItem>();

        if (root.ValueKind == JsonValueKind.Array) {
            ExtractFromArray(root, files);
            return new RemoteListResponse { Files = files };
        }

        if (root.ValueKind != JsonValueKind.Object) {
            return new RemoteListResponse();
        }

        foreach (var propertyName in new[] { "files", "data", "arquivos", "itens", "items" }) {
            if (!root.TryGetProperty(propertyName, out var arrayElement) || arrayElement.ValueKind != JsonValueKind.Array) {
                continue;
            }

            ExtractFromArray(arrayElement, files);
            if (files.Count > 0) {
                return new RemoteListResponse { Files = files };
            }
        }

        // Single object fallback.
        if (TryReadFileItem(root, out var singleItem)) {
            files.Add(singleItem);
        }

        return new RemoteListResponse { Files = files };
    }

    private static void ExtractFromArray(JsonElement arrayElement, List<RemoteFileItem> destination) {
        foreach (var item in arrayElement.EnumerateArray()) {
            if (TryReadFileItem(item, out var file)) {
                destination.Add(file);
            }
        }
    }

    private static bool TryReadFileItem(JsonElement element, out RemoteFileItem file) {
        file = new RemoteFileItem();

        if (element.ValueKind == JsonValueKind.String) {
            var value = element.GetString();
            if (string.IsNullOrWhiteSpace(value)) {
                return false;
            }

            file = new RemoteFileItem { Name = value };
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object) {
            return false;
        }

        var name = TryGetString(element, "name")
            ?? TryGetString(element, "arquivo")
            ?? TryGetString(element, "fileName")
            ?? TryGetString(element, "filename");

        if (string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        file = new RemoteFileItem {
            Name = name,
            Size = TryGetLong(element, "size"),
            ModifiedUtc = TryGetDate(element, "modifiedUtc") ?? TryGetDate(element, "modified")
        };
        return true;
    }

    private static string? TryGetString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String) {
            return null;
        }

        return value.GetString();
    }

    private static long? TryGetLong(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number) {
            return null;
        }

        return value.TryGetInt64(out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? TryGetDate(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String) {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private sealed class RemoteListResponse {
        public List<RemoteFileItem> Files { get; init; } = [];
    }

    private sealed class RemoteFileItem {
        public string Name { get; init; } = string.Empty;
        public long? Size { get; init; }
        public DateTimeOffset? ModifiedUtc { get; init; }
    }

    private static string? SanitizeUrlForLogs(string? url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return url;
        }

        return url.Replace("API_AUTH=", "API_AUTH=***", StringComparison.OrdinalIgnoreCase);
    }
}
