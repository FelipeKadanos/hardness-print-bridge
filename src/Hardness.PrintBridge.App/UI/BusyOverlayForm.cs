using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace Hardness.PrintBridge.App.UI;

internal sealed class BusyOverlayForm : Form {
    private readonly Form _owner;
    private readonly Panel _cardPanel;
    private readonly Label _titleLabel;
    private readonly Label _messageLabel;
    private readonly PictureBox _iconPictureBox;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private string _baseText = "Processando";
    private int _dotCount;

    public BusyOverlayForm(Form owner) {
        _owner = owner;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = false;
        BackColor = Color.Black;
        Opacity = 1;

        _iconPictureBox = new PictureBox {
            Size = new Size(28, 28),
            SizeMode = PictureBoxSizeMode.StretchImage,
            Image = AppIconProvider.GetAppIcon().ToBitmap()
        };

        _titleLabel = new Label {
            AutoSize = true,
            Text = "Hardness Print Bridge",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold),
            ForeColor = Color.FromArgb(32, 32, 32)
        };

        _messageLabel = new Label {
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 24, 24),
            Margin = new Padding(0, 20, 0, 0)
        };

        var headerFlow = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        headerFlow.Controls.Add(_iconPictureBox);
        headerFlow.Controls.Add(new Panel { Width = 10, Height = 1 });
        headerFlow.Controls.Add(_titleLabel);

        _cardPanel = new Panel {
            Size = new Size(400, 220),
            BackColor = Color.FromArgb(250, 250, 250),
            Padding = new Padding(28)
        };
        _cardPanel.Paint += OnCardPanelPaint;
        _cardPanel.Controls.Add(_messageLabel);
        _cardPanel.Controls.Add(headerFlow);

        Controls.Add(_cardPanel);

        _animationTimer = new System.Windows.Forms.Timer {
            Interval = 350
        };
        _animationTimer.Tick += (_, _) => AdvanceAnimation();
    }

    public void ShowOverlay(string baseText) {
        _baseText = string.IsNullOrWhiteSpace(baseText) ? "Processando" : baseText.Trim();
        _dotCount = 0;
        ApplyMessage();

        Bounds = _owner.Bounds;
        PositionCard();

        if (!Visible) {
            Show(_owner);
        }

        BringToFront();
        EnableBlur();
        _animationTimer.Start();
    }

    public void HideOverlay() {
        _animationTimer.Stop();
        Hide();
    }

    protected override void OnShown(EventArgs e) {
        base.OnShown(e);
        EnableBlur();
        PositionCard();
    }

    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        PositionCard();
        UpdateCardShape();
    }

    private void AdvanceAnimation() {
        _dotCount = (_dotCount % 3) + 1;
        ApplyMessage();
    }

    private void ApplyMessage() {
        _messageLabel.Text = _baseText + new string('.', _dotCount);
    }

    private void PositionCard() {
        _cardPanel.Left = Math.Max(0, (ClientSize.Width - _cardPanel.Width) / 2);
        _cardPanel.Top = Math.Max(0, (ClientSize.Height - _cardPanel.Height) / 2);
    }

    private void OnCardPanelPaint(object? sender, PaintEventArgs e) {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, _cardPanel.Width - 1, _cardPanel.Height - 1);
        using var path = CreateRoundedRectanglePath(rect, 16);
        using var fillBrush = new SolidBrush(_cardPanel.BackColor);
        using var borderPen = new Pen(Color.FromArgb(220, 224, 228), 1);
        e.Graphics.FillPath(fillBrush, path);
        e.Graphics.DrawPath(borderPen, path);
    }

    private void EnableBlur() {
        if (!IsHandleCreated) {
            return;
        }

        try {
            var accent = new AccentPolicy {
                AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND,
                GradientColor = unchecked((int)0xD0202020)
            };

            var accentSize = Marshal.SizeOf<AccentPolicy>();
            var accentPtr = Marshal.AllocHGlobal(accentSize);
            try {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = accentSize,
                    Data = accentPtr
                };
                SetWindowCompositionAttribute(Handle, ref data);
            } finally {
                Marshal.FreeHGlobal(accentPtr);
            }
        } catch {
            BackColor = Color.FromArgb(210, 32, 32, 32);
        }
    }

    protected override void OnLoad(EventArgs e) {
        base.OnLoad(e);
        UpdateCardShape();
    }

    private void UpdateCardShape() {
        using var path = CreateRoundedRectanglePath(new Rectangle(0, 0, _cardPanel.Width, _cardPanel.Height), 16);
        _cardPanel.Region?.Dispose();
        _cardPanel.Region = new Region(path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int cornerRadius) {
        var diameter = cornerRadius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private enum AccentState {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_BLURBEHIND = 3
    }

    private enum WindowCompositionAttribute {
        WCA_ACCENT_POLICY = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}
