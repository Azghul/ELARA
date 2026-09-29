using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace SimpleAudioRecorder;

public sealed class MainForm : Form
{
    private const string GitHubUrl = "https://github.com/Azghul/SimpleAudioRecorder";
    private const string UpstreamGitHubUrl = "https://github.com/SickPuppyCoding/SimpleAudioRecorder";
    private const float CardRadius = 18F;

    private readonly AudioCaptureService captureService = new();
    private readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 90 };
    private readonly ToolTip toolTip = new() { ShowAlways = true };
    private readonly ContextMenuStrip appMenu = new();
    private readonly ToolStripMenuItem microphoneMenuItem = new("Microphone");
    private readonly ToolStripMenuItem systemAudioMenuItem = new("System Audio");
    private readonly ToolStripMenuItem formatMenuItem = new("Format");
    private readonly WindowControlButton minimizeButton = new(WindowControlButtonKind.Minimize);
    private readonly WindowControlButton closeButton = new(WindowControlButtonKind.Close);
    private NotifyIcon? trayIcon;

    private readonly Label timerLabel = new();
    private readonly LinkLabel modeLink = new();
    private readonly StatusBadgeControl statusBadge = new();
    private readonly DotMeterControl levelMeter = new();
    private readonly RecordActionButton recordButton = new();

    private CaptureMode selectedMode = CaptureMode.Both;
    private string? selectedMicrophoneDeviceId;
    private string? selectedMicrophoneDeviceName;
    private string? selectedPlaybackDeviceId;
    private string? selectedPlaybackDeviceName;
    private OutputFormat selectedFormat = OutputFormat.Mp3;
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
        LoadSettings();
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
        AppLogger.Info("Main window initialized.");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!captureService.IsRecording)
        {
            DisposeTrayIcon();
            captureService.Dispose();
            base.OnFormClosing(e);
            return;
        }

        // Make sure the confirmation prompt has a visible owner when closing from the tray.
        if (!Visible)
        {
            Show();
            WindowState = FormWindowState.Normal;
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
            DisposeTrayIcon();
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

        DisposeTrayIcon();
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

        minimizeButton.Click += (_, _) => HideToTray();
        closeButton.Click += (_, _) => Close();

        Controls.Add(timerLabel);
        Controls.Add(modeLink);
        Controls.Add(statusBadge);
        Controls.Add(levelMeter);
        Controls.Add(recordButton);
        Controls.Add(minimizeButton);
        Controls.Add(closeButton);

        LayoutCompactControls();
    }

    private void BuildContextMenu()
    {
        microphoneMenuItem.DropDownOpening += (_, _) => RefreshMicrophoneMenu();
        systemAudioMenuItem.DropDownOpening += (_, _) => RefreshSystemAudioMenu();
        formatMenuItem.DropDownOpening += (_, _) => RefreshFormatMenu();

        appMenu.Items.Add("About App", null, (_, _) => ShowAboutDialog());
        appMenu.Items.Add("View Audio Files", null, (_, _) => OpenAudioFolder());
        appMenu.Items.Add(microphoneMenuItem);
        appMenu.Items.Add(systemAudioMenuItem);
        appMenu.Items.Add(formatMenuItem);
        appMenu.Items.Add(new ToolStripSeparator());
        appMenu.Items.Add("Exit", null, (_, _) => Close());

        RefreshMicrophoneMenu();
        RefreshSystemAudioMenu();
        RefreshFormatMenu();
    }

    private void LoadSettings()
    {
        var settings = AppSettings.Load();
        selectedFormat = settings.ResolveOutputFormat();

        try
        {
            var microphones = AudioInputDeviceCatalog.GetMicrophones();
            if (string.IsNullOrWhiteSpace(settings.SelectedMicrophoneDeviceId))
            {
                selectedMicrophoneDeviceId = null;
                selectedMicrophoneDeviceName = null;
            }
            else
            {
                var saved = microphones.FirstOrDefault(device =>
                    string.Equals(device.Id, settings.SelectedMicrophoneDeviceId, StringComparison.Ordinal));
                if (saved.Id is not null)
                {
                    selectedMicrophoneDeviceId = saved.Id;
                    selectedMicrophoneDeviceName = saved.DisplayName;
                }
                else
                {
                    selectedMicrophoneDeviceId = null;
                    selectedMicrophoneDeviceName = null;
                    AppLogger.Warn(
                        $"Saved microphone '{settings.SelectedMicrophoneDeviceId}' is no longer available; falling back to the Windows default.");
                }
            }

            var playbackDevices = AudioInputDeviceCatalog.GetPlaybackDevices();
            if (string.IsNullOrWhiteSpace(settings.SelectedPlaybackDeviceId))
            {
                selectedPlaybackDeviceId = null;
                selectedPlaybackDeviceName = null;
            }
            else
            {
                var saved = playbackDevices.FirstOrDefault(device =>
                    string.Equals(device.Id, settings.SelectedPlaybackDeviceId, StringComparison.Ordinal));
                if (saved.Id is not null)
                {
                    selectedPlaybackDeviceId = saved.Id;
                    selectedPlaybackDeviceName = saved.DisplayName;
                }
                else
                {
                    selectedPlaybackDeviceId = null;
                    selectedPlaybackDeviceName = null;
                    AppLogger.Warn(
                        $"Saved playback device '{settings.SelectedPlaybackDeviceId}' is no longer available; falling back to the Windows default.");
                }
            }
        }
        catch (Exception ex)
        {
            // Device validation is best-effort; the normal start error handling covers
            // machines without usable audio devices.
            AppLogger.Warn($"Could not validate the saved devices at startup. {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        new AppSettings
        {
            SelectedMicrophoneDeviceId = selectedMicrophoneDeviceId,
            SelectedPlaybackDeviceId = selectedPlaybackDeviceId,
            Format = selectedFormat.ToString(),
        }.Save();
    }

    private void LayoutCompactControls()
    {
        var left = Padding.Left + 2;
        var top = Padding.Top + 2;
        var right = ClientSize.Width - Padding.Right - 2;

        timerLabel.Location = new Point(left, top);
        modeLink.Location = new Point(left + 2, timerLabel.Bottom - 2);

        closeButton.Location = new Point(ClientSize.Width - 28, 6);
        minimizeButton.Location = new Point(closeButton.Left - 24, 6);

        // Keep the level meter / status badge clear of the window control buttons.
        var contentRight = minimizeButton.Left - 4;
        levelMeter.Location = new Point(contentRight - levelMeter.Width, top + 10);

        if (statusBadge.Visible)
        {
            statusBadge.Location = new Point(contentRight - statusBadge.Width, top + 4);
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
            AppLogger.Info(
                $"Start requested from UI. Mode={selectedMode}; Format={selectedFormat.ToDisplayName()}; SelectedMicrophone={(selectedMicrophoneDeviceName ?? "Windows default")} [{(selectedMicrophoneDeviceId ?? "<default>")}]; SelectedPlayback={(selectedPlaybackDeviceName ?? "Windows default")} [{(selectedPlaybackDeviceId ?? "<default>")}]");
            await captureService.StartAsync(selectedMode, selectedMicrophoneDeviceId, selectedPlaybackDeviceId, selectedFormat);
            timerLabel.Text = "00:00";
            uiTimer.Start();
            var systemDeviceText = selectedMode is CaptureMode.System or CaptureMode.Both
                ? captureService.ActiveSystemDeviceName ?? selectedPlaybackDeviceName ?? "the Windows default playback device"
                : null;
            var recordingDetails = systemDeviceText is null
                ? $"{selectedMode.ToDisplayName()} capture is recording with {captureService.ActiveMicrophoneDeviceName ?? selectedMicrophoneDeviceName ?? "the Windows default microphone"}."
                : $"{selectedMode.ToDisplayName()} capture is recording with {systemDeviceText}.";
            UpdateStatus("Recording", Color.FromArgb(255, 142, 168), recordingDetails);
        }
        catch (Exception ex)
        {
            AppLogger.Error("UI start recording failed.", ex);
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
        UpdateStatus("Saving", Color.FromArgb(255, 211, 122), "Finalizing the recording file and cleaning up the recording session.");

        try
        {
            AppLogger.Info("Stop requested from UI.");
            var info = await captureService.StopAsync();
            timerLabel.Text = "00:00";
            levelMeter.ResetMeter();

            // The RecordingInfo always reflects the actually saved state.
            var statusText = $"Saved {Path.GetFileName(info.FilePath)}";
            var statusDetails = statusText;
            if (info.WasRescuedToWav)
            {
                statusText = $"{statusText} (WAV)";
                statusDetails = "MP3 encoding failed. The recording was rescued as a WAV file.";
            }

            if (info.HadCaptureStopErrors)
            {
                statusDetails = $"{statusDetails} A capture source stopped with an error, but the recording was saved.";
            }

            UpdateStatus("Saved", Color.FromArgb(110, 230, 182), statusDetails);

            if (info.WasRescuedToWav || info.HadCaptureStopErrors)
            {
                MessageBox.Show(
                    this,
                    info.WasRescuedToWav
                        ? $"MP3 encoding failed. The recording was saved as a WAV file instead:\n{info.FilePath}"
                        : $"The recording was saved, but a capture source stopped with an error:\n{info.FilePath}",
                    "Recording Saved",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("UI stop recording failed.", ex);
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
        microphoneMenuItem.Enabled = !IsVisualRecording();
        systemAudioMenuItem.Enabled = !IsVisualRecording();
        formatMenuItem.Enabled = !IsVisualRecording();
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

        AppLogger.Info($"Capture mode changed to {selectedMode}.");
        UpdateModeText();
        UpdateStatus(null, default, null);
    }

    private void UpdateModeText()
    {
        modeLink.Text = selectedMode.ToDisplayName();
        var microphoneText = selectedMicrophoneDeviceName ?? "Windows default microphone";
        var systemText = selectedPlaybackDeviceName ?? "Windows default playback device";
        toolTip.SetToolTip(
            modeLink,
            $"{selectedMode.ToDisplayName()} capture. Click to switch between Both, Mic, and System. Current microphone: {microphoneText}. System audio: {systemText}. Output format: {selectedFormat.ToDisplayName()}.");
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
        AppLogger.Info($"Opening audio folder: {captureService.OutputDirectory}");
        Process.Start(new ProcessStartInfo(captureService.OutputDirectory) { UseShellExecute = true });
    }

    private void ShowAboutDialog()
    {
        AppLogger.Info("Opening About dialog.");
        using var dialog = new AboutDialog(GitHubUrl, captureService.LogDirectory);
        dialog.ShowDialog(this);
    }

    private void RefreshMicrophoneMenu()
    {
        microphoneMenuItem.DropDownItems.Clear();

        if (captureService.IsRecording || previewMode)
        {
            microphoneMenuItem.Text = "Microphone";
            microphoneMenuItem.DropDownItems.Add(new ToolStripMenuItem("Locked while recording") { Enabled = false });
            return;
        }

        IReadOnlyList<AudioDeviceInfo> microphones;
        try
        {
            microphones = AudioInputDeviceCatalog.GetMicrophones();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not enumerate microphones for the context menu.", ex);
            microphoneMenuItem.Text = "Microphone";
            microphoneMenuItem.DropDownItems.Add(new ToolStripMenuItem("Could not load microphones") { Enabled = false });
            return;
        }

        var defaultLabel = "Windows default";
        var effectiveSelectionId = selectedMicrophoneDeviceId;
        var effectiveSelectionName = selectedMicrophoneDeviceName;

        if (string.IsNullOrWhiteSpace(effectiveSelectionId))
        {
            var defaultMicrophone = microphones.FirstOrDefault(device => device.IsDefault);
            effectiveSelectionName = defaultMicrophone.DisplayName;
        }
        else
        {
            var selectedMicrophone = microphones.FirstOrDefault(device => string.Equals(device.Id, effectiveSelectionId, StringComparison.Ordinal));
            if (selectedMicrophone != default)
            {
                effectiveSelectionName = selectedMicrophone.DisplayName;
            }
        }

        microphoneMenuItem.DropDownItems.Add(CreateMicrophoneMenuItem(
            defaultLabel,
            isChecked: string.IsNullOrWhiteSpace(selectedMicrophoneDeviceId),
            onClick: () =>
            {
                selectedMicrophoneDeviceId = null;
                selectedMicrophoneDeviceName = null;
                AppLogger.Info("Microphone selection reset to Windows default.");
                SaveSettings();
                UpdateModeText();
            }));

        if (microphones.Count > 0)
        {
            microphoneMenuItem.DropDownItems.Add(new ToolStripSeparator());
        }

        var selectedDeviceMissing = !string.IsNullOrWhiteSpace(selectedMicrophoneDeviceId)
            && microphones.All(device => !string.Equals(device.Id, selectedMicrophoneDeviceId, StringComparison.Ordinal));

        if (selectedDeviceMissing)
        {
            microphoneMenuItem.DropDownItems.Add(new ToolStripMenuItem("Selected microphone is unavailable") { Enabled = false });
            microphoneMenuItem.DropDownItems.Add(new ToolStripSeparator());
        }

        foreach (var microphone in microphones)
        {
            var localMicrophone = microphone;
            microphoneMenuItem.DropDownItems.Add(CreateMicrophoneMenuItem(
                localMicrophone.MenuLabel,
                isChecked: string.Equals(localMicrophone.Id, selectedMicrophoneDeviceId, StringComparison.Ordinal),
                onClick: () =>
                {
                    selectedMicrophoneDeviceId = localMicrophone.Id;
                    selectedMicrophoneDeviceName = localMicrophone.DisplayName;
                    AppLogger.Info($"Microphone selection changed to {localMicrophone.DisplayName} [{localMicrophone.Id}]");
                    SaveSettings();
                    UpdateModeText();
                }));
        }

        if (microphones.Count == 0)
        {
            microphoneMenuItem.DropDownItems.Add(new ToolStripMenuItem("No active microphones found") { Enabled = false });
        }

        var headerText = effectiveSelectionName is { Length: > 0 }
            ? $"Microphone ({effectiveSelectionName})"
            : "Microphone";
        if (selectedDeviceMissing)
        {
            headerText = "Microphone (Unavailable)";
        }

        microphoneMenuItem.Text = headerText;
    }

    private static ToolStripMenuItem CreateMicrophoneMenuItem(string text, bool isChecked, Action onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Checked = isChecked,
            CheckOnClick = false,
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void RefreshSystemAudioMenu()
    {
        systemAudioMenuItem.DropDownItems.Clear();

        if (captureService.IsRecording || previewMode)
        {
            systemAudioMenuItem.Text = "System Audio";
            systemAudioMenuItem.DropDownItems.Add(new ToolStripMenuItem("Locked while recording") { Enabled = false });
            return;
        }

        IReadOnlyList<AudioDeviceInfo> playbackDevices;
        try
        {
            playbackDevices = AudioInputDeviceCatalog.GetPlaybackDevices();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not enumerate playback devices for the context menu.", ex);
            systemAudioMenuItem.Text = "System Audio";
            systemAudioMenuItem.DropDownItems.Add(new ToolStripMenuItem("Could not load playback devices") { Enabled = false });
            return;
        }

        var effectiveSelectionId = selectedPlaybackDeviceId;
        var effectiveSelectionName = selectedPlaybackDeviceName;

        if (string.IsNullOrWhiteSpace(effectiveSelectionId))
        {
            effectiveSelectionName = playbackDevices.FirstOrDefault(device => device.IsDefault).DisplayName;
        }
        else
        {
            var selectedDevice = playbackDevices.FirstOrDefault(device =>
                string.Equals(device.Id, effectiveSelectionId, StringComparison.Ordinal));
            if (selectedDevice != default)
            {
                effectiveSelectionName = selectedDevice.DisplayName;
            }
        }

        systemAudioMenuItem.DropDownItems.Add(CreateMicrophoneMenuItem(
            "Windows default",
            isChecked: string.IsNullOrWhiteSpace(selectedPlaybackDeviceId),
            onClick: () =>
            {
                selectedPlaybackDeviceId = null;
                selectedPlaybackDeviceName = null;
                AppLogger.Info("System audio selection reset to Windows default.");
                SaveSettings();
                UpdateModeText();
            }));

        if (playbackDevices.Count > 0)
        {
            systemAudioMenuItem.DropDownItems.Add(new ToolStripSeparator());
        }

        var selectedDeviceMissing = !string.IsNullOrWhiteSpace(selectedPlaybackDeviceId)
            && playbackDevices.All(device => !string.Equals(device.Id, selectedPlaybackDeviceId, StringComparison.Ordinal));

        if (selectedDeviceMissing)
        {
            systemAudioMenuItem.DropDownItems.Add(new ToolStripMenuItem("Selected playback device is unavailable") { Enabled = false });
            systemAudioMenuItem.DropDownItems.Add(new ToolStripSeparator());
        }

        foreach (var playbackDevice in playbackDevices)
        {
            var localDevice = playbackDevice;
            systemAudioMenuItem.DropDownItems.Add(CreateMicrophoneMenuItem(
                localDevice.MenuLabel,
                isChecked: string.Equals(localDevice.Id, selectedPlaybackDeviceId, StringComparison.Ordinal),
                onClick: () =>
                {
                    selectedPlaybackDeviceId = localDevice.Id;
                    selectedPlaybackDeviceName = localDevice.DisplayName;
                    AppLogger.Info($"System audio selection changed to {localDevice.DisplayName} [{localDevice.Id}]");
                    SaveSettings();
                    UpdateModeText();
                }));
        }

        if (playbackDevices.Count == 0)
        {
            systemAudioMenuItem.DropDownItems.Add(new ToolStripMenuItem("No active playback devices found") { Enabled = false });
        }

        var headerText = effectiveSelectionName is { Length: > 0 }
            ? $"System Audio ({effectiveSelectionName})"
            : "System Audio";
        if (selectedDeviceMissing)
        {
            headerText = "System Audio (Unavailable)";
        }

        systemAudioMenuItem.Text = headerText;
    }

    private void RefreshFormatMenu()
    {
        formatMenuItem.DropDownItems.Clear();

        foreach (var format in new[] { OutputFormat.Mp3, OutputFormat.Wav })
        {
            var localFormat = format;
            formatMenuItem.DropDownItems.Add(CreateMicrophoneMenuItem(
                localFormat.ToDisplayName(),
                isChecked: localFormat == selectedFormat,
                onClick: () =>
                {
                    selectedFormat = localFormat;
                    AppLogger.Info($"Output format changed to {localFormat.ToDisplayName()}.");
                    SaveSettings();
                    UpdateModeText();
                }));
        }

        formatMenuItem.Text = $"Format ({selectedFormat.ToDisplayName()})";
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
        if (control == recordButton || control == modeLink
            || control == minimizeButton || control == closeButton)
        {
            return;
        }

        control.MouseDown += HandleDragMouseDown;
        foreach (Control child in control.Controls)
        {
            AttachDragBehavior(child);
        }
    }

    private void HideToTray()
    {
        trayIcon ??= CreateTrayIcon();
        trayIcon.Visible = true;
        Hide();
        AppLogger.Info("Window hidden to the notification area.");
    }

    private void RestoreFromTray()
    {
        if (!Visible)
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
        }

        AppLogger.Info("Window restored from the notification area.");
    }

    private void DisposeTrayIcon()
    {
        if (trayIcon is null)
        {
            return;
        }

        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayIcon = null;
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => Close());

        var icon = new NotifyIcon
        {
            Icon = CreateTrayIconImage(),
            Text = "Simple Audio Recorder",
            ContextMenuStrip = menu,
            Visible = false,
        };
        icon.DoubleClick += (_, _) => RestoreFromTray();
        return icon;
    }

    private static Icon CreateTrayIconImage()
    {
        // Small application icon drawn in code so no asset file is needed:
        // purple disc with the red record dot, matching the main button.
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var disc = new SolidBrush(Color.FromArgb(101, 77, 245));
            using var dot = new SolidBrush(Color.FromArgb(255, 98, 129));
            graphics.FillEllipse(disc, 1, 1, 14, 14);
            graphics.FillEllipse(dot, 5, 5, 6, 6);
        }

        return Icon.FromHandle(bitmap.GetHicon());
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

    private enum WindowControlButtonKind
    {
        Minimize,
        Close,
    }

    private sealed class WindowControlButton : Control
    {
        private readonly WindowControlButtonKind kind;
        private bool hovered;

        public WindowControlButton(WindowControlButtonKind kind)
        {
            this.kind = kind;
            Size = new Size(22, 16);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ForeColor = Color.FromArgb(150, 158, 198);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
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
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var color = kind == WindowControlButtonKind.Close
                ? (hovered ? Color.FromArgb(255, 98, 129) : ForeColor)
                : (hovered ? Color.FromArgb(210, 216, 240) : ForeColor);

            var caption = kind == WindowControlButtonKind.Close ? "\u2715" : "\u2013";
            TextRenderer.DrawText(
                e.Graphics,
                caption,
                Font,
                ClientRectangle,
                color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
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
