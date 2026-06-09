using Hardness.PrintBridge.App.Services;
using Hardness.PrintBridge.Contracts.Configuration;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.UI;

public sealed class MainForm : Form {
    private readonly Label _statusValueLabel;
    private readonly Label _messageValueLabel;
    private readonly Label _updatedAtValueLabel;
    private readonly CheckBox _startWithWindowsCheckBox;
    private Button _updateButton = null!;
    private Button _restartButton = null!;
    private readonly TextBox _queueRootPathTextBox;
    private readonly TextBox _apiAuthTokenTextBox;
    private readonly TextBox _remoteListUrlTextBox;
    private readonly TextBox _remoteDownloadUrlTemplateTextBox;
    private readonly TextBox _hardnessCallbackUrlTextBox;
    private readonly ComboBox _printerComboBox;
    private BusyOverlayForm? _busyOverlayForm;
    private bool _allowClose;

    public event EventHandler<bool>? StartWithWindowsChanged;
    public event EventHandler? CheckForUpdatesRequested;
    public event EventHandler? RestartAgentRequested;
    public event EventHandler<AgentConfigurationSaveRequestedEventArgs>? SaveAgentConfigurationRequested;

    public MainForm() {
        Text = "Hardness Print Bridge";
        Icon = AppIconProvider.GetAppIcon();
        StartPosition = FormStartPosition.CenterScreen;

        var titleLabel = new Label {
            Text = "Hardness Print Bridge",
            Dock = DockStyle.Top,
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            Height = 42
        };

        var tabs = new TabControl {
            Dock = DockStyle.Fill
        };

        _statusValueLabel = BuildValueLabel();
        _messageValueLabel = BuildValueLabel();
        _updatedAtValueLabel = BuildValueLabel();
        _startWithWindowsCheckBox = new CheckBox {
            AutoSize = true,
            Text = "Iniciar aplicativo com o Windows"
        };
        _startWithWindowsCheckBox.CheckedChanged += OnCheckboxChanged;

        _queueRootPathTextBox = BuildTextBox();
        _apiAuthTokenTextBox = BuildTextBox();
        _remoteListUrlTextBox = BuildTextBox();
        _remoteDownloadUrlTemplateTextBox = BuildTextBox();
        _hardnessCallbackUrlTextBox = BuildTextBox();
        _printerComboBox = new ComboBox {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        tabs.TabPages.Add(BuildOverviewTab());
        tabs.TabPages.Add(BuildConfigurationTab());

        var container = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(16)
        };
        container.Controls.Add(tabs);
        container.Controls.Add(titleLabel);

        Controls.Add(container);
        MinimumSize = new Size(860, 620);
    }

    public void ApplySettings(AppSettings settings) {
        _startWithWindowsCheckBox.CheckedChanged -= OnCheckboxChanged;
        _startWithWindowsCheckBox.Checked = settings.StartWithWindows;
        _startWithWindowsCheckBox.CheckedChanged += OnCheckboxChanged;
    }

    public void ApplyAgentConfiguration(AgentConfigurationModel configuration, IReadOnlyList<string> printers) {
        _queueRootPathTextBox.Text = configuration.QueueRootPath;
        _apiAuthTokenTextBox.Text = configuration.ApiAuthToken;
        _remoteListUrlTextBox.Text = configuration.RemoteListUrl;
        _remoteDownloadUrlTemplateTextBox.Text = configuration.RemoteDownloadUrlTemplate;
        _hardnessCallbackUrlTextBox.Text = configuration.HardnessCallbackUrl;

        _printerComboBox.BeginUpdate();
        try {
            _printerComboBox.Items.Clear();
            foreach (var printer in printers) {
                _printerComboBox.Items.Add(printer);
            }

            if (_printerComboBox.Items.Count == 0) {
                _printerComboBox.Items.Add(configuration.DefaultPrinterName);
            }

            var selectedPrinter = configuration.DefaultPrinterName;
            if (string.IsNullOrWhiteSpace(selectedPrinter) && _printerComboBox.Items.Count > 0) {
                selectedPrinter = _printerComboBox.Items[0]?.ToString() ?? string.Empty;
            }

            var index = _printerComboBox.FindStringExact(selectedPrinter);
            _printerComboBox.SelectedIndex = index >= 0 ? index : 0;
        } finally {
            _printerComboBox.EndUpdate();
        }
    }

    public void UpdateStatus(AgentStatusSnapshot? snapshot, bool stale) {
        if (snapshot is null) {
            _statusValueLabel.Text = "Desconhecido";
            _messageValueLabel.Text = "Nenhum status publicado pelo Agent ainda.";
            _updatedAtValueLabel.Text = "-";
            return;
        }

        _statusValueLabel.Text = stale
            ? $"{snapshot.State} (desatualizado)"
            : snapshot.State;
        _messageValueLabel.Text = string.IsNullOrWhiteSpace(snapshot.Message)
            ? "-"
            : snapshot.Message;
        _updatedAtValueLabel.Text = snapshot.UpdatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    }

    public void AllowExit() {
        _allowClose = true;
    }

    public void ShowBusyOverlay(string baseText) {
        _busyOverlayForm ??= new BusyOverlayForm(this);
        _busyOverlayForm.ShowOverlay(baseText);
        ToggleInteractiveControls(false);
    }

