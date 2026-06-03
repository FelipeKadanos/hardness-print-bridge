using Hardness.PrintBridge.App.Services;
using Hardness.PrintBridge.Contracts.Runtime;

namespace Hardness.PrintBridge.App.UI;

public sealed class MainForm : Form {
    private readonly Label _statusValueLabel;
    private readonly Label _messageValueLabel;
    private readonly Label _updatedAtValueLabel;
    private readonly CheckBox _startWithWindowsCheckBox;
    private bool _allowClose;

    public event EventHandler<bool>? StartWithWindowsChanged;
    public event EventHandler? CheckForUpdatesRequested;
    public event EventHandler? RestartAgentRequested;

    public MainForm() {
        Text = "Hardness Print Bridge";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(520, 320);

        var titleLabel = new Label {
            Text = "Hardness Print Bridge",
            Dock = DockStyle.Top,
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            Height = 42
        };

        _statusValueLabel = BuildValueLabel();
        _messageValueLabel = BuildValueLabel();
        _updatedAtValueLabel = BuildValueLabel();

        _startWithWindowsCheckBox = new CheckBox {
            AutoSize = true,
            Text = "Iniciar aplicativo com o Windows"
        };
        _startWithWindowsCheckBox.CheckedChanged += OnCheckboxChanged;

        var updateButton = new Button {
            Text = "Verificar atualizações",
            AutoSize = true
        };
        updateButton.Click += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

        var restartButton = new Button {
            Text = "Reiniciar serviço",
            AutoSize = true
        };
        restartButton.Click += (_, _) => RestartAgentRequested?.Invoke(this, EventArgs.Empty);

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
        actionsFlow.Controls.Add(updateButton);
        actionsFlow.Controls.Add(restartButton);

        var container = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(16)
        };
        container.Controls.Add(actionsFlow);
        container.Controls.Add(_startWithWindowsCheckBox);
        container.Controls.Add(statusLayout);
        container.Controls.Add(titleLabel);

        Controls.Add(container);
    }

    public void ApplySettings(AppSettings settings) {
        _startWithWindowsCheckBox.CheckedChanged -= OnCheckboxChanged;
        _startWithWindowsCheckBox.Checked = settings.StartWithWindows;
        _startWithWindowsCheckBox.CheckedChanged += OnCheckboxChanged;
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

    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing) {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private static Label BuildCaptionLabel(string text) {
        return new Label {
            AutoSize = true,
            Text = text,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };
    }

    private static Label BuildValueLabel() {
        return new Label {
            AutoSize = true,
            MaximumSize = new Size(320, 0)
        };
    }

    private void OnCheckboxChanged(object? sender, EventArgs e) {
        StartWithWindowsChanged?.Invoke(this, _startWithWindowsCheckBox.Checked);
    }
}
