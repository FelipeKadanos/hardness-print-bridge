using Hardness.PrintBridge.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace Hardness.PrintBridge.Agent;

public class Worker(
    ILogger<Worker> logger,
    IOptions<PrintBridgeOptions> options) : BackgroundService {
    private readonly PrintBridgeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        EnsureDirectories();

        logger.LogInformation(
            "Queue worker started. Watching '{WatchPath}' every {PollIntervalMs}ms.",
            _options.WatchPath,
            _options.PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                ProcessInboxBatch(stoppingToken);
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

    private void ProcessInboxBatch(CancellationToken stoppingToken) {
        var files = Directory.GetFiles(_options.WatchPath, "*.etq", SearchOption.TopDirectoryOnly);

        if (files.Length == 0) {
            return;
        }

        logger.LogInformation("Found {FileCount} file(s) in inbox.", files.Length);

        foreach (var sourcePath in files) {
            stoppingToken.ThrowIfCancellationRequested();
            ProcessSingleFile(sourcePath);
        }
    }

    private void ProcessSingleFile(string sourcePath) {
        var fileName = Path.GetFileName(sourcePath);
        var processingPath = Path.Combine(_options.ProcessingPath, fileName);

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
            var content = File.ReadAllText(processingPath).Trim();
            if (string.IsNullOrWhiteSpace(content)) {
                throw new InvalidDataException("ETQ payload is empty.");
            }

            var printedPath = Path.Combine(_options.PrintedPath, fileName);
            File.Move(processingPath, printedPath, overwrite: false);
            logger.LogInformation("File '{FileName}' processed and moved to printed.", fileName);
        } catch (Exception ex) {
            var errorPath = Path.Combine(_options.ErrorPath, fileName);
            SafeMoveToError(processingPath, errorPath);
            logger.LogError(ex, "File '{FileName}' failed and was moved to error.", fileName);
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
