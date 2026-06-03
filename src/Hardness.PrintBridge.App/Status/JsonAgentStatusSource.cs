using System.Text.Json;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.Status;

public sealed class JsonAgentStatusSource : IAgentStatusSource {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AgentStatusSnapshot?> GetCurrentAsync(CancellationToken cancellationToken) {
        var statusPath = RuntimePaths.GetAgentStatusPath();
        if (!File.Exists(statusPath)) {
            return null;
        }

        try {
            await using var stream = File.OpenRead(statusPath);
            return await JsonSerializer.DeserializeAsync<AgentStatusSnapshot>(stream, JsonOptions, cancellationToken);
        } catch (IOException) {
            return null;
        } catch (JsonException) {
            return null;
        }
    }
}
