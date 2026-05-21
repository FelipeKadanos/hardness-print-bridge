using Hardness.PrintBridge.Agent.Application;
using Hardness.PrintBridge.Agent.Configuration;
using Hardness.PrintBridge.Agent.Infrastructure.Printing;
using Microsoft.Extensions.Options;

namespace Hardness.PrintBridge.Agent;

public class Worker(
    ILogger<Worker> logger,
    IOptions<PrintBridgeOptions> options,
    IPrintJobParser printJobParser,
    IPrinterResolver printerResolver,
    IRawPrinterClient rawPrinterClient,
    IHardnessCallbackClient callbackClient) : BackgroundService {
    private readonly PrintBridgeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        EnsureDirectories();

        logger.LogInformation(
            "Queue worker started. Watching '{WatchPath}' every {PollIntervalMs}ms.",
            _options.WatchPath,
            _options.PollIntervalMs);

        await RecoverProcessingQueueAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested) {
            var cycleStartedAt = DateTimeOffset.Now;
            var processedCount = 0;
            var failedCount = 0;

            try {
                var processingResult = await ProcessProcessingBatchAsync(stoppingToken);
                processedCount += processingResult.ProcessedCount;
                failedCount += processingResult.FailedCount;

                var inboxResult = await ProcessInboxBatchAsync(stoppingToken);
                processedCount += inboxResult.ProcessedCount;
                failedCount += inboxResult.FailedCount;
            } catch (Exception ex) {
                failedCount++;
                logger.LogError(ex, "Unexpected error while processing queue batch.");
            }

            logger.LogInformation(
                "Queue cycle finished. StartedAt={StartedAt}, Processed={ProcessedCount}, Failed={FailedCount}.",
                cycleStartedAt,
                processedCount,
                failedCount);

            await Task.Delay(_options.PollIntervalMs, stoppingToken);
        }
    }

    private void EnsureDirectories() {
        Directory.CreateDirectory(_options.WatchPath);
        Directory.CreateDirectory(_options.ProcessingPath);
        Directory.CreateDirectory(_options.PrintedPath);
        Directory.CreateDirectory(_options.ErrorPath);
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
        var files = Directory.GetFiles(_options.WatchPath, "*.etq", SearchOption.TopDirectoryOnly);
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
        var files = Directory.GetFiles(_options.ProcessingPath, "*.etq", SearchOption.TopDirectoryOnly);
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

        // Idempotence by filename: if already finalized, don't process again.
        if (AlreadyFinalized(fileName)) {
            logger.LogWarning(
                "Skipping '{FileName}' because it already exists in printed or error.",
                fileName);
            TryMoveDuplicateToError(sourcePath, fileName, "Duplicate filename already finalized.");
            fileResult.Success = false;
            return fileResult;
        }

        if (!sourcePathIsProcessingPath) {
            try {
                // Atomic move inbox -> processing as queue lock.
                File.Move(sourcePath, processingPath, overwrite: false);
                logger.LogInformation("Moved '{FileName}' to processing.", fileName);
            } catch (IOException ioEx) {
                logger.LogWarning(ioEx, "Could not move '{FileName}' to processing. It may be in use.", fileName);
                fileResult.Success = false;
                return fileResult;
            }
        }

        try {
            var printJob = printJobParser.ParseEtq(processingPath);
            requestedPrinter = printJob.RequestedPrinter ?? requestedPrinter;
            var resolvedPrinter = printerResolver.Resolve(printJob);
            usedPrinter = resolvedPrinter;
            rawPrinterClient.Print(resolvedPrinter, printJob.RawPayload, printJob.FileName);

            logger.LogInformation(
                "Printed '{FileName}' successfully. Requested printer: '{RequestedPrinter}', used printer: '{UsedPrinter}', payload bytes: {PayloadLength}.",
                printJob.FileName,
                printJob.RequestedPrinter ?? "(default)",
                resolvedPrinter,
                printJob.RawPayload.Length);

            var printedPath = Path.Combine(_options.PrintedPath, fileName);
            File.Move(processingPath, printedPath, overwrite: false);
            logger.LogInformation("File '{FileName}' processed and moved to printed.", fileName);

            await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                FileName = printJob.FileName,
                Status = "success",
                RequestedPrinter = requestedPrinter,
                UsedPrinter = resolvedPrinter,
                ErrorMessage = null
            }, cancellationToken);
            fileResult.Success = true;
        } catch (PrinterResolutionException ex) {
            var errorPath = Path.Combine(_options.ErrorPath, fileName);
            SafeMoveToError(processingPath, errorPath);
            logger.LogError(ex, "Printer resolution failed for '{FileName}'.", fileName);
            await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                FileName = fileName,
                Status = "error",
                RequestedPrinter = requestedPrinter,
                UsedPrinter = usedPrinter,
                ErrorMessage = ex.Message
            }, cancellationToken);
            fileResult.Success = false;
        } catch (InvalidDataException ex) {
            var errorPath = Path.Combine(_options.ErrorPath, fileName);
            SafeMoveToError(processingPath, errorPath);
            logger.LogError(ex, "Invalid ETQ payload for '{FileName}'.", fileName);
            await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                FileName = fileName,
                Status = "error",
                RequestedPrinter = requestedPrinter,
                UsedPrinter = usedPrinter,
                ErrorMessage = ex.Message
            }, cancellationToken);
            fileResult.Success = false;
        } catch (PrintJobProcessingException ex) {
            var errorPath = Path.Combine(_options.ErrorPath, fileName);
            SafeMoveToError(processingPath, errorPath);
            logger.LogError(ex, "RAW printing failed for '{FileName}'.", fileName);
            await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                FileName = fileName,
                Status = "error",
                RequestedPrinter = requestedPrinter,
                UsedPrinter = usedPrinter,
                ErrorMessage = ex.Message
            }, cancellationToken);
            fileResult.Success = false;
        } catch (Exception ex) {
            var errorPath = Path.Combine(_options.ErrorPath, fileName);
            SafeMoveToError(processingPath, errorPath);
            logger.LogError(ex, "File '{FileName}' failed and was moved to error.", fileName);
            await NotifyCallbackSafeAsync(new PrintCallbackRequest {
                FileName = fileName,
                Status = "error",
                RequestedPrinter = requestedPrinter,
                UsedPrinter = usedPrinter,
                ErrorMessage = ex.Message
            }, cancellationToken);
            fileResult.Success = false;
        }

        return fileResult;
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

    private bool AlreadyFinalized(string fileName) {
        var printedPath = Path.Combine(_options.PrintedPath, fileName);
        var errorPath = Path.Combine(_options.ErrorPath, fileName);
        return File.Exists(printedPath) || File.Exists(errorPath);
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
}
