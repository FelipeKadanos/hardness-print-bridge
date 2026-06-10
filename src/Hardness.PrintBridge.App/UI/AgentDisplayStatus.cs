namespace Hardness.PrintBridge.App.UI;

public sealed record AgentDisplayStatus(
    string State,
    string Message,
    DateTimeOffset? UpdatedAtUtc);
