using Hardness.PrintBridge.Agent.Application;
using Hardness.PrintBridge.Agent.Configuration;
using Hardness.PrintBridge.Agent.Infrastructure.Printing;
using Hardness.PrintBridge.Agent.Infrastructure.Runtime;
using Hardness.PrintBridge.Contracts.Runtime;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace Hardness.PrintBridge.Agent;

public class Worker(
    ILogger<Worker> logger,
    IOptions<PrintBridgeOptions> options,
    IRemoteJobFetcher remoteJobFetcher,
    IPrintJobParser printJobParser,
    IPrinterResolver printerResolver,
    IRawPrinterClient rawPrinterClient,
    IDocumentPrintFallbackClient documentPrintFallbackClient,
    AgentStatusWriter statusWriter,
    IHardnessCallbackClient callbackClient) : BackgroundService {
    private readonly PrintBridgeOptions _options = options.Value;
    private readonly DateTimeOffset _processStartedAtUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private const int MaxRetryAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        EnsureDirectories();
        await WriteStatusSafeAsync(AgentState.Starting, "Agent iniciando.", stoppingToken);

        logger.LogInformation(
            "Queue worker started. Watching '{WatchPath}' every {PollIntervalMs}ms.",
            _options.WatchPath,
            _options.PollIntervalMs);

        await RecoverProcessingQueueAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested) {
            var cycleStartedAt = DateTimeOffset.Now;
            var processedCount = 0;
            var failedCount = 0;
            var remoteDownloaded = 0;
            var remoteSkipped = 0;
            var remoteFailed = 0;

            try {
                var remoteResult = await remoteJobFetcher.FetchAsync(stoppingToken);
                remoteDownloaded += remoteResult.DownloadedCount;
                remoteSkipped += remoteResult.SkippedCount;
                remoteFailed += remoteResult.FailedCount;

                var processingResult = await ProcessProcessingBatchAsync(stoppingToken);
                processedCount += processingResult.ProcessedCount;
                failedCount += processingResult.FailedCount;

                var retryResult = await ProcessRetryBatchAsync(stoppingToken);
                processedCount += retryResult.ProcessedCount;
                failedCount += retryResult.FailedCount;

                var inboxResult = await ProcessInboxBatchAsync(stoppingToken);
                processedCount += inboxResult.ProcessedCount;
                failedCount += inboxResult.FailedCount;
            } catch (Exception ex) {
                failedCount++;
                logger.LogError(ex, "Unexpected error while processing queue batch.");
            }

            logger.LogInformation(
                "Queue cycle finished. StartedAt={StartedAt}, Processed={ProcessedCount}, Failed={FailedCount}, RemoteDownloaded={RemoteDownloaded}, RemoteSkipped={RemoteSkipped}, RemoteFailed={RemoteFailed}.",
                cycleStartedAt,
                processedCount,
                failedCount,
                remoteDownloaded,
                remoteSkipped,
                remoteFailed);

            var state = failedCount > 0 || remoteFailed > 0 ? AgentState.Warning : AgentState.Running;
            var message = state == AgentState.Warning
                ? "Agent executado com avisos no ultimo ciclo."
                : "Agent em execucao.";
            await WriteStatusSafeAsync(
                state,
                message,
                stoppingToken,
                processedCount,
                failedCount,
                remoteDownloaded,
                remoteSkipped,
                remoteFailed);

            await Task.Delay(_options.PollIntervalMs, stoppingToken);
        }

        await WriteStatusSafeAsync(AgentState.Stopped, "Agent finalizado.", CancellationToken.None);
    }

    private void EnsureDirectories() {
        Directory.CreateDirectory(_options.WatchPath);
        Directory.CreateDirectory(_options.ProcessingPath);
        Directory.CreateDirectory(_options.PrintedPath);
        Directory.CreateDirectory(_options.ErrorPath);
        Directory.CreateDirectory(GetRetryPath());
    }

    private async Task<BatchResult> RecoverProcessingQueueAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Recovering pending files from processing folder.");
        var recoveryResult = await ProcessProcessingBatchAsync(stoppingToken);
        logger.LogInformation(
            "Recovery finished. Processed={ProcessedCount}, Failed={FailedCount}.",
            recoveryResult.ProcessedCount,
            recoveryResult.FailedCount);
        return recoveryResult;
    }

    private async Task<BatchResult> ProcessInboxBatchAsync(CancellationToken stoppingToken) {
        var files = GetQueueFiles(_options.WatchPath);
        var result = new BatchResult();

        if (files.Length == 0) {
            return result;
        }

        logger.LogInformation("Found {FileCount} file(s) in inbox.", files.Length);

        foreach (var sourcePath in files) {
            stoppingToken.ThrowIfCancellationRequested();
            var fileResult = await ProcessSingleFileAsync(sourcePath, sourcePathIsProcessingPath: false, stoppingToken);
            result.ProcessedCount++;
            if (!fileResult.Success) {
                result.FailedCount++;
            }
        }

        return result;
    }

    private async Task<BatchResult> ProcessProcessingBatchAsync(CancellationToken stoppingToken) {
        var files = GetQueueFiles(_options.ProcessingPath);
        var result = new BatchResult();

        if (files.Length == 0) {
            return result;
        }

        logger.LogInformation("Found {FileCount} pending file(s) in processing.", files.Length);

        foreach (var processingPath in files) {
            stoppingToken.ThrowIfCancellationRequested();
            var fileResult = await ProcessSingleFileAsync(processingPath, sourcePathIsProcessingPath: true, stoppingToken);
            result.ProcessedCount++;
            if (!fileResult.Success) {
                result.FailedCount++;
            }
        }

        return result;
    }

    private async Task<BatchResult> ProcessRetryBatchAsync(CancellationToken stoppingToken) {
        var files = GetQueueFiles(GetRetryPath());
        var result = new BatchResult();

        if (files.Length == 0) {
            return result;
        }

        logger.LogInformation("Found {FileCount} pending file(s) in retry.", files.Length);

        foreach (var retryPath in files) {
            stoppingToken.ThrowIfCancellationRequested();
            var fileResult = await ProcessSingleFileAsync(retryPath, sourcePathIsProcessingPath: false, stoppingToken);
            result.ProcessedCount++;
            if (!fileResult.Success) {
                result.FailedCount++;
            }
        }

        return result;
    }

    private async Task<FileResult> ProcessSingleFileAsync(
        string sourcePath,
        bool sourcePathIsProcessingPath,
        CancellationToken cancellationToken) {
        var fileName = Path.GetFileName(sourcePath);
        var processingPath = sourcePathIsProcessingPath
            ? sourcePath
            : Path.Combine(_options.ProcessingPath, fileName);
        string? requestedPrinter = TryExtractRequestedPrinter(fileName);
        string? usedPrinter = null;
        var fileResult = new FileResult();
        var retryState = await LoadRetryStateAsync(sourcePath, cancellationToken);

        // Idempotence by filename: if already finalized, don't process again.
        if (AlreadyFinalized(fileName)) {
            logger.LogWarning(
                "Skipping '{FileName}' because it already exists in printed or error.",
                fileName);
            TryMoveDuplicateToError(sourcePath, fileName, "Duplicate filename already finalized.");
            DeleteRetryState(sourcePath);
            await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                FileName = fileName,
                Status = "error",
                RequestedPrinter = requestedPrinter,
                UsedPrinter = usedPrinter,
                Message = "Arquivo ignorado por duplicidade: nome de arquivo ja finalizado em printed/error."
            }, cancellationToken);
            fileResult.Success = false;
            return fileResult;
        }

        if (!sourcePathIsProcessingPath) {
            try {
                // Atomic move inbox -> processing as queue lock.
                File.Move(sourcePath, processingPath, overwrite: false);
                logger.LogInformation("Moved '{FileName}' to processing.", fileName);
                MoveRetryState(sourcePath, processingPath);
            } catch (IOException ioEx) {
                logger.LogWarning(ioEx, "Could not move '{FileName}' to processing. It may be in use.", fileName);
                fileResult.Success = false;
                return fileResult;
            }
        }

        try {
            var printJob = printJobParser.Parse(processingPath);
            requestedPrinter = printJob.RequestedPrinter ?? requestedPrinter;
            var resolvedPrinter = printerResolver.Resolve(printJob);
            usedPrinter = resolvedPrinter;

            try {
                rawPrinterClient.Print(resolvedPrinter, printJob.RawPayload, printJob.FileName);
            } catch (PrintJobProcessingException ex) when (documentPrintFallbackClient.CanPrint(printJob)) {
                logger.LogWarning(
                    ex,
                    "RAW printing failed for '{FileName}'. Trying fallback print route for extension '{Extension}'.",
                    printJob.FileName,
                    Path.GetExtension(printJob.SourcePath));
                documentPrintFallbackClient.Print(resolvedPrinter, printJob);
            }

            logger.LogInformation(
                "Printed '{FileName}' successfully. Requested printer: '{RequestedPrinter}', used printer: '{UsedPrinter}', payload bytes: {PayloadLength}.",
                printJob.FileName,
                printJob.RequestedPrinter ?? "(default)",
                resolvedPrinter,
                printJob.RawPayload.Length);

            var printedPath = Path.Combine(_options.PrintedPath, fileName);
            File.Move(processingPath, printedPath, overwrite: false);
            DeleteRetryState(processingPath);
            logger.LogInformation("File '{FileName}' processed and moved to printed.", fileName);

            await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                FileName = printJob.FileName,
                Status = "success",
                RequestedPrinter = requestedPrinter,
                UsedPrinter = resolvedPrinter,
                Message = $"Arquivo '{printJob.FileName}' impresso com sucesso na impressora '{resolvedPrinter}'."
            }, cancellationToken);
            fileResult.Success = true;
        } catch (PrinterResolutionException ex) {
            var handled = await TryScheduleRetryAsync(
                processingPath,
                fileName,
                retryState,
                ex.CanRetry,
                $"Printer resolution failed for '{fileName}': {ex.Message}",
                cancellationToken);
            if (!handled) {
                var errorPath = Path.Combine(_options.ErrorPath, fileName);
                SafeMoveToError(processingPath, errorPath);
                DeleteRetryState(processingPath);
                logger.LogError(ex, "Printer resolution failed for '{FileName}'.", fileName);
                await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                    FileName = fileName,
                    Status = "error",
                    RequestedPrinter = requestedPrinter,
                    UsedPrinter = usedPrinter,
                    Message = ex.Message
                }, cancellationToken);
            }
            fileResult.Success = false;
        } catch (InvalidDataException ex) {
            var errorPath = Path.Combine(_options.ErrorPath, fileName);
            SafeMoveToError(processingPath, errorPath);
            DeleteRetryState(processingPath);
            logger.LogError(ex, "Invalid print payload for '{FileName}'.", fileName);
            await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                FileName = fileName,
                Status = "error",
                RequestedPrinter = requestedPrinter,
                UsedPrinter = usedPrinter,
                Message = ex.Message
            }, cancellationToken);
            fileResult.Success = false;
        } catch (PrintJobProcessingException ex) {
            var handled = await TryScheduleRetryAsync(
                processingPath,
                fileName,
                retryState,
                ex.CanRetry,
                $"Print processing failed for '{fileName}': {ex.Message}",
                cancellationToken);
            if (!handled) {
                var errorPath = Path.Combine(_options.ErrorPath, fileName);
                SafeMoveToError(processingPath, errorPath);
                DeleteRetryState(processingPath);
                logger.LogError(ex, "RAW printing failed for '{FileName}'.", fileName);
                await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                    FileName = fileName,
                    Status = "error",
                    RequestedPrinter = requestedPrinter,
                    UsedPrinter = usedPrinter,
                    Message = ex.Message
                }, cancellationToken);
            }
            fileResult.Success = false;
        } catch (Exception ex) {
            var errorPath = Path.Combine(_options.ErrorPath, fileName);
            SafeMoveToError(processingPath, errorPath);
            DeleteRetryState(processingPath);
            logger.LogError(ex, "File '{FileName}' failed and was moved to error.", fileName);
            await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                FileName = fileName,
                Status = "error",
                RequestedPrinter = requestedPrinter,
                UsedPrinter = usedPrinter,
                Message = ex.Message
            }, cancellationToken);
            fileResult.Success = false;
        }

        return fileResult;
    }

    private async Task<bool> TryScheduleRetryAsync(
        string processingPath,
        string fileName,
        RetryState? retryState,
        bool canRetry,
        string logMessage,
        CancellationToken cancellationToken) {
        if (!canRetry) {
            return false;
        }

        retryState ??= new RetryState();
        var nextAttempt = retryState.Attempts + 1;
        if (nextAttempt > MaxRetryAttempts) {
            logger.LogWarning(
                "File '{FileName}' exhausted retry attempts ({MaxRetryAttempts}) and will move to error.",
                fileName,
                MaxRetryAttempts);
            return false;
        }

        var retryPath = Path.Combine(GetRetryPath(), fileName);
        File.Move(processingPath, retryPath, overwrite: true);
        await SaveRetryStateAsync(
            retryPath,
            new RetryState {
                Attempts = nextAttempt,
                LastError = logMessage,
                LastAttemptAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);

        logger.LogWarning(
            "{LogMessage}. Scheduled retry {Attempt}/{MaxRetryAttempts} for '{FileName}'.",
            logMessage,
            nextAttempt,
            MaxRetryAttempts,
            fileName);
        return true;
    }

    private async Task NotifyCallbackSafeAsync(PrintCallbackRequest callbackRequest, CancellationToken cancellationToken) {
        try {
            await callbackClient.SendAsync(callbackRequest, cancellationToken);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogError(
                ex,
                "Failed to send callback for '{FileName}' with status '{Status}'.",
                callbackRequest.FileName,
                callbackRequest.Status);
        }
    }

    private async Task WriteStatusSafeAsync(
        string state,
        string message,
        CancellationToken cancellationToken,
        int? processedCount = null,
        int? failedCount = null,
        int? remoteDownloaded = null,
        int? remoteSkipped = null,
        int? remoteFailed = null) {
        try {
            await statusWriter.WriteAsync(new AgentStatusSnapshot {
                State = state,
                Message = message,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                ProcessStartedAtUtc = _processStartedAtUtc,
                ProcessedCount = processedCount,
                FailedCount = failedCount,
                RemoteDownloaded = remoteDownloaded,
                RemoteSkipped = remoteSkipped,
                RemoteFailed = remoteFailed
            }, cancellationToken);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "Failed to persist agent status snapshot.");
        }
    }

    private bool AlreadyFinalized(string fileName) {
        var printedPath = Path.Combine(_options.PrintedPath, fileName);
        var errorPath = Path.Combine(_options.ErrorPath, fileName);
        return File.Exists(printedPath) || File.Exists(errorPath);
    }

    private string[] GetQueueFiles(string queuePath) {
        return Directory.GetFiles(queuePath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".retry.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private string GetRetryPath() {
        if (!string.IsNullOrWhiteSpace(_options.QueueRootPath)) {
            return Path.Combine(_options.QueueRootPath, "retry");
        }

        var rootPath = Path.GetDirectoryName(_options.ErrorPath);
        return string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(AppContext.BaseDirectory, "retry")
            : Path.Combine(rootPath, "retry");
    }

    private static string GetRetryMetadataPath(string filePath) {
        return $"{filePath}.retry.json";
    }

    private async Task<RetryState?> LoadRetryStateAsync(string filePath, CancellationToken cancellationToken) {
        var metadataPath = GetRetryMetadataPath(filePath);
        if (!File.Exists(metadataPath)) {
            return null;
        }

        await using var stream = File.OpenRead(metadataPath);
        return await JsonSerializer.DeserializeAsync<RetryState>(stream, cancellationToken: cancellationToken);
    }

    private async Task SaveRetryStateAsync(string filePath, RetryState state, CancellationToken cancellationToken) {
        var metadataPath = GetRetryMetadataPath(filePath);
        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, state, cancellationToken: cancellationToken);
    }

    private void MoveRetryState(string sourcePath, string destinationPath) {
        var sourceMetadataPath = GetRetryMetadataPath(sourcePath);
        if (!File.Exists(sourceMetadataPath)) {
            return;
        }

        var destinationMetadataPath = GetRetryMetadataPath(destinationPath);
        File.Move(sourceMetadataPath, destinationMetadataPath, overwrite: true);
    }

    private void DeleteRetryState(string filePath) {
        var metadataPath = GetRetryMetadataPath(filePath);
        if (File.Exists(metadataPath)) {
            File.Delete(metadataPath);
        }
    }

    private void TryMoveDuplicateToError(string sourcePath, string fileName, string reason) {
        try {
            if (!File.Exists(sourcePath)) {
                return;
            }

            var duplicateErrorPath = BuildUniqueErrorPath(fileName);
            File.Move(sourcePath, duplicateErrorPath, overwrite: false);
            logger.LogWarning(
                "Duplicate file '{FileName}' moved to error as '{DuplicateFileName}'. Reason: {Reason}",
                fileName,
                Path.GetFileName(duplicateErrorPath),
                reason);
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to move duplicate file '{FileName}' to error.", fileName);
        }
    }

    private void SafeMoveToError(string currentPath, string errorPath) {
        try {
            if (File.Exists(currentPath)) {
                File.Move(currentPath, errorPath, overwrite: true);
            }
        } catch (Exception moveEx) {
            logger.LogError(
                moveEx,
                "Failed to move '{CurrentPath}' to error location '{ErrorPath}'.",
                currentPath,
                errorPath);
        }
    }

    private string BuildUniqueErrorPath(string fileName) {
        var destinationPath = Path.Combine(_options.ErrorPath, fileName);
        if (!File.Exists(destinationPath)) {
            return destinationPath;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var uniqueFileName = $"{nameWithoutExtension}__duplicate-{stamp}{extension}";
        return Path.Combine(_options.ErrorPath, uniqueFileName);
    }

    private static string? TryExtractRequestedPrinter(string fileName) {
        const string marker = "__printer=";
        var markerIndex = fileName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) {
            return null;
        }

        var printerStart = markerIndex + marker.Length;
        var extensionIndex = fileName.LastIndexOf('.');
        if (extensionIndex <= printerStart) {
            return null;
        }

        var extracted = fileName[printerStart..extensionIndex].Trim();
        return string.IsNullOrWhiteSpace(extracted) ? null : extracted;
    }

    private sealed class BatchResult {
        public int ProcessedCount { get; set; }
        public int FailedCount { get; set; }
    }

    private sealed class FileResult {
        public bool Success { get; set; }
    }

    private sealed class RetryState {
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset LastAttemptAtUtc { get; set; }
    }
}
