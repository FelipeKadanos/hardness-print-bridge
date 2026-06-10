namespace Hardness.PrintBridge.App.Services;

public interface IAgentLogSource {
    Task<AgentLogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
