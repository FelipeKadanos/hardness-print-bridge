using System.Diagnostics;
using Hardness.PrintBridge.App.Services;
using Hardness.PrintBridge.App.Status;
using Hardness.PrintBridge.App.Update;
using Hardness.PrintBridge.Contracts.Configuration;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.UI;

public sealed class TrayApplicationContext : ApplicationContext {
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleStatusThreshold = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LogPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AgentStartupGracePeriod = TimeSpan.FromSeconds(30);

    private readonly IAppSettingsStore _appSettingsStore;
    private readonly IAgentConfigurationStore _agentConfigurationStore;
    private readonly IPrinterCatalogService _printerCatalogService;
    private readonly IStartupService _startupService;
    private readonly IAgentStatusSource _agentStatusSource;
    private readonly IAgentLogSource _agentLogSource;
    private readonly IAgentControlService _agentControlService;
    private readonly IUpdateService _updateService;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private readonly System.Windows.Forms.Timer _logTimer;
    private readonly System.Windows.Forms.Timer _startupTimer;
    private readonly MainForm _mainForm;
    private ToolStripMenuItem? _restartAgentMenuItem;
    private AppSettings _settings = new();
    private DateTimeOffset _lastUpdateCheckAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _awaitAgentStatusUntilUtc = DateTimeOffset.MinValue;
    private string? _agentStartupFailureMessage;
    private bool _restartAgentInProgress;

