using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace SimpleAudioRecorder;

public sealed class MainForm : Form
{
    private const string GitHubUrl = "https://github.com/SickPuppyCoding/SimpleAudioRecorder";
    private const float CardRadius = 18F;

    private readonly AudioCaptureService captureService = new();
    private readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 90 };
    private readonly ToolTip toolTip = new() { ShowAlways = true };
    private readonly ContextMenuStrip appMenu = new();

    private readonly Label timerLabel = new();
    private readonly LinkLabel modeLink = new();
    private readonly StatusBadgeControl statusBadge = new();
    private readonly DotMeterControl levelMeter = new();
    private readonly RecordActionButton recordButton = new();

    private CaptureMode selectedMode = CaptureMode.Both;
    private bool previewMode;
    private bool previewRecording;

    public MainForm()
    {
        Text = "Simple Audio Recorder";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(294, 156);
        MinimumSize = Size;
        MaximumSize = Size;
        FormBorderStyle = FormBorderStyle.None;
        ShowIcon = false;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(13, 17, 39);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        DoubleBuffered = true;
        Padding = new Padding(18, 16, 18, 18);

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        BuildLayout();
        BuildContextMenu();
        AttachContextMenu(this, appMenu);
        AttachDragBehavior(this);
        ApplyVisualState();
        UpdateStatus(null, default, null);
        UpdateModeText();
        UpdateWindowRegion();

        uiTimer.Tick += HandleUiTick;
        modeLink.LinkClicked += (_, _) => CycleMode();
        recordButton.Click += async (_, _) => await ToggleRecordingAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!captureService.IsRecording)
        {
            captureService.Dispose();
            base.OnFormClosing(e);
            return;
        }

        var result = MessageBox.Show(
            this,
            "A recording is still in progress. Stop and save it before closing?",
            "Stop Recording",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (result == DialogResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == DialogResult.No)
        {
            captureService.Dispose();
            base.OnFormClosing(e);
            return;
        }

        try
        {
            captureService.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Recording Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            captureService.Dispose();
        }

        base.OnFormClosing(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutCompactControls();
        UpdateWindowRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = CreateRoundedRectangle(ClientRectangle, CardRadius);
        using var backgroundBrush = new SolidBrush(BackColor);
        using var borderPen = new Pen(Color.FromArgb(49, 57, 96));

        e.Graphics.FillPath(backgroundBrush, path);
        e.Graphics.DrawPath(borderPen, path);
        base.OnPaint(e);
    }

    internal void ApplyPreviewState(UiPreviewState state)
    {
        previewMode = true;
        previewRecording = state.IsRecording;
        selectedMode = state.Mode;

        timerLabel.Text = state.TimerText;
        levelMeter.SetPreviewLevels(state.Levels);
        UpdateModeText();
        UpdateStatus(state.StatusText, state.StatusColor, state.StatusDetails);
        ApplyVisualState();
        Refresh();
    }

    private void BuildLayout()
    {
        timerLabel.AutoSize = true;
        timerLabel.Font = new Font("Cascadia Mono", 28F, FontStyle.Bold, GraphicsUnit.Point);
        timerLabel.ForeColor = Color.FromArgb(250, 251, 255);
        timerLabel.Text = "00:00";
        timerLabel.BackColor = Color.Transparent;

        modeLink.AutoSize = true;
        modeLink.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
        modeLink.LinkBehavior = LinkBehavior.HoverUnderline;
        modeLink.ActiveLinkColor = Color.FromArgb(187, 176, 255);
        modeLink.LinkColor = Color.FromArgb(145, 136, 255);
        modeLink.VisitedLinkColor = modeLink.LinkColor;
        modeLink.BackColor = Color.Transparent;
        modeLink.TabStop = false;

        statusBadge.Visible = false;
        statusBadge.BackColor = Color.Transparent;

        levelMeter.Size = new Size(68, 14);
        levelMeter.BackColor = Color.Transparent;

        recordButton.Size = new Size(ClientSize.Width - Padding.Horizontal, 44);
        recordButton.BackColor = Color.Transparent;

        Controls.Add(timerLabel);
        Controls.Add(modeLink);
        Controls.Add(statusBadge);
        Controls.Add(levelMeter);
        Controls.Add(recordButton);

        LayoutCompactControls();
    }

    private void BuildContextMenu()
    {
        appMenu.Items.Add("About App", null, (_, _) => ShowAboutDialog());
        appMenu.Items.Add("View Audio Files", null, (_, _) => OpenAudioFolder());
        appMenu.Items.Add(new ToolStripSeparator());
        appMenu.Items.Add("Exit", null, (_, _) => Close());
    }

    private void LayoutCompactControls()
    {
        var left = Padding.Left + 2;
        var top = Padding.Top + 2;
        var right = ClientSize.Width - Padding.Right - 2;

        timerLabel.Location = new Point(left, top);
        modeLink.Location = new Point(left + 2, timerLabel.Bottom - 2);
        levelMeter.Location = new Point(right - levelMeter.Width, top + 10);

        if (statusBadge.Visible)
        {
            statusBadge.Location = new Point(right - statusBadge.Width, top + 4);
        }

        recordButton.Location = new Point(Padding.Left + 1, ClientSize.Height - Padding.Bottom - recordButton.Height);
        recordButton.Width = ClientSize.Width - Padding.Horizontal - 2;
    }

    private void HandleUiTick(object? sender, EventArgs e)
    {
        if (previewMode)
        {
            return;
        }

        if (captureService.IsRecording)
        {
            timerLabel.Text = FormatElapsed(captureService.Elapsed);
        }

        levelMeter.PushLevel(captureService.ConsumePeak(), captureService.IsRecording);
    }

    private async Task ToggleRecordingAsync()
    {
        if (captureService.IsRecording)
        {
            await StopRecordingAsync();
            return;
        }

        await StartRecordingAsync();
    }

    private async Task StartRecordingAsync()
    {
        recordButton.Enabled = false;
        UpdateStatus("Preparing", Color.FromArgb(255, 211, 122), "Opening the Windows audio devices and preparing the recording file.");

        try
        {
            await captureService.StartAsync(selectedMode);
            timerLabel.Text = "00:00";
            uiTimer.Start();
            UpdateStatus("Recording", Color.FromArgb(255, 142, 168), $"{selectedMode.ToDisplayName()} capture is recording.");
        }
        catch (Exception ex)
        {
            UpdateStatus("Blocked", Color.FromArgb(255, 196, 120), ex.Message);
            MessageBox.Show(this, ex.Message, "Audio Capture Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            ApplyVisualState();
        }
    }

    private async Task StopRecordingAsync()
    {
        recordButton.Enabled = false;
        UpdateStatus("Saving", Color.FromArgb(255, 211, 122), "Finalizing the WAV file and cleaning up the recording session.");

        try
        {
            var info = await captureService.StopAsync();
            timerLabel.Text = "00:00";
            levelMeter.ResetMeter();
            UpdateStatus("Saved", Color.FromArgb(110, 230, 182), $"Saved {Path.GetFileName(info.FilePath)}");
        }
        catch (Exception ex)
        {
            UpdateStatus("Error", Color.FromArgb(255, 142, 168), ex.Message);
            MessageBox.Show(this, ex.Message, "Audio Capture Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            uiTimer.Stop();
            ApplyVisualState();
        }
    }

    private void ApplyVisualState()
    {
        recordButton.IsRecording = IsVisualRecording();
        recordButton.Enabled = previewMode || !captureService.IsRecording || recordButton.IsRecording;
        modeLink.Enabled = !IsVisualRecording();
        UpdateModeText();
    }

    private void CycleMode()
    {
        if (IsVisualRecording())
        {
            return;
        }

        selectedMode = selectedMode switch
        {
            CaptureMode.Both => CaptureMode.Microphone,
            CaptureMode.Microphone => CaptureMode.System,
            _ => CaptureMode.Both,
        };

        UpdateModeText();
        UpdateStatus(null, default, null);
    }

    private void UpdateModeText()
    {
        modeLink.Text = selectedMode.ToDisplayName();
        toolTip.SetToolTip(modeLink, $"{selectedMode.ToDisplayName()} capture. Click to switch between Both, Mic, and System.");
    }

    private void UpdateStatus(string? text, Color color, string? details)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            statusBadge.Visible = false;
            levelMeter.Visible = true;
            toolTip.SetToolTip(statusBadge, null);
            LayoutCompactControls();
            Invalidate();
            return;
        }

        statusBadge.Caption = text;
        statusBadge.AccentColor = color;
        statusBadge.Visible = true;
        levelMeter.Visible = false;
        toolTip.SetToolTip(statusBadge, details ?? text);
        LayoutCompactControls();
        statusBadge.Invalidate();
        Invalidate();
    }

    private bool IsVisualRecording()
    {
        return previewMode ? previewRecording : captureService.IsRecording;
    }

    private void OpenAudioFolder()
    {
        Directory.CreateDirectory(captureService.OutputDirectory);
        Process.Start(new ProcessStartInfo(captureService.OutputDirectory) { UseShellExecute = true });
    }

    private void ShowAboutDialog()
    {
        using var dialog = new AboutDialog(GitHubUrl);
        dialog.ShowDialog(this);
    }

    private void AttachContextMenu(Control root, ContextMenuStrip menu)
    {
        root.ContextMenuStrip = menu;

        foreach (Control child in root.Controls)
        {
            AttachContextMenu(child, menu);
        }
    }

    private void AttachDragBehavior(Control control)
    {
        if (control == recordButton || control == modeLink)
        {
            return;
        }

        control.MouseDown += HandleDragMouseDown;
        foreach (Control child in control.Controls)
        {
            AttachDragBehavior(child);
        }
    }

    private void HandleDragMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WmNclButtonDown, NativeMethods.HtCaption, 0);
    }

    private void UpdateWindowRegion()
    {
        using var path = CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), CardRadius);
        Region = new Region(path);
    }

    private static string FormatElapsed(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, float radius)
    {
        var rect = new RectangleF(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        var path = new GraphicsPath();
        var diameter = radius * 2F;

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class StatusBadgeControl : Control
    {
        private string caption = string.Empty;
        private Color accentColor = Color.FromArgb(110, 230, 182);

        public StatusBadgeControl()
        {
            Size = new Size(84, 24);
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            ForeColor = Color.White;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Caption
        {
            get => caption;
            set
            {
                caption = value;
                Width = Math.Max(64, TextRenderer.MeasureText(value, Font).Width + 24);
                Invalidate();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor
        {
            get => accentColor;
            set
            {
                accentColor = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectangle(ClientRectangle, 10F);
            using var fill = new SolidBrush(Color.FromArgb(34, accentColor));
            using var textBrush = new SolidBrush(accentColor);

            e.Graphics.FillPath(fill, path);
            TextRenderer.DrawText(
                e.Graphics,
                caption,
                Font,
                ClientRectangle,
                accentColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private sealed class DotMeterControl : Control
    {
        private float displayedLevel;
        private float phase;
        private float[]? previewLevels;

        public DotMeterControl()
        {
            Size = new Size(68, 14);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
        }

        public void PushLevel(float rawLevel, bool active)
        {
            var target = active ? Math.Clamp(rawLevel, 0F, 1F) : 0F;
            displayedLevel = Math.Max(target, displayedLevel * 0.84F);
            phase += active ? 0.28F : 0.08F;
            previewLevels = null;
            Invalidate();
        }

        public void ResetMeter()
        {
            displayedLevel = 0F;
            previewLevels = null;
            Invalidate();
        }

        public void SetPreviewLevels(IReadOnlyList<float> levels)
        {
            previewLevels = levels.Select(level => Math.Clamp(level, 0F, 1F)).ToArray();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            const int dotCount = 11;
            const float dotSize = 3.6F;
            const float spacing = 2.8F;

            for (var index = 0; index < dotCount; index++)
            {
                float intensity;
                if (previewLevels is { Length: > 0 })
                {
                    intensity = previewLevels[Math.Min(index, previewLevels.Length - 1)];
                }
                else
                {
                    var wobble = (MathF.Sin(phase + index * 0.52F) + 1F) * 0.5F;
                    intensity = 0.1F + displayedLevel * wobble;
                }

                var x = index * (dotSize + spacing);
                var y = (Height - dotSize) / 2F;
                var color = Blend(Color.FromArgb(66, 73, 109), Color.FromArgb(150, 136, 255), intensity);

                using var brush = new SolidBrush(color);
                e.Graphics.FillEllipse(brush, x, y, dotSize, dotSize);
            }
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0F, 1F);
            var r = (int)(from.R + (to.R - from.R) * amount);
            var g = (int)(from.G + (to.G - from.G) * amount);
            var b = (int)(from.B + (to.B - from.B) * amount);
            return Color.FromArgb(r, g, b);
        }
    }

    private sealed class RecordActionButton : Control
    {
        private bool hovered;
        private bool pressed;
        private bool isRecording;

        public RecordActionButton()
        {
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
            ForeColor = Color.White;
            Size = new Size(258, 44);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsRecording
        {
            get => isRecording;
            set
            {
                isRecording = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var start = isRecording ? Color.FromArgb(255, 98, 129) : Color.FromArgb(101, 77, 245);
            var end = isRecording ? Color.FromArgb(255, 88, 119) : Color.FromArgb(81, 67, 242);

            if (hovered)
            {
                start = Lighten(start, 0.05F);
                end = Lighten(end, 0.05F);
            }

            if (pressed)
            {
                start = Darken(start, 0.07F);
                end = Darken(end, 0.07F);
            }

            if (!Enabled)
            {
                start = Blend(start, Color.FromArgb(70, 75, 110), 0.45F);
                end = Blend(end, Color.FromArgb(70, 75, 110), 0.45F);
            }

            using var path = CreateRoundedRectangle(ClientRectangle, 13F);
            using var brush = new LinearGradientBrush(ClientRectangle, start, end, 0F);
            using var borderPen = new Pen(Color.FromArgb(40, 255, 255, 255));
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(borderPen, path);

            var text = isRecording ? "Stop" : "Record";
            var textSize = TextRenderer.MeasureText(text, Font);
            var iconRect = new Rectangle(
                (Width - textSize.Width - 24) / 2,
                (Height - 16) / 2 - 1,
                16,
                16);
            var textRect = new Rectangle(iconRect.Right + 8, 0, textSize.Width + 6, Height);

            if (isRecording)
            {
                using var iconBrush = new SolidBrush(ForeColor);
                e.Graphics.FillRectangle(iconBrush, iconRect.X + 2, iconRect.Y + 2, 11, 11);
            }
            else
            {
                DrawMicrophoneIcon(e.Graphics, iconRect, ForeColor);
            }

            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                textRect,
                Enabled ? ForeColor : Color.FromArgb(210, ForeColor),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        private static void DrawMicrophoneIcon(Graphics graphics, Rectangle bounds, Color color)
        {
            using var pen = new Pen(color, 1.7F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            var capsule = new RectangleF(bounds.X + 4, bounds.Y + 1, 7, 9);
            using var capsulePath = new GraphicsPath();
            capsulePath.AddArc(capsule.X, capsule.Y, capsule.Width, capsule.Width, 180, 180);
            capsulePath.AddLine(capsule.Right, capsule.Y + capsule.Width / 2F, capsule.Right, capsule.Bottom);
            capsulePath.AddArc(capsule.X, capsule.Bottom - capsule.Width, capsule.Width, capsule.Width, 0, 180);
            capsulePath.CloseFigure();
            graphics.DrawPath(pen, capsulePath);

            graphics.DrawLine(pen, bounds.X + 7.5F, bounds.Y + 10.2F, bounds.X + 7.5F, bounds.Bottom - 3);
            graphics.DrawArc(pen, bounds.X + 2.5F, bounds.Y + 6.5F, 10, 8, 15, 150);
            graphics.DrawLine(pen, bounds.X + 4.5F, bounds.Bottom - 2, bounds.X + 10.5F, bounds.Bottom - 2);
        }

        private static Color Lighten(Color color, float amount)
        {
            return Blend(color, Color.White, amount);
        }

        private static Color Darken(Color color, float amount)
        {
            return Blend(color, Color.Black, amount);
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0F, 1F);
            var r = (int)(from.R + (to.R - from.R) * amount);
            var g = (int)(from.G + (to.G - from.G) * amount);
            var b = (int)(from.B + (to.B - from.B) * amount);
            return Color.FromArgb(r, g, b);
        }
    }

    private static class NativeMethods
    {
        public const int WmNclButtonDown = 0x00A1;
        public const int HtCaption = 0x2;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }
}
