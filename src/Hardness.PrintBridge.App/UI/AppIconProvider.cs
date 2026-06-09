using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.UI;

internal static class AppIconProvider {
    private static Icon? _baseAppIcon;

    public static Icon GetAppIcon() {
        _baseAppIcon ??= LoadBaseIcon();
        return (Icon)_baseAppIcon.Clone();
    }

    public static Icon GetStatusIcon(string state) {
        return state switch {
            AgentState.Warning => (Icon)SystemIcons.Warning.Clone(),
            AgentState.Error => (Icon)SystemIcons.Error.Clone(),
            _ => GetAppIcon()
        };
    }

    private static Icon LoadBaseIcon() {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "HPB.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath)
            : (Icon)SystemIcons.Application.Clone();
    }
}
