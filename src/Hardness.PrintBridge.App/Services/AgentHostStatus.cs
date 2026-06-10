namespace Hardness.PrintBridge.App.Services;

public sealed record AgentHostStatus(
    string State,
    string Message,
    bool IsServiceMode,
    int? ProcessId = null,
    DateTimeOffset? ProcessStartedAtUtc = null);