    public TrayApplicationContext(
        IAppSettingsStore appSettingsStore,
        IAgentConfigurationStore agentConfigurationStore,
        IPrinterCatalogService printerCatalogService,
        IStartupService startupService,
        IAgentStatusSource agentStatusSource,
        IAgentLogSource agentLogSource,
        IAgentControlService agentControlService,
        IUpdateService updateService) {
        _appSettingsStore = appSettingsStore;
        _agentConfigurationStore = agentConfigurationStore;
        _printerCatalogService = printerCatalogService;
        _startupService = startupService;
        _agentStatusSource = agentStatusSource;
        _agentLogSource = agentLogSource;
        _agentControlService = agentControlService;
        _updateService = updateService;

        _mainForm = new MainForm();
        _mainForm.StartWithWindowsChanged += async (_, enabled) => await UpdateStartupAsync(enabled);
        _mainForm.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesInteractiveAsync();
        _mainForm.RestartAgentRequested += async (_, _) => await RestartAgentInteractiveAsync();
        _mainForm.SaveAgentConfigurationRequested += async (_, eventArgs) => await SaveAgentConfigurationInteractiveAsync(eventArgs.Configuration);

        _notifyIcon = new NotifyIcon {
            Visible = true,
            Icon = AppIconProvider.GetAppIcon(),
            Text = "Hardness Print Bridge",
            ContextMenuStrip = BuildContextMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainForm();

        _statusTimer = new System.Windows.Forms.Timer {
            Interval = (int)StatusPollInterval.TotalMilliseconds
        };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();

        _logTimer = new System.Windows.Forms.Timer {
            Interval = (int)LogPollInterval.TotalMilliseconds
        };
        _logTimer.Tick += async (_, _) => await RefreshLogsAsync();

        _startupTimer = new System.Windows.Forms.Timer {
            Interval = 1
        };
        _startupTimer.Tick += OnStartupTimerTick;
        _startupTimer.Start();
    }

    private void OnStartupTimerTick(object? sender, EventArgs e) {
        _startupTimer.Stop();
        _startupTimer.Tick -= OnStartupTimerTick;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync() {
        try {
            _settings = await _appSettingsStore.LoadAsync(CancellationToken.None);
            var startupEnabled = _startupService.IsEnabled();
            if (startupEnabled != _settings.StartWithWindows) {
                _settings = _settings with { StartWithWindows = startupEnabled };
                await _appSettingsStore.SaveAsync(_settings, CancellationToken.None);
            }

            _mainForm.ApplySettings(_settings);
            var agentConfiguration = await _agentConfigurationStore.LoadAsync(CancellationToken.None);
            _mainForm.ApplyAgentConfiguration(agentConfiguration, _printerCatalogService.GetInstalledPrinters());

            await EnsureAgentRunningAsync();

            _statusTimer.Start();
            _logTimer.Start();
            await RefreshStatusAsync();
            await RefreshLogsAsync();

            if (_settings.CheckForUpdatesOnStartup) {
                _ = CheckForUpdatesSilentlyAsync();
            }

            ShowMainForm();
        } catch (Exception ex) {
            MessageBox.Show(
                _mainForm,
                ex.Message,
                "Falha ao iniciar o Hardness Print Bridge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ExitApplication();
        }
    }

    private ContextMenuStrip BuildContextMenu() {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir aplicativo", null, (_, _) => ShowMainForm());
        menu.Items.Add("Configurações", null, (_, _) => ShowMainForm());
        menu.Items.Add("Verificar atualizações", null, async (_, _) => await CheckForUpdatesInteractiveAsync());
        _restartAgentMenuItem = new ToolStripMenuItem("Reiniciar serviço", null, async (_, _) => await RestartAgentInteractiveAsync());
        menu.Items.Add(_restartAgentMenuItem);
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
        var hostStatus = await _agentControlService.GetCurrentStatusAsync(CancellationToken.None);
        var snapshot = await _agentStatusSource.GetCurrentAsync(CancellationToken.None);
        var displayStatus = ResolveDisplayStatus(hostStatus, snapshot);

        _mainForm.UpdateStatus(displayStatus);
        ApplyTrayPresentation(displayStatus);

        if (DateTimeOffset.UtcNow - _lastUpdateCheckAtUtc >= TimeSpan.FromHours(Math.Max(_settings.UpdateCheckIntervalHours, 1))) {
            _ = CheckForUpdatesSilentlyAsync();
        }
    }

    private async Task RefreshLogsAsync() {
        try {
            var snapshot = await _agentLogSource.GetSnapshotAsync(CancellationToken.None);
            _mainForm.UpdateLogs(snapshot.SourcePath, snapshot.Content);
        } catch (Exception ex) {
            _mainForm.UpdateLogs(null, $"Falha ao carregar logs: {ex.Message}");
        }
    }

    private AgentDisplayStatus ResolveDisplayStatus(AgentHostStatus hostStatus, AgentStatusSnapshot? snapshot) {
        var liveSnapshot = TryResolveLiveSnapshot(snapshot);

        if (hostStatus.State == AgentState.Stopped || hostStatus.State == AgentState.Error) {
            return new AgentDisplayStatus(
                hostStatus.State,
                _agentStartupFailureMessage ?? hostStatus.Message,
                snapshot?.UpdatedAtUtc);
        }

        if (liveSnapshot is not null) {
            var isStale = DateTimeOffset.UtcNow - liveSnapshot.UpdatedAtUtc > StaleStatusThreshold;
            if (isStale) {
                return new AgentDisplayStatus(
                    AgentState.Warning,
                    "Agent sem atualização recente de status.",
                    liveSnapshot.UpdatedAtUtc);
            }

            return new AgentDisplayStatus(
                liveSnapshot.State,
                string.IsNullOrWhiteSpace(liveSnapshot.Message) ? hostStatus.Message : liveSnapshot.Message!,
                liveSnapshot.UpdatedAtUtc);
        }

        if (DateTimeOffset.UtcNow <= _awaitAgentStatusUntilUtc || hostStatus.State == AgentState.Starting) {
            return new AgentDisplayStatus(
                AgentState.Starting,
                _agentStartupFailureMessage ?? hostStatus.Message,
                snapshot?.UpdatedAtUtc);
        }

        if (snapshot is not null) {
            return new AgentDisplayStatus(
                AgentState.Warning,
                "O último status disponível não pertence a um processo ativo do Agent.",
                snapshot.UpdatedAtUtc);
        }

        return new AgentDisplayStatus(
            AgentState.Stopped,
            _agentStartupFailureMessage ?? "Agent não está em execução.",
            null);
    }

    private static AgentStatusSnapshot? TryResolveLiveSnapshot(AgentStatusSnapshot? snapshot) {
        if (snapshot?.ProcessId is null || snapshot.ProcessStartedAtUtc is null) {
            return null;
        }

        try {
            using var process = Process.GetProcessById(snapshot.ProcessId.Value);
            if (process.HasExited) {
                return null;
            }

            var actualStartTimeUtc = process.StartTime.ToUniversalTime();
            var difference = (actualStartTimeUtc - snapshot.ProcessStartedAtUtc.Value.UtcDateTime).Duration();
            return difference <= TimeSpan.FromSeconds(2) ? snapshot : null;
        } catch {
            return null;
        }
    }

    private void ApplyTrayPresentation(AgentDisplayStatus status) {
        _notifyIcon.Icon = AppIconProvider.GetStatusIcon(status.State);

        var tooltip = $"HPB: {status.State}";
        if (!string.IsNullOrWhiteSpace(status.Message)) {
            tooltip = $"{tooltip} - {status.Message}";
        }

        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    private async Task EnsureAgentRunningAsync() {
        _agentStartupFailureMessage = null;

        try {
            await _agentControlService.EnsureRunningAsync(CancellationToken.None);
            _awaitAgentStatusUntilUtc = DateTimeOffset.UtcNow.Add(AgentStartupGracePeriod);
        } catch (Exception ex) {
            _agentStartupFailureMessage = ex.Message;
            _awaitAgentStatusUntilUtc = DateTimeOffset.MinValue;
        }
    }

    private async Task UpdateStartupAsync(bool enabled) {
        _startupService.SetEnabled(enabled);
        _settings = _settings with { StartWithWindows = enabled };
        await _appSettingsStore.SaveAsync(_settings, CancellationToken.None);
    }

    private async Task RestartAgentInteractiveAsync() {
        if (_restartAgentInProgress) {
            return;
        }

        _restartAgentInProgress = true;
        _restartAgentMenuItem!.Enabled = false;
        _mainForm.SetInteractionLocked(true);

        try {
            await _agentControlService.RestartAsync(CancellationToken.None);
            _agentStartupFailureMessage = null;
            _awaitAgentStatusUntilUtc = DateTimeOffset.UtcNow.Add(AgentStartupGracePeriod);
            _notifyIcon.ShowBalloonTip(3000, "Hardness Print Bridge", "Solicitação de reinício do Agent enviada.", ToolTipIcon.Info);
        } catch (Exception ex) {
            _agentStartupFailureMessage = ex.Message;
            MessageBox.Show(
                _mainForm,
                ex.Message,
                "Reiniciar serviço",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        } finally {
            _mainForm.SetInteractionLocked(false);
            _restartAgentMenuItem!.Enabled = true;
            _restartAgentInProgress = false;
        }
    }

    private async Task SaveAgentConfigurationInteractiveAsync(AgentConfigurationModel configuration) {
        try {
            await _agentConfigurationStore.SaveAsync(configuration, CancellationToken.None);
            MessageBox.Show(
                _mainForm,
                "Configuração salva com sucesso. Reinicie o Agent para aplicar as alteracoes.",
                "Configuração do Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        } catch (Exception ex) {
            MessageBox.Show(
                _mainForm,
                ex.Message,
                "Configuração do Agent",
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
        _logTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _mainForm.AllowExit();
        _mainForm.Close();
        ExitThread();
    }
}
