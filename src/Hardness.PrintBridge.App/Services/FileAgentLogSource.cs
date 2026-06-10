using System.Text;
using Hardness.PrintBridge.Contracts.Runtime;
namespace Hardness.PrintBridge.App.Services;

public sealed class FileAgentLogSource : IAgentLogSource {
    private const int MaxBytesToRead = 256 * 1024;

    public Task<AgentLogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var logFilePath = RuntimePaths.GetAgentLogPath();
        if (!File.Exists(logFilePath)) {
            return Task.FromResult(new AgentLogSnapshot(logFilePath, "Nenhum arquivo de log do Agent foi encontrado ainda."));
        }

        using var stream = new FileStream(
            logFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (stream.Length == 0) {
            return Task.FromResult(new AgentLogSnapshot(logFilePath, string.Empty));
        }

        if (stream.Length > MaxBytesToRead) {
            stream.Seek(-MaxBytesToRead, SeekOrigin.End);
            SkipPartialLine(stream);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return Task.FromResult(new AgentLogSnapshot(logFilePath, content));
    }

    private static void SkipPartialLine(Stream stream) {
        while (stream.Position < stream.Length) {
            var nextByte = stream.ReadByte();
            if (nextByte == -1 || nextByte == '\n') {
                break;
            }
        }
    }
}
