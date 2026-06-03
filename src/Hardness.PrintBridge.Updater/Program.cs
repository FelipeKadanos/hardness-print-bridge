using System.Diagnostics;
using System.IO.Compression;
using Hardness.PrintBridge.Contracts.Runtime;

var arguments = UpdaterArguments.Parse(args);
var logger = new UpdaterLogger();
var workspacePath = RuntimePaths.GetUpdateWorkspacePath();
var backupPath = Path.Combine(workspacePath, $"backup-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
var extractPath = Path.Combine(workspacePath, $"extract-{Guid.NewGuid():N}");

try {
    logger.Info("Updater starting.");
    await WaitForProcessExitAsync(arguments.ProcessId, logger);
    await StopServiceIfInstalledAsync(arguments.ServiceName, logger);

    Directory.CreateDirectory(workspacePath);

    logger.Info($"Creating backup at '{backupPath}'.");
    CopyDirectory(arguments.TargetDirectory, backupPath);

    logger.Info($"Extracting package '{arguments.PackagePath}' to '{extractPath}'.");
    ZipFile.ExtractToDirectory(arguments.PackagePath, extractPath, overwriteFiles: true);

    logger.Info($"Applying update to '{arguments.TargetDirectory}'.");
    CopyDirectory(extractPath, arguments.TargetDirectory, overwrite: true);

    await StartServiceIfInstalledAsync(arguments.ServiceName, logger);
    RestartApplication(arguments.RestartExecutablePath, logger);
    logger.Info("Updater finished successfully.");
    return 0;
} catch (Exception ex) {
    logger.Error("Updater failed.", ex);
    if (Directory.Exists(backupPath)) {
        try {
            logger.Info("Restoring backup after failure.");
            CopyDirectory(backupPath, arguments.TargetDirectory, overwrite: true);
            await StartServiceIfInstalledAsync(arguments.ServiceName, logger);
            RestartApplication(arguments.RestartExecutablePath, logger);
            logger.Info("Backup restored successfully.");
        } catch (Exception rollbackEx) {
            logger.Error("Rollback failed.", rollbackEx);
        }
    }
    return 1;
}

static async Task WaitForProcessExitAsync(int? processId, UpdaterLogger logger) {
    if (processId is null or <= 0) {
        return;
    }

    try {
        var process = Process.GetProcessById(processId.Value);
        logger.Info($"Waiting for process {processId.Value} to exit.");
        await process.WaitForExitAsync();
    } catch (ArgumentException) {
        logger.Info($"Process {processId.Value} already exited.");
    }
}

static async Task StopServiceIfInstalledAsync(string? serviceName, UpdaterLogger logger) {
    if (string.IsNullOrWhiteSpace(serviceName)) {
        return;
    }

    if (!await ServiceExistsAsync(serviceName)) {
        return;
    }

    logger.Info($"Stopping service '{serviceName}'.");
    await RunScAsync($"stop {serviceName}");
    await Task.Delay(TimeSpan.FromSeconds(2));
}

static async Task StartServiceIfInstalledAsync(string? serviceName, UpdaterLogger logger) {
    if (string.IsNullOrWhiteSpace(serviceName)) {
        return;
    }

    if (!await ServiceExistsAsync(serviceName)) {
        return;
    }

    logger.Info($"Starting service '{serviceName}'.");
    await RunScAsync($"start {serviceName}");
}

static async Task<bool> ServiceExistsAsync(string serviceName) {
    var result = await RunProcessAsync("sc.exe", $"query {serviceName}");
    return result.ExitCode == 0;
}

static async Task RunScAsync(string arguments) {
    var result = await RunProcessAsync("sc.exe", arguments);
    if (result.ExitCode != 0) {
        throw new InvalidOperationException(result.ErrorOutput);
    }
}

static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments) {
    using var process = new Process {
        StartInfo = new ProcessStartInfo {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }
    };

    process.Start();
    var standardOutput = await process.StandardOutput.ReadToEndAsync();
    var standardError = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return new ProcessResult(process.ExitCode, standardOutput, standardError);
}

static void RestartApplication(string? executablePath, UpdaterLogger logger) {
    if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) {
        logger.Info("Restart executable path not provided or not found. Skipping restart.");
        return;
    }

    logger.Info($"Restarting application '{executablePath}'.");
    Process.Start(new ProcessStartInfo {
        FileName = executablePath,
        UseShellExecute = true,
        WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
    });
}

static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite = false) {
    Directory.CreateDirectory(destinationDirectory);

    foreach (var directoryPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories)) {
        Directory.CreateDirectory(directoryPath.Replace(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase));
    }

    foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)) {
        var destinationPath = filePath.Replace(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(filePath, destinationPath, overwrite);
    }
}

sealed record ProcessResult(int ExitCode, string StandardOutput, string ErrorOutput);

sealed class UpdaterArguments {
    public required string PackagePath { get; init; }
    public required string TargetDirectory { get; init; }
    public string? RestartExecutablePath { get; init; }
    public string? ServiceName { get; init; }
    public int? ProcessId { get; init; }

    public static UpdaterArguments Parse(string[] args) {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2) {
            if (index + 1 >= args.Length) {
                break;
            }

            values[args[index]] = args[index + 1];
        }

        if (!values.TryGetValue("--package", out var packagePath) || string.IsNullOrWhiteSpace(packagePath)) {
            throw new ArgumentException("Missing required argument --package.");
        }

        if (!values.TryGetValue("--target", out var targetDirectory) || string.IsNullOrWhiteSpace(targetDirectory)) {
            throw new ArgumentException("Missing required argument --target.");
        }

        values.TryGetValue("--restart", out var restartExecutablePath);
        values.TryGetValue("--service", out var serviceName);
        values.TryGetValue("--process-id", out var processIdRaw);

        return new UpdaterArguments {
            PackagePath = packagePath,
            TargetDirectory = targetDirectory,
            RestartExecutablePath = restartExecutablePath,
            ServiceName = serviceName,
            ProcessId = int.TryParse(processIdRaw, out var processId) ? processId : null
        };
    }
}

sealed class UpdaterLogger {
    private readonly string _logPath;

    public UpdaterLogger() {
        var workspacePath = RuntimePaths.GetUpdateWorkspacePath();
        Directory.CreateDirectory(workspacePath);
        _logPath = Path.Combine(workspacePath, $"updater-{DateTimeOffset.Now:yyyyMMdd}.log");
    }

    public void Info(string message) {
        Write("INF", message);
    }

    public void Error(string message, Exception exception) {
        Write("ERR", $"{message} {exception}");
    }

    private void Write(string level, string message) {
        File.AppendAllText(
            _logPath,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message}{Environment.NewLine}");
    }
}