    public void HideBusyOverlay() {
        _busyOverlayForm?.HideOverlay();
        ToggleInteractiveControls(true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing) {
            e.Cancel = true;
            Hide();
            return;
        }

        _busyOverlayForm?.Close();
        base.OnFormClosing(e);
    }

    private TabPage BuildOverviewTab() {
        _updateButton = new Button {
            Text = "Verificar atualizacoes",
            AutoSize = true
        };
        _updateButton.Click += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

        _restartButton = new Button {
            Text = "Reiniciar servico",
            AutoSize = true
        };
        _restartButton.Click += (_, _) => RestartAgentRequested?.Invoke(this, EventArgs.Empty);

        var statusLayout = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            Padding = new Padding(0, 12, 0, 12)
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusLayout.Controls.Add(BuildCaptionLabel("Status"), 0, 0);
        statusLayout.Controls.Add(_statusValueLabel, 1, 0);
        statusLayout.Controls.Add(BuildCaptionLabel("Mensagem"), 0, 1);
        statusLayout.Controls.Add(_messageValueLabel, 1, 1);
        statusLayout.Controls.Add(BuildCaptionLabel("Atualizado"), 0, 2);
        statusLayout.Controls.Add(_updatedAtValueLabel, 1, 2);

        var actionsFlow = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        actionsFlow.Controls.Add(_updateButton);
        actionsFlow.Controls.Add(_restartButton);

        var content = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        content.Controls.Add(actionsFlow);
        content.Controls.Add(_startWithWindowsCheckBox);
        content.Controls.Add(statusLayout);

        var page = new TabPage("Visao geral");
        page.Controls.Add(content);
        return page;
    }

    private TabPage BuildConfigurationTab() {
        var saveButton = new Button {
            Text = "Salvar configuracao",
            AutoSize = true
        };
        saveButton.Click += (_, _) => SaveConfiguration();

        var fields = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(0, 12, 0, 12)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(fields, "Pasta raiz da fila", _queueRootPathTextBox);
        AddField(fields, "API_AUTH", _apiAuthTokenTextBox);
        AddField(fields, "RemoteListUrl", _remoteListUrlTextBox);
        AddField(fields, "RemoteDownloadUrlTemplate", _remoteDownloadUrlTemplateTextBox);
        AddField(fields, "HardnessCallbackUrl", _hardnessCallbackUrlTextBox);
        AddField(fields, "Impressora padrao", _printerComboBox);

        var helpLabel = new Label {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "Use REPLACE_ME nas URLs para o token ser substituido automaticamente pelo API_AUTH informado acima."
        };

        var content = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        content.Controls.Add(saveButton);
        content.Controls.Add(helpLabel);
        content.Controls.Add(fields);

        var page = new TabPage("Configuracao");
        page.Controls.Add(content);
        return page;
    }

    private void SaveConfiguration() {
        var configuration = new AgentConfigurationModel {
            QueueRootPath = _queueRootPathTextBox.Text,
            ApiAuthToken = _apiAuthTokenTextBox.Text,
            RemoteListUrl = _remoteListUrlTextBox.Text,
            RemoteDownloadUrlTemplate = _remoteDownloadUrlTemplateTextBox.Text,
            HardnessCallbackUrl = _hardnessCallbackUrlTextBox.Text,
            DefaultPrinterName = _printerComboBox.SelectedItem?.ToString() ?? _printerComboBox.Text,
            RemoteSourceEnabled = true
        };

        SaveAgentConfigurationRequested?.Invoke(this, new AgentConfigurationSaveRequestedEventArgs(configuration));
    }

    private static void AddField(TableLayoutPanel layout, string labelText, Control control) {
        var rowIndex = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 0, 0, 10);

        layout.Controls.Add(BuildCaptionLabel(labelText), 0, rowIndex);
        layout.Controls.Add(control, 1, rowIndex);
    }

    private static Label BuildCaptionLabel(string text) {
        return new Label {
            AutoSize = true,
            Text = text,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 6, 12, 0)
        };
    }

    private static Label BuildValueLabel() {
        return new Label {
            AutoSize = true,
            MaximumSize = new Size(620, 0)
        };
    }

    private static TextBox BuildTextBox() {
        return new TextBox {
            Width = 520
        };
    }

    private void ToggleInteractiveControls(bool enabled) {
        _startWithWindowsCheckBox.Enabled = enabled;
        _updateButton.Enabled = enabled;
        _restartButton.Enabled = enabled;
        _queueRootPathTextBox.Enabled = enabled;
        _apiAuthTokenTextBox.Enabled = enabled;
        _remoteListUrlTextBox.Enabled = enabled;
        _remoteDownloadUrlTemplateTextBox.Enabled = enabled;
        _hardnessCallbackUrlTextBox.Enabled = enabled;
        _printerComboBox.Enabled = enabled;
    }

    private void OnCheckboxChanged(object? sender, EventArgs e) {
        StartWithWindowsChanged?.Invoke(this, _startWithWindowsCheckBox.Checked);
    }
}

public sealed class AgentConfigurationSaveRequestedEventArgs(AgentConfigurationModel configuration) : EventArgs {
    public AgentConfigurationModel Configuration { get; } = configuration;
}
