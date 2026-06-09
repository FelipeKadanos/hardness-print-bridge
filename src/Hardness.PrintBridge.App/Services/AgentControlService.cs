using System.Diagnostics;

namespace Hardness.PrintBridge.App.Services;

public sealed class AgentControlService : IAgentControlService {
    private const string ServiceName = "HardnessPrintBridgeAgent";
    private static readonly TimeSpan ServiceTransitionTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ServicePollInterval = TimeSpan.FromMilliseconds(500);

    public async Task RestartAsync(CancellationToken cancellationToken) {
        if (await ServiceExistsAsync(cancellationToken)) {
            await StopServiceIfNeededAsync(cancellationToken);
            await StartServiceAndWaitAsync(cancellationToken);
            return;
        }

        var executablePath = TryResolveAgentExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath)) {
            throw new FileNotFoundException("Agent executable not found for restart.");
        }

        Process.Start(new ProcessStartInfo {
            FileName = executablePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
        });
    }

    private static async Task<bool> ServiceExistsAsync(CancellationToken cancellationToken) {
        var result = await RunProcessAsync("sc.exe", $"query {ServiceName}", cancellationToken);
        return result.ExitCode == 0;
    }

    private static async Task StopServiceIfNeededAsync(CancellationToken cancellationToken) {
        var stopResult = await RunProcessAsync("sc.exe", $"stop {ServiceName}", cancellationToken);
        if (stopResult.ExitCode != 0 && !IsServiceAlreadyStopped(stopResult)) {
            throw new InvalidOperationException(BuildProcessFailureMessage("sc.exe", $"stop {ServiceName}", stopResult));
        }

        await WaitForServiceStateAsync(ServiceState.Stopped, cancellationToken);
    }

    private static async Task StartServiceAndWaitAsync(CancellationToken cancellationToken) {
        var startResult = await RunProcessAsync("sc.exe", $"start {ServiceName}", cancellationToken);
        if (startResult.ExitCode != 0 && !IsServiceAlreadyRunning(startResult)) {
            throw new InvalidOperationException(BuildProcessFailureMessage("sc.exe", $"start {ServiceName}", startResult));
        }

        await WaitForServiceStateAsync(ServiceState.Running, cancellationToken);
    }

    private static async Task RunScCommandAsync(string arguments, CancellationToken cancellationToken) {
        var result = await RunProcessAsync("sc.exe", arguments, cancellationToken);
        if (result.ExitCode != 0) {
            throw new InvalidOperationException(BuildProcessFailureMessage("sc.exe", arguments, result));
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken) {
        using var process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };

        process.Start();
        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static async Task WaitForServiceStateAsync(ServiceState desiredState, CancellationToken cancellationToken) {
        var deadline = DateTimeOffset.UtcNow.Add(ServiceTransitionTimeout);

        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            var currentState = await QueryServiceStateAsync(cancellationToken);
            if (currentState == desiredState) {
                return;
            }

            await Task.Delay(ServicePollInterval, cancellationToken);
        }

        throw new TimeoutException($"Service '{ServiceName}' did not reach state '{desiredState}' within {ServiceTransitionTimeout.TotalSeconds:0} seconds.");
    }

    private static async Task<ServiceState> QueryServiceStateAsync(CancellationToken cancellationToken) {
        var result = await RunProcessAsync("sc.exe", $"query {ServiceName}", cancellationToken);
        if (result.ExitCode != 0) {
            throw new InvalidOperationException(BuildProcessFailureMessage("sc.exe", $"query {ServiceName}", result));
        }

        var output = result.StandardOutput ?? string.Empty;
        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)) {
            var trimmedLine = line.Trim();
            if (!trimmedLine.StartsWith("STATE", StringComparison.OrdinalIgnoreCase)
                && !trimmedLine.StartsWith("ESTADO", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var stateCode = ExtractServiceStateCode(trimmedLine);
            if (stateCode is not null) {
                return stateCode.Value switch {
                    1 => ServiceState.Stopped,
                    2 => ServiceState.StartPending,
                    3 => ServiceState.StopPending,
                    4 => ServiceState.Running,
                    _ => throw new InvalidOperationException($"Unsupported service state code '{stateCode.Value}' in output:{Environment.NewLine}{output}")
                };
            }
        }

        throw new InvalidOperationException($"Unable to determine service state from output:{Environment.NewLine}{output}");
    }

    private static int? ExtractServiceStateCode(string serviceStateLine) {
        var colonIndex = serviceStateLine.IndexOf(':');
        if (colonIndex < 0) {
            return null;
        }

        var suffix = serviceStateLine[(colonIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(suffix)) {
            return null;
        }

        var firstToken = suffix
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return int.TryParse(firstToken, out var parsed) ? parsed : null;
    }

    private static string? TryResolveAgentExecutablePath() {
        var candidates = new[] {
            Path.Combine(AppContext.BaseDirectory, "Hardness.PrintBridge.Agent.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Hardness.PrintBridge.Agent\bin\Debug\net10.0-windows\Hardness.PrintBridge.Agent.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Hardness.PrintBridge.Agent\bin\Release\net10.0-windows\Hardness.PrintBridge.Agent.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string BuildProcessFailureMessage(string fileName, string arguments, ProcessResult result) {
        var details = string.IsNullOrWhiteSpace(result.ErrorOutput)
            ? result.StandardOutput?.Trim()
            : result.ErrorOutput.Trim();

        if (string.IsNullOrWhiteSpace(details)) {
            details = $"O comando '{fileName} {arguments}' falhou com codigo de saida {result.ExitCode}.";
        }

        return details;
    }

    private static bool IsServiceAlreadyStopped(ProcessResult result) {
        return ContainsErrorCode(result, 1062)
            || result.StandardOutput.Contains("O serviço não foi iniciado.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsServiceAlreadyRunning(ProcessResult result) {
        return ContainsErrorCode(result, 1056)
            || result.StandardOutput.Contains("Uma instância do serviço já está sendo executada.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsErrorCode(ProcessResult result, int code) {
        var codeText = code.ToString();
        return result.StandardOutput.Contains(codeText, StringComparison.OrdinalIgnoreCase)
            || result.ErrorOutput.Contains(codeText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string ErrorOutput);

    private enum ServiceState {
        Stopped,
        StartPending,
        StopPending,
        Running
    }
}
