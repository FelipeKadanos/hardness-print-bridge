using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.Status;

public interface IAgentStatusSource {
    Task<AgentStatusSnapshot?> GetCurrentAsync(CancellationToken cancellationToken);
}
