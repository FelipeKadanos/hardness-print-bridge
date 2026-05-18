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

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await ProcessInboxBatchAsync(stoppingToken);
            } catch (Exception ex) {
                logger.LogError(ex, "Unexpected error while processing queue batch.");
            }

            await Task.Delay(_options.PollIntervalMs, stoppingToken);
        }
    }

    private void EnsureDirectories() {
        Directory.CreateDirectory(_options.WatchPath);
        Directory.CreateDirectory(_options.ProcessingPath);
        Directory.CreateDirectory(_options.PrintedPath);
        Directory.CreateDirectory(_options.ErrorPath);
    }

    private async Task ProcessInboxBatchAsync(CancellationToken stoppingToken) {
        var files = Directory.GetFiles(_options.WatchPath, "*.etq", SearchOption.TopDirectoryOnly);

        if (files.Length == 0) {
            return;
        }

        logger.LogInformation("Found {FileCount} file(s) in inbox.", files.Length);

        foreach (var sourcePath in files) {
            stoppingToken.ThrowIfCancellationRequested();
            await ProcessSingleFileAsync(sourcePath, stoppingToken);
        }
    }

    private async Task ProcessSingleFileAsync(string sourcePath, CancellationToken cancellationToken) {
        var fileName = Path.GetFileName(sourcePath);
        var processingPath = Path.Combine(_options.ProcessingPath, fileName);
        string? requestedPrinter = null;
        string? usedPrinter = null;

        // Idempotence by filename: if already finalized, don't process again.
        if (AlreadyFinalized(fileName)) {
            logger.LogWarning(
                "Skipping '{FileName}' because it already exists in printed or error.",
                fileName);
            TryMoveDuplicateToError(sourcePath, fileName, "Duplicate filename already finalized.");
            return;
        }

        try {
            // Atomic move inbox -> processing as queue lock.
            File.Move(sourcePath, processingPath, overwrite: false);
            logger.LogInformation("Moved '{FileName}' to processing.", fileName);
        } catch (IOException ioEx) {
            logger.LogWarning(ioEx, "Could not move '{FileName}' to processing. It may be in use.", fileName);
            return;
        }

        try {
            var printJob = printJobParser.ParseEtq(processingPath);
            requestedPrinter = printJob.RequestedPrinter;
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
        }
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
        var duplicateErrorPath = Path.Combine(_options.ErrorPath, fileName);
        try {
            if (!File.Exists(sourcePath)) {
                return;
            }

            File.Move(sourcePath, duplicateErrorPath, overwrite: false);
            logger.LogWarning("Duplicate file '{FileName}' moved to error. Reason: {Reason}", fileName, reason);
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
}
