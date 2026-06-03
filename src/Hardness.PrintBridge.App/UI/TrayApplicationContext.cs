using Hardness.PrintBridge.App.Services;
using Hardness.PrintBridge.App.Status;
using Hardness.PrintBridge.App.Update;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.UI;

public sealed class TrayApplicationContext : ApplicationContext {
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleStatusThreshold = TimeSpan.FromSeconds(30);

    private readonly IAppSettingsStore _appSettingsStore;
    private readonly IStartupService _startupService;
    private readonly IAgentStatusSource _agentStatusSource;
    private readonly IAgentControlService _agentControlService;
    private readonly IUpdateService _updateService;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private readonly MainForm _mainForm;
    private AppSettings _settings = new();
    private DateTimeOffset _lastUpdateCheckAtUtc = DateTimeOffset.MinValue;

    public TrayApplicationContext(
        IAppSettingsStore appSettingsStore,
        IStartupService startupService,
        IAgentStatusSource agentStatusSource,
        IAgentControlService agentControlService,
        IUpdateService updateService) {
        _appSettingsStore = appSettingsStore;
        _startupService = startupService;
        _agentStatusSource = agentStatusSource;
        _agentControlService = agentControlService;
        _updateService = updateService;

        _mainForm = new MainForm();
        _mainForm.StartWithWindowsChanged += async (_, enabled) => await UpdateStartupAsync(enabled);
        _mainForm.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesInteractiveAsync();
        _mainForm.RestartAgentRequested += async (_, _) => await RestartAgentInteractiveAsync();

        _notifyIcon = new NotifyIcon {
            Visible = true,
            Icon = SystemIcons.Application,
            Text = "Hardness Print Bridge",
            ContextMenuStrip = BuildContextMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainForm();

        _statusTimer = new System.Windows.Forms.Timer {
            Interval = (int)StatusPollInterval.TotalMilliseconds
        };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();

        _ = InitializeAsync();
    }

    private async Task InitializeAsync() {
        _settings = await _appSettingsStore.LoadAsync(CancellationToken.None);
        var startupEnabled = _startupService.IsEnabled();
        if (startupEnabled != _settings.StartWithWindows) {
            _settings = _settings with { StartWithWindows = startupEnabled };
            await _appSettingsStore.SaveAsync(_settings, CancellationToken.None);
        }

        _mainForm.ApplySettings(_settings);
        _statusTimer.Start();
        await RefreshStatusAsync();

        if (_settings.CheckForUpdatesOnStartup) {
            _ = CheckForUpdatesSilentlyAsync();
        }

        ShowMainForm();
    }

    private ContextMenuStrip BuildContextMenu() {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir aplicativo", null, (_, _) => ShowMainForm());
        menu.Items.Add("Configurações", null, (_, _) => ShowMainForm());
        menu.Items.Add("Verificar atualizações", null, async (_, _) => await CheckForUpdatesInteractiveAsync());
        menu.Items.Add("Reiniciar serviço", null, async (_, _) => await RestartAgentInteractiveAsync());
        menu.Items.Add("Sair", null, (_, _) => ExitApplication());
        return menu;
    }

    private void ShowMainForm() {
        if (_mainForm.Visible) {
            _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Activate();
            return;
        }

        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private async Task RefreshStatusAsync() {
        var snapshot = await _agentStatusSource.GetCurrentAsync(CancellationToken.None);
        var stale = snapshot is not null && DateTimeOffset.UtcNow - snapshot.UpdatedAtUtc > StaleStatusThreshold;

        _mainForm.UpdateStatus(snapshot, stale);
        ApplyTrayPresentation(snapshot, stale);

        if (DateTimeOffset.UtcNow - _lastUpdateCheckAtUtc >= TimeSpan.FromHours(Math.Max(_settings.UpdateCheckIntervalHours, 1))) {
            _ = CheckForUpdatesSilentlyAsync();
        }
    }

    private void ApplyTrayPresentation(AgentStatusSnapshot? snapshot, bool stale) {
        if (snapshot is null) {
            _notifyIcon.Icon = SystemIcons.Application;
            _notifyIcon.Text = "HPB: aguardando status do Agent";
            return;
        }

        var effectiveState = stale ? AgentState.Warning : snapshot.State;
        _notifyIcon.Icon = effectiveState switch {
            AgentState.Starting => SystemIcons.Shield,
            AgentState.Running => SystemIcons.Information,
            AgentState.Warning => SystemIcons.Warning,
            AgentState.Error => SystemIcons.Error,
            AgentState.Stopped => SystemIcons.Application,
            _ => SystemIcons.Application
        };

        var tooltip = $"HPB: {effectiveState}";
        if (!string.IsNullOrWhiteSpace(snapshot.Message)) {
            tooltip = $"{tooltip} - {snapshot.Message}";
        }

        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    private async Task UpdateStartupAsync(bool enabled) {
        _startupService.SetEnabled(enabled);
        _settings = _settings with { StartWithWindows = enabled };
        await _appSettingsStore.SaveAsync(_settings, CancellationToken.None);
    }

    private async Task RestartAgentInteractiveAsync() {
        try {
            await _agentControlService.RestartAsync(CancellationToken.None);
            _notifyIcon.ShowBalloonTip(3000, "Hardness Print Bridge", "Solicitação de reinício do Agent enviada.", ToolTipIcon.Info);
        } catch (Exception ex) {
            MessageBox.Show(
                _mainForm,
                ex.Message,
                "Reiniciar serviço",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task CheckForUpdatesInteractiveAsync() {
        try {
            var result = await _updateService.CheckForUpdatesAsync(CancellationToken.None);
            _lastUpdateCheckAtUtc = DateTimeOffset.UtcNow;

            if (!result.UpdateAvailable) {
                MessageBox.Show(
                    _mainForm,
                    $"Nenhuma atualização disponível. Versão atual: {result.CurrentVersion}.",
                    "Verificar atualizações",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var decision = MessageBox.Show(
                _mainForm,
                $"Nova versão disponível: {result.LatestVersion} (atual: {result.CurrentVersion}). Deseja instalar agora?",
                "Verificar atualizações",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (decision != DialogResult.Yes) {
                return;
            }

            await _updateService.BeginUpdateAsync(result, CancellationToken.None);
            ExitApplication();
        } catch (Exception ex) {
            MessageBox.Show(
                _mainForm,
                ex.Message,
                "Verificar atualizações",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task CheckForUpdatesSilentlyAsync() {
        try {
            var result = await _updateService.CheckForUpdatesAsync(CancellationToken.None);
            _lastUpdateCheckAtUtc = DateTimeOffset.UtcNow;

            if (!result.UpdateAvailable) {
                return;
            }

            _notifyIcon.ShowBalloonTip(
                4000,
                "Hardness Print Bridge",
                $"Nova versão disponível: {result.LatestVersion}.",
                ToolTipIcon.Info);
        } catch {
            _lastUpdateCheckAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private void ExitApplication() {
        _statusTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _mainForm.AllowExit();
        _mainForm.Close();
        ExitThread();
    }
}
