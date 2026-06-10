using System.Diagnostics;
using System.Text.Json;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.Services;

public sealed class AgentControlService : IAgentControlService {
    private const string ServiceName = "HardnessPrintBridgeAgent";
    private static readonly TimeSpan ServiceTransitionTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ServicePollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private int? _lastLaunchedProcessId;
    private DateTimeOffset? _lastLaunchedProcessStartedAtUtc;

    public async Task EnsureRunningAsync(CancellationToken cancellationToken) {
        if (await ServiceExistsAsync(cancellationToken)) {
            var serviceState = await QueryServiceStateAsync(cancellationToken);
            if (serviceState == ServiceState.Running || serviceState == ServiceState.StartPending) {
                return;
            }

            await StartServiceAndWaitAsync(cancellationToken);
            return;
        }

        if (TryGetLiveAgentProcess() is not null) {
            return;
        }

        LaunchAgentProcess();
    }

    public async Task<AgentHostStatus> GetCurrentStatusAsync(CancellationToken cancellationToken) {
        if (await ServiceExistsAsync(cancellationToken)) {
            var serviceState = await QueryServiceStateAsync(cancellationToken);
            return serviceState switch {
                ServiceState.Running => new AgentHostStatus(
                    AgentState.Running,
                    "Serviço do Agent em execução.",
                    IsServiceMode: true),
                ServiceState.StartPending => new AgentHostStatus(
                    AgentState.Starting,
                    "Serviço do Agent iniciando.",
                    IsServiceMode: true),
                ServiceState.StopPending => new AgentHostStatus(
                    AgentState.Warning,
                    "Serviço do Agent em transição de parada.",
                    IsServiceMode: true),
                _ => new AgentHostStatus(
                    AgentState.Stopped,
                    "Serviço do Agent parado.",
                    IsServiceMode: true)
            };
        }

        var liveProcess = TryGetLiveAgentProcess();
        if (liveProcess is not null) {
            return new AgentHostStatus(
                AgentState.Running,
                "Processo do Agent em execução.",
                IsServiceMode: false,
                liveProcess.ProcessId,
                liveProcess.ProcessStartedAtUtc);
        }

        if (TryGetTrackedLaunchedProcess() is { } launchedProcess) {
            return new AgentHostStatus(
                AgentState.Starting,
                "Processo do Agent iniciado; aguardando publicação de status.",
                IsServiceMode: false,
                launchedProcess.ProcessId,
                launchedProcess.ProcessStartedAtUtc);
        }

        return new AgentHostStatus(
            AgentState.Stopped,
            "Agent não está em execução.",
            IsServiceMode: false);
    }

    public async Task RestartAsync(CancellationToken cancellationToken) {
        if (await ServiceExistsAsync(cancellationToken)) {
            await StopServiceIfNeededAsync(cancellationToken);
            await StartServiceAndWaitAsync(cancellationToken);
            return;
        }

        await StopLiveProcessIfNeededAsync(cancellationToken);
        LaunchAgentProcess();
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

    private void LaunchAgentProcess() {
        var executablePath = TryResolveAgentExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath)) {
            throw new FileNotFoundException("Agent executable not found for startup.");
        }

        var process = Process.Start(new ProcessStartInfo {
            FileName = executablePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
        });

        if (process is not null) {
            _lastLaunchedProcessId = process.Id;
            try {
                _lastLaunchedProcessStartedAtUtc = process.StartTime.ToUniversalTime();
            } catch {
                _lastLaunchedProcessStartedAtUtc = DateTimeOffset.UtcNow;
            }
        } else {
            _lastLaunchedProcessId = null;
            _lastLaunchedProcessStartedAtUtc = null;
        }
    }

    private async Task StopLiveProcessIfNeededAsync(CancellationToken cancellationToken) {
        var liveProcess = TryGetLiveAgentProcess();
        if (liveProcess is null) {
            return;
        }

        try {
            using var process = Process.GetProcessById(liveProcess.ProcessId);
            if (process.HasExited) {
                return;
            }

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
        } catch (ArgumentException) {
        }
    }

    private LiveAgentProcess? TryGetTrackedLaunchedProcess() {
        if (_lastLaunchedProcessId is null || _lastLaunchedProcessStartedAtUtc is null) {
            return null;
        }

        return TryResolveLiveProcess(_lastLaunchedProcessId.Value, _lastLaunchedProcessStartedAtUtc.Value)
            ? new LiveAgentProcess(_lastLaunchedProcessId.Value, _lastLaunchedProcessStartedAtUtc.Value)
            : null;
    }

    private static LiveAgentProcess? TryGetLiveAgentProcess() {
        var snapshot = TryReadSnapshot();
        if (snapshot?.ProcessId is null || snapshot.ProcessStartedAtUtc is null) {
            return null;
        }

        return TryResolveLiveProcess(snapshot.ProcessId.Value, snapshot.ProcessStartedAtUtc.Value)
            ? new LiveAgentProcess(snapshot.ProcessId.Value, snapshot.ProcessStartedAtUtc.Value)
            : null;
    }

    private static bool TryResolveLiveProcess(int processId, DateTimeOffset startedAtUtc) {
        try {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) {
                return false;
            }

            var actualStart = process.StartTime.ToUniversalTime();
            var difference = (actualStart - startedAtUtc.UtcDateTime).Duration();
            return difference <= TimeSpan.FromSeconds(2);
        } catch {
            return false;
        }
    }

    private static AgentStatusSnapshot? TryReadSnapshot() {
        var path = RuntimePaths.GetAgentStatusPath();
        if (!File.Exists(path)) {
            return null;
        }

        try {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AgentStatusSnapshot>(json, JsonOptions);
        } catch {
            return null;
        }
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
            || result.StandardOutput.Contains("O serviÃ§o nÃ£o foi iniciado.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsServiceAlreadyRunning(ProcessResult result) {
        return ContainsErrorCode(result, 1056)
            || result.StandardOutput.Contains("Uma instÃ¢ncia do serviÃ§o jÃ¡ estÃ¡ sendo executada.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsErrorCode(ProcessResult result, int code) {
        var codeText = code.ToString();
        return result.StandardOutput.Contains(codeText, StringComparison.OrdinalIgnoreCase)
            || result.ErrorOutput.Contains(codeText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string ErrorOutput);
    private sealed record LiveAgentProcess(int ProcessId, DateTimeOffset ProcessStartedAtUtc);

    private enum ServiceState {
        Stopped,
        StartPending,
        StopPending,
        Running
    }
}
