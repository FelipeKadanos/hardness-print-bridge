namespace Hardness.PrintBridge.Agent.Configuration;

internal static class PrintBridgeUrlResolver {
    public static string Resolve(string template, string? apiAuthToken) {
        if (string.IsNullOrWhiteSpace(template)) {
            return template;
        }

        if (string.IsNullOrWhiteSpace(apiAuthToken)) {
            return template;
        }

        return template.Replace(
            "REPLACE_ME",
            Uri.EscapeDataString(apiAuthToken),
            StringComparison.Ordinal);
    }
}
