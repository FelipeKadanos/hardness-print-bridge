using System.Diagnostics;

namespace Hardness.PrintBridge.App.Services;

public sealed class AgentControlService : IAgentControlService {
    private const string ServiceName = "HardnessPrintBridgeAgent";

    public async Task RestartAsync(CancellationToken cancellationToken) {
        if (await ServiceExistsAsync(cancellationToken)) {
            await RunScCommandAsync($"stop {ServiceName}", cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            await RunScCommandAsync($"start {ServiceName}", cancellationToken);
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

    private static async Task RunScCommandAsync(string arguments, CancellationToken cancellationToken) {
        var result = await RunProcessAsync("sc.exe", arguments, cancellationToken);
        if (result.ExitCode != 0) {
            throw new InvalidOperationException(result.ErrorOutput);
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

    private static string? TryResolveAgentExecutablePath() {
        var candidates = new[] {
            Path.Combine(AppContext.BaseDirectory, "Hardness.PrintBridge.Agent.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Hardness.PrintBridge.Agent\bin\Debug\net10.0-windows\Hardness.PrintBridge.Agent.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Hardness.PrintBridge.Agent\bin\Release\net10.0-windows\Hardness.PrintBridge.Agent.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string ErrorOutput);
}
