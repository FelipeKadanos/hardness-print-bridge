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
    private bool _isFirstFetchCycle = true;

    public async Task<RemoteFetchResult> FetchAsync(CancellationToken cancellationToken) {
        if (!_options.RemoteSourceEnabled) {
            return RemoteFetchResult.Disabled;
        }

        var firstCycleMode = _isFirstFetchCycle;
        if (_isFirstFetchCycle) {
            await ClearSeenCacheAsync(cancellationToken);
            _isFirstFetchCycle = false;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _nextRemotePollAtUtc) {
            return new RemoteFetchResult { SkippedBySchedule = true };
        }

        var result = new RemoteFetchResult();
        try {
            var listResponse = await ListRemoteFilesAsync(cancellationToken);
            var remoteFiles = listResponse.Files
                .Where(static f => !string.IsNullOrWhiteSpace(f.Name))
                .Take(_options.RemoteMaxFilesPerCycle)
                .ToArray();

            result = result with { ListedCount = remoteFiles.Length };
            if (remoteFiles.Length == 0) {
                SetNextPollWithoutBackoff();
                return result;
            }

            var seen = await LoadSeenSetAsync(cancellationToken);
            foreach (var remoteFile in remoteFiles) {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = remoteFile.Name.Trim();

                if (remoteFile.Size is 0) {
                    logger.LogWarning(
                        "Skipping remote file '{FileName}' because it is empty (0 bytes).",
                        fileName);
                    result = result with { SkippedCount = result.SkippedCount + 1 };
                    continue;
                }

                if (!firstCycleMode && ShouldSkip(fileName, seen)) {
                    result = result with { SkippedCount = result.SkippedCount + 1 };
                    continue;
                }

                try {
                    var bytes = await DownloadFileAsync(fileName, cancellationToken);
                    var savedFileName = await SaveToInboxAtomicallyAsync(fileName, bytes, firstCycleMode, cancellationToken);
                    seen.Add(fileName);
                    logger.LogInformation(
                        "Remote file '{RemoteFileName}' saved to inbox as '{SavedFileName}'.",
                        fileName,
                        savedFileName);
                    result = result with { DownloadedCount = result.DownloadedCount + 1 };
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    logger.LogError(ex, "Failed to fetch remote file '{FileName}'.", fileName);
                    result = result with { FailedCount = result.FailedCount + 1 };
                }
            }

            await SaveSeenSetAsync(seen, cancellationToken);
            if (result.FailedCount > 0) {
                ApplyBackoff();
                return result with { BackoffApplied = true };
            }

            _consecutiveFailures = 0;
            SetNextPollWithoutBackoff();
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

        var listUrl = PrintBridgeUrlResolver.Resolve(_options.RemoteListUrl, _options.ApiAuthToken);
        var response = await httpClient.GetAsync(listUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfApiReturnedFailure(rawJson, "listagem remota");
        return ParseRemoteListPayload(rawJson);
    }

    private async Task<byte[]> DownloadFileAsync(string fileName, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(_options.RemoteDownloadUrlTemplate)) {
            throw new InvalidOperationException("RemoteDownloadUrlTemplate is required when remote source is enabled.");
        }

        var encodedName = Uri.EscapeDataString(fileName);
        var downloadUrl = PrintBridgeUrlResolver.Resolve(_options.RemoteDownloadUrlTemplate, _options.ApiAuthToken)
            .Replace("{fileName}", encodedName, StringComparison.Ordinal);
        var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        ThrowIfApiReturnedFailure(bytes, "download remoto", fileName);
        return bytes;
    }

    private async Task<string> SaveToInboxAtomicallyAsync(
        string fileName,
        byte[] content,
        bool firstCycleMode,
        CancellationToken cancellationToken) {
        Directory.CreateDirectory(_options.WatchPath);

        var finalPath = ResolveInboxTargetPath(fileName, firstCycleMode);

        var tempPath = Path.Combine(_options.WatchPath, $"{fileName}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken);

        try {
            File.Move(tempPath, finalPath, overwrite: false);
            return Path.GetFileName(finalPath);
        } catch (IOException) {
            if (!File.Exists(finalPath)) {
                throw;
            }

            // Non-first-cycle collision means "already present", nothing to do.
            return Path.GetFileName(finalPath);
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

    private async Task ClearSeenCacheAsync(CancellationToken cancellationToken) {
        await _seenLock.WaitAsync(cancellationToken);
        try {
            var path = ResolveSeenCachePath();
            if (File.Exists(path)) {
                File.Delete(path);
                logger.LogInformation(
                    "Startup remote reset: cleared seen cache at '{SeenCachePath}' to allow re-download.",
                    path);
            } else {
                logger.LogInformation(
                    "Startup remote reset: no seen cache found at '{SeenCachePath}'.",
                    path);
            }
        } finally {
            _seenLock.Release();
        }
    }

    private string ResolveInboxTargetPath(string fileName, bool firstCycleMode) {
        var basePath = Path.Combine(_options.WatchPath, fileName);
        if (!File.Exists(basePath)) {
            return basePath;
        }

        if (!firstCycleMode) {
            return basePath;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var uniqueName = $"{nameWithoutExtension}__startup-redownload-{stamp}{extension}";
        return Path.Combine(_options.WatchPath, uniqueName);
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
            ?? TryGetString(element, "nome")
            ?? TryGetString(element, "fileName")
            ?? TryGetString(element, "filename");

        if (string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        var tipo = TryGetString(element, "tipo");
        if (!string.IsNullOrWhiteSpace(tipo)
            && !tipo.Equals("arquivo", StringComparison.OrdinalIgnoreCase)
            && !tipo.Equals("file", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        file = new RemoteFileItem {
            Name = name,
            Size = TryGetLong(element, "size") ?? TryGetLong(element, "tamanho_bytes"),
            ModifiedUtc = TryGetDate(element, "modifiedUtc")
                ?? TryGetDate(element, "modified")
                ?? TryGetDate(element, "modificado_em")
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

    private static void ThrowIfApiReturnedFailure(string rawJson, string operationName) {
        using var document = JsonDocument.Parse(rawJson);
        ThrowIfApiReturnedFailure(document.RootElement, operationName);
    }

    private static void ThrowIfApiReturnedFailure(byte[] payload, string operationName, string fileName) {
        if (payload.Length == 0) {
            return;
        }

        var firstByte = payload[0];
        if (firstByte is not (byte)'{' and not (byte)'[') {
            return;
        }

        using var document = JsonDocument.Parse(payload);
        ThrowIfApiReturnedFailure(document.RootElement, operationName, fileName);
    }

    private static void ThrowIfApiReturnedFailure(JsonElement root, string operationName, string? fileName = null) {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("sucesso", out var successProperty)) {
            return;
        }

        var success = successProperty.ValueKind == JsonValueKind.True;
        if (success) {
            return;
        }

        var errorMessage = TryGetString(root, "erro")
            ?? TryGetString(root, "message")
            ?? "Erro remoto sem mensagem detalhada.";
        var fileSuffix = string.IsNullOrWhiteSpace(fileName) ? string.Empty : $" para '{fileName}'";
        throw new InvalidDataException($"Falha na {operationName}{fileSuffix}: {errorMessage}");
    }

    private sealed class RemoteListResponse {
        public List<RemoteFileItem> Files { get; init; } = [];
    }

    private sealed class RemoteFileItem {
        public string Name { get; init; } = string.Empty;
        public long? Size { get; init; }
        public DateTimeOffset? ModifiedUtc { get; init; }
    }
}
