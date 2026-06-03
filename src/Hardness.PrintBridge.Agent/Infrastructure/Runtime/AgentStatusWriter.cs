using System.Text.Json;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.Agent.Infrastructure.Runtime;

public sealed class AgentStatusWriter {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task WriteAsync(AgentStatusSnapshot snapshot, CancellationToken cancellationToken) {
        await _writeLock.WaitAsync(cancellationToken);
        try {
            var statusPath = RuntimePaths.GetAgentStatusPath();
            var directoryPath = Path.GetDirectoryName(statusPath);
            if (!string.IsNullOrWhiteSpace(directoryPath)) {
                Directory.CreateDirectory(directoryPath);
            }

            var tempPath = $"{statusPath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(snapshot, JsonOptions),
                cancellationToken);

            File.Move(tempPath, statusPath, overwrite: true);
        } finally {
            _writeLock.Release();
        }
    }
}
