using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace ELARA;

public sealed class MainForm : Form
{
    private const string GitHubUrl = "https://github.com/Azghul/ELARA";
    private const string UpstreamGitHubUrl = "https://github.com/SickPuppyCoding/SimpleAudioRecorder";
    private const float CardRadius = 18F;

    private readonly AudioCaptureService captureService = new();
    private readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 90 };
    private readonly ToolTip toolTip = new() { ShowAlways = true };
    private readonly ContextMenuStrip appMenu = new();
    private readonly ToolStripMenuItem microphoneMenuItem = new("Microphone");
    private readonly ToolStripMenuItem systemAudioMenuItem = new("System Audio");
    private readonly WindowControlButton optionsButton = new(WindowControlButtonKind.Options);
    private readonly WindowControlButton minimizeButton = new(WindowControlButtonKind.Minimize);
    private readonly WindowControlButton closeButton = new(WindowControlButtonKind.Close);
    private NotifyIcon? trayIcon;

    // Selector menus. Each selector uses its own standalone ContextMenuStrip so the
    // dropdown is always positioned relative to its control, never borrowed from the
    // tray/context-menu hierarchy (which caused menus to appear at the screen origin).
    private readonly ContextMenuStrip modeMenu = new();
    private readonly ContextMenuStrip microphoneMenu = new();
    private readonly ContextMenuStrip systemAudioMenu = new();
    private readonly Label timerLabel = new();
    private readonly Label titleHeader = new();
    private readonly Label versionLabel = new();
    private readonly Label modeFieldLabel = CreateFieldLabel("Mode");
    private readonly Label micFieldLabel = CreateFieldLabel("Microphone");
    private readonly Label systemFieldLabel = CreateFieldLabel("System Audio");
    private readonly Label outputFieldLabel = CreateFieldLabel("Save to");
    private readonly DarkSelector modeSelector = new();
    private readonly DarkSelector micSelector = new();
    private readonly DarkSelector systemSelector = new();
    private readonly DarkSelector outputPathSelector = new(interactive: false);
    private readonly FolderBrowseButton browseButton = new();
    private readonly LinkLabel openLink = new();
    private readonly StatusBadgeControl statusBadge = new();
    private readonly DotMeterControl levelMeter = new();
    private readonly RecordActionButton recordButton = new();
    private readonly Label diagnosticsFieldLabel = CreateFieldLabel("Diagnostics");
    private readonly Label diagnosticsLabel = new();

    private CaptureMode selectedMode = CaptureMode.Both;
    private string? selectedMicrophoneDeviceId;
    private string? selectedMicrophoneDeviceName;
    private string? selectedPlaybackDeviceId;
    private string? selectedPlaybackDeviceName;
    private OutputFormat selectedFormat = OutputFormat.Mp3;
    private bool previewMode;
    private bool previewRecording;
    private bool recordingOperationInProgress;
    private int diagnosticsProbeGeneration;
    private EndpointDiagnosticsReport? pendingDiagnosticsReport;
    private EndpointDiagnosticsReport? lastEndpointReport;

    public MainForm()
    {
        Text = "ELARA";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(432, 452);
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
        recordButton.Click += async (_, _) => await ToggleRecordingAsync();
        QueueDiagnosticsRefresh();
        AppLogger.Info("Main window initialized.");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // A diagnostics probe may finish before the window handle exists (for
        // example during construction); apply it now that marshalling is possible.
        if (pendingDiagnosticsReport is { } pending)
        {
            pendingDiagnosticsReport = null;
            ApplyEndpointDiagnostics(pending);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Never dispose the capture service while a start/stop/save operation is
        // still running; the close is simply retried by the user afterwards.
        if (recordingOperationInProgress)
        {
            e.Cancel = true;
            MessageBox.Show(
                this,
                "ELARA is currently starting or saving a recording. Please try again in a moment.",
                "Please Wait",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

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
            "A recording is still in progress.\n\n" +
            "Yes — stop the recording now and save it.\n" +
            "No — close ELARA. The app will still try to save the recording automatically.\n" +
            "Cancel — keep recording.",
            "Stop Recording",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (result == DialogResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        // Both Yes and No explicitly perform the announced save attempt; if saving
        // fails, the user must see the error (including the preserved raw-audio path)
        // before ELARA exits. AudioCaptureService keeps the raw PCM in that case.
        TryStopAndSaveRecordingBeforeClose();

        DisposeTrayIcon();
        captureService.Dispose();
        base.OnFormClosing(e);
    }

    private bool TryStopAndSaveRecordingBeforeClose()
    {
        try
        {
            // Run off the UI thread so encoding does not freeze the window while the
            // close sequence waits for the result.
            Task.Run(() => captureService.StopAsync()).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recording could not be saved during application close.", ex);
            MessageBox.Show(
                this,
                ex.Message,
                "Recording Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
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
        ApplyStaticDiagnostics();
        UpdateStatus(state.StatusText, state.StatusColor, state.StatusDetails);
        ApplyVisualState();
        Refresh();
    }

    private void BuildLayout()
    {
        titleHeader.AutoSize = true;
        titleHeader.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        titleHeader.ForeColor = Color.FromArgb(247, 248, 255);
        titleHeader.BackColor = Color.Transparent;
        titleHeader.Text = "ELARA";

        versionLabel.AutoSize = true;
        versionLabel.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        versionLabel.ForeColor = Color.FromArgb(150, 158, 198);
        versionLabel.BackColor = Color.Transparent;
        versionLabel.Text = AppVersion.DisplayLabel;

        timerLabel.AutoSize = false;
        timerLabel.Font = new Font("Cascadia Mono", 28F, FontStyle.Bold, GraphicsUnit.Point);
        timerLabel.ForeColor = Color.FromArgb(250, 251, 255);
        timerLabel.TextAlign = ContentAlignment.MiddleCenter;
        timerLabel.Text = "00:00";
        timerLabel.BackColor = Color.Transparent;

        openLink.AutoSize = true;
        openLink.Text = "Open Audio Files";
        openLink.ActiveLinkColor = Color.FromArgb(187, 176, 255);
        openLink.LinkColor = Color.FromArgb(145, 136, 255);
        openLink.VisitedLinkColor = openLink.LinkColor;
        openLink.LinkBehavior = LinkBehavior.HoverUnderline;
        openLink.BackColor = Color.Transparent;
        openLink.TabStop = false;
        openLink.Cursor = Cursors.Hand;
        openLink.LinkClicked += (_, _) => OpenAudioFolder();

        statusBadge.BackColor = Color.Transparent;
        levelMeter.BackColor = Color.Transparent;

        recordButton.Size = new Size(368, 46);
        recordButton.BackColor = Color.Transparent;

        diagnosticsFieldLabel.Text = "Diagnostics";
        diagnosticsLabel.AutoSize = false;
        diagnosticsLabel.Font = new Font("Cascadia Mono", 6.5F, FontStyle.Regular, GraphicsUnit.Point);
        diagnosticsLabel.ForeColor = Color.FromArgb(178, 185, 214);
        diagnosticsLabel.BackColor = Color.Transparent;

        minimizeButton.Click += (_, _) => HideToTray();
        closeButton.Click += (_, _) => Close();
        optionsButton.Click += (_, _) => ShowOptions();
        toolTip.SetToolTip(optionsButton, "Options");

        modeSelector.Click += (_, _) => OpenSelectorDropdown(modeSelector, modeMenu, RefreshModeMenu);
        micSelector.Click += (_, _) => OpenSelectorDropdown(micSelector, microphoneMenu, () => RefreshMicrophoneMenu(microphoneMenu));
        systemSelector.Click += (_, _) => OpenSelectorDropdown(systemSelector, systemAudioMenu, () => RefreshSystemAudioMenu(systemAudioMenu));
        browseButton.Click += (_, _) => BrowseForOutputFolder();

        Controls.Add(titleHeader);
        Controls.Add(versionLabel);
        Controls.Add(timerLabel);
        Controls.Add(statusBadge);
        Controls.Add(levelMeter);
        Controls.Add(modeFieldLabel);
        Controls.Add(micFieldLabel);
        Controls.Add(systemFieldLabel);
        Controls.Add(outputFieldLabel);
        Controls.Add(modeSelector);
        Controls.Add(micSelector);
        Controls.Add(systemSelector);
        Controls.Add(outputPathSelector);
        Controls.Add(browseButton);
        Controls.Add(recordButton);
        Controls.Add(diagnosticsFieldLabel);
        Controls.Add(diagnosticsLabel);
        Controls.Add(openLink);
        Controls.Add(minimizeButton);
        Controls.Add(closeButton);
        Controls.Add(optionsButton);

        Icon = AppIcon.Load();

        LayoutCompactControls();
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(150, 158, 198),
            BackColor = Color.Transparent,
        };
    }

    private void OpenSelectorDropdown(Control anchor, ToolStripDropDown dropDown, Action refresh)
    {
        if (IsVisualRecording())
        {
            return;
        }

        // Show the dropdown control-relative: below the selector, anchored to the
        // selector itself. The menu is repositioned relative to its anchor control,
        // so it is correct on any monitor, after the window moved, and at any DPI.
        // Never mix client coordinates with screen coordinates here.
        refresh();
        dropDown.Show(anchor, new Point(0, anchor.Height + 2));
    }

    private void RefreshModeMenu()
    {
        modeMenu.Items.Clear();

        foreach (var mode in new[] { CaptureMode.Both, CaptureMode.Microphone, CaptureMode.System })
        {
            var localMode = mode;
            modeMenu.Items.Add(CreateMicrophoneMenuItem(
                localMode.ToDisplayName(),
                isChecked: localMode == selectedMode,
                onClick: () =>
                {
                    selectedMode = localMode;
                    AppLogger.Info($"Capture mode changed to {localMode.ToDisplayName()}.");
                    UpdateModeText();
                    UpdateStatus(null, default, null);
                    QueueDiagnosticsRefresh();
                }));
        }
    }

    private void BuildContextMenu()
    {
        microphoneMenuItem.DropDownOpening += (_, _) => RefreshMicrophoneMenu(microphoneMenuItem.DropDown);
        systemAudioMenuItem.DropDownOpening += (_, _) => RefreshSystemAudioMenu(systemAudioMenuItem.DropDown);

        appMenu.Items.Add("About App", null, (_, _) => ShowAboutDialog());
        appMenu.Items.Add("View Audio Files", null, (_, _) => OpenAudioFolder());
        appMenu.Items.Add(microphoneMenuItem);
        appMenu.Items.Add(systemAudioMenuItem);
        appMenu.Items.Add(new ToolStripSeparator());
        appMenu.Items.Add("Exit", null, (_, _) => Close());

        RefreshMicrophoneMenu(microphoneMenuItem.DropDown);
        RefreshSystemAudioMenu(systemAudioMenuItem.DropDown);
    }

    private void LoadSettings()
    {
        var settings = AppSettings.Load();
        selectedFormat = settings.ResolveOutputFormat();

        if (string.IsNullOrWhiteSpace(settings.CustomOutputDirectory))
        {
            captureService.CustomOutputDirectory = null;
        }
        else
        {
            try
            {
                Directory.CreateDirectory(settings.CustomOutputDirectory);
                captureService.CustomOutputDirectory = settings.CustomOutputDirectory;
            }
            catch (Exception ex)
            {
                captureService.CustomOutputDirectory = null;
                AppLogger.Warn(
                    $"Saved output directory '{settings.CustomOutputDirectory}' could not be created; falling back to the default recording directory. {ex.Message}");
            }
        }

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
                    // Keep the saved selection instead of silently switching to the
                    // Windows default: a recording must never use a device other than
                    // the one the user chose. Starting will fail with a clear error
                    // until the device returns or another microphone is selected.
                    selectedMicrophoneDeviceId = settings.SelectedMicrophoneDeviceId;
                    selectedMicrophoneDeviceName = null;
                    AppLogger.Warn(
                        $"Saved microphone '{settings.SelectedMicrophoneDeviceId}' is currently not available; the recording will not start until the device returns or a different microphone is selected.");
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
                    // Same rule as for the microphone: never silently fall back to the
                    // Windows default playback device.
                    selectedPlaybackDeviceId = settings.SelectedPlaybackDeviceId;
                    selectedPlaybackDeviceName = null;
                    AppLogger.Warn(
                        $"Saved playback device '{settings.SelectedPlaybackDeviceId}' is currently not available; the recording will not start until the device returns or a different device is selected.");
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
            CustomOutputDirectory = captureService.CustomOutputDirectory,
        }.Save();
    }

    private void LayoutCompactControls()
    {
        closeButton.Location = new Point(ClientSize.Width - 28, 8);
        minimizeButton.Location = new Point(closeButton.Left - 24, 8);
        optionsButton.Location = new Point(minimizeButton.Left - 24, 8);
        titleHeader.Location = new Point(18, 8);
        versionLabel.Location = new Point(18, 26);

        // Status badge sits between the title and the window control buttons.
        statusBadge.Location = new Point(optionsButton.Left - 8 - statusBadge.Width, 6);

        timerLabel.Location = new Point(20, 46);
        timerLabel.Size = new Size(ClientSize.Width - 40, 44);

        levelMeter.Location = new Point((ClientSize.Width - levelMeter.Width) / 2, 96);

        var selectorLeft = 126;
        var selectorWidth = ClientSize.Width - selectorLeft - 22;
        var rowFields = new[] { (modeSelector, modeFieldLabel), (micSelector, micFieldLabel), (systemSelector, systemFieldLabel) };
        var rowTop = 116;
        foreach (var (selector, fieldLabel) in rowFields)
        {
            selector.SetBounds(selectorLeft, rowTop, selectorWidth, 26);
            fieldLabel.Location = new Point(22, rowTop + 5);
            rowTop += 34;
        }

        var outputRowTop = rowTop;
        outputPathSelector.SetBounds(selectorLeft, outputRowTop, selectorWidth - 42, 26);
        browseButton.Location = new Point(selectorLeft + selectorWidth - 36, outputRowTop);
        outputFieldLabel.Location = new Point(22, outputRowTop + 5);

        recordButton.Location = new Point(32, outputRowTop + 42);
        recordButton.Width = ClientSize.Width - 64;

        diagnosticsFieldLabel.Location = new Point(22, recordButton.Bottom + 8);
        diagnosticsLabel.SetBounds(22, recordButton.Bottom + 26, ClientSize.Width - 44, 92);

        openLink.Location = new Point((ClientSize.Width - openLink.PreferredWidth) / 2, ClientSize.Height - 24);
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

    private readonly record struct SaveAsSelection(OutputFormat Format, string? OutputPath);

    private async Task StartRecordingAsync()
    {
        recordButton.Enabled = false;
        recordingOperationInProgress = true;

        try
        {
            // Save As comes BEFORE the recording starts: when the user cancels the
            // dialog the recording does not start, no file is created and the state
            // stays Ready.
            var target = AskForSaveTarget();
            if (target.OutputPath is null)
            {
                AppLogger.Info("Recording start canceled by the user in the Save As dialog.");
                return;
            }

            selectedFormat = target.Format;
            UpdateStatus("Preparing", Color.FromArgb(255, 211, 122), "Opening the Windows audio devices and preparing the recording file.");
            AppLogger.Info(
                $"Start requested from UI. Mode={selectedMode}; Format={selectedFormat.ToDisplayName()}; SelectedMicrophone={(selectedMicrophoneDeviceName ?? "Windows default")} [{(selectedMicrophoneDeviceId ?? "<default>")}]; SelectedPlayback={(selectedPlaybackDeviceName ?? "Windows default")} [{(selectedPlaybackDeviceId ?? "<default>")}]; OutputFile={target.OutputPath}");
            // Device enumeration and COM initialization can block for several seconds;
            // keep them off the UI thread so the window stays responsive.
            await Task.Run(() => captureService.StartAsync(
                selectedMode,
                selectedMicrophoneDeviceId,
                selectedPlaybackDeviceId,
                target.Format,
                target.OutputPath));
            timerLabel.Text = "00:00";
            uiTimer.Start();
            var systemDeviceText = selectedMode is CaptureMode.System or CaptureMode.Both
                ? captureService.ActiveSystemDeviceName ?? selectedPlaybackDeviceName ?? "the Windows default playback device"
                : null;
            var recordingDetails = systemDeviceText is null
                ? $"{selectedMode.ToDisplayName()} capture is recording with {captureService.ActiveMicrophoneDeviceName ?? selectedMicrophoneDeviceName ?? "the Windows default microphone"}."
                : $"{selectedMode.ToDisplayName()} capture is recording with {systemDeviceText}.";
            if (selectedMode is CaptureMode.Microphone or CaptureMode.Both
                && captureService.ActiveMicrophoneRawActivated == false)
            {
                recordingDetails += " RAW was not accepted; Windows shared-mode processing may be active.";
            }

            UpdateStatus("Recording", Color.FromArgb(255, 142, 168), recordingDetails);
            ApplyRecordingDiagnostics();
        }
        catch (Exception ex)
        {
            AppLogger.Error("UI start recording failed.", ex);
            UpdateStatus("Blocked", Color.FromArgb(255, 196, 120), ex.Message);
            QueueDiagnosticsRefresh();
            MessageBox.Show(this, ex.Message, "Audio Capture Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            recordingOperationInProgress = false;
            ApplyVisualState();
        }
    }

    private SaveAsSelection AskForSaveTarget()
    {
        // The Save As dialog opens in the configured Save-to folder and preselects
        // the most recently used output format as the default filter.
        var currentDirectory = captureService.CustomOutputDirectory;
        if (string.IsNullOrWhiteSpace(currentDirectory))
        {
            currentDirectory = AppPaths.RecordingDirectory;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save Recording As",
            InitialDirectory = currentDirectory,
            FileName = OutputPathUtility.DefaultRecordingFileName(DateTime.Now),
            Filter = "MP3 Audio - Mono 48 kHz 128 kbps (*.mp3)|*.mp3|WAV Audio - PCM16 Mono 48 kHz (*.wav)|*.wav",
            FilterIndex = selectedFormat == OutputFormat.Mp3 ? 1 : 2,
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = selectedFormat == OutputFormat.Mp3 ? "mp3" : "wav",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return new SaveAsSelection(selectedFormat, null);
        }

        // The format is derived from the selected filter; the extension is forced to
        // match so the saved file always has the extension of the chosen format.
        var chosenFormat = dialog.FilterIndex == 2 ? OutputFormat.Wav : OutputFormat.Mp3;
        var selectedPath = dialog.FileName;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return new SaveAsSelection(selectedFormat, null);
        }

        var outputPath = OutputPathUtility.EnsureExtensionMatches(selectedPath, chosenFormat);

        // Remember the last used format so the next Save As preselects the same filter.
        selectedFormat = chosenFormat;
        SaveSettings();
        QueueDiagnosticsRefresh();

        // A different folder chosen in the Save As dialog becomes the new default
        // Save-to folder (persisted); Open Audio Files follows it automatically.
        var chosenDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(chosenDirectory)
            && !string.Equals(chosenDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            ApplyOutputDirectorySelection(chosenDirectory);
        }

        AppLogger.Info($"Save As target chosen. Format={chosenFormat.ToDisplayName()}; File={outputPath}");
        return new SaveAsSelection(chosenFormat, outputPath);
    }

    private async Task StopRecordingAsync()
    {
        recordButton.Enabled = false;
        UpdateStatus("Saving", Color.FromArgb(255, 211, 122), "Finalizing the recording file and cleaning up the recording session.");
        recordingOperationInProgress = true;

        try
        {
            AppLogger.Info("Stop requested from UI.");
            // Stopping, gain analysis and MP3/WAV encoding can take a while for long
            // recordings; keep them off the UI thread so the window stays responsive.
            var info = await Task.Run(() => captureService.StopAsync());
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
            else if (info.WasRescuedToMp3)
            {
                statusText = $"{statusText} (MP3)";
                statusDetails = "WAV encoding failed (the recording is too long for the WAV format). The recording was rescued as an MP3 file.";
            }

            if (info.HadCaptureStopErrors)
            {
                statusDetails = $"{statusDetails} A capture source stopped with an error, but the recording was saved.";
            }

            UpdateStatus("Saved", Color.FromArgb(110, 230, 182), statusDetails);

            if (info.WasRescuedToWav || info.WasRescuedToMp3 || info.HadCaptureStopErrors)
            {
                MessageBox.Show(
                    this,
                    info.WasRescuedToWav
                        ? $"MP3 encoding failed. The recording was saved as a WAV file instead:\n{info.FilePath}"
                        : info.WasRescuedToMp3
                            ? $"WAV encoding failed. The recording was saved as an MP3 file instead:\n{info.FilePath}"
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
            recordingOperationInProgress = false;
            ApplyVisualState();
            QueueDiagnosticsRefresh();
        }
    }

    private void ApplyVisualState()
    {
        recordButton.IsRecording = IsVisualRecording();
        recordButton.Enabled = previewMode || !captureService.IsRecording || recordButton.IsRecording;

        var selectionLocked = IsVisualRecording();
        modeSelector.Enabled = !selectionLocked;
        micSelector.Enabled = !selectionLocked;
        systemSelector.Enabled = !selectionLocked;
        outputPathSelector.Enabled = !selectionLocked;
        browseButton.Enabled = !selectionLocked;
        optionsButton.Enabled = !selectionLocked;
        microphoneMenuItem.Enabled = !selectionLocked;
        systemAudioMenuItem.Enabled = !selectionLocked;
        UpdateModeText();
    }

    private void UpdateModeText()
    {
        modeSelector.Text = selectedMode.ToDisplayName();
        micSelector.Text = selectedMicrophoneDeviceName
            ?? (selectedMicrophoneDeviceId is null
                ? "Windows default microphone"
                : "Selected microphone (unavailable)");
        systemSelector.Text = selectedPlaybackDeviceName
            ?? (selectedPlaybackDeviceId is null
                ? "Windows default playback device"
                : "Selected playback device (unavailable)");

        toolTip.SetToolTip(
            modeSelector,
            $"Capture mode: {selectedMode.ToDisplayName()}. Both mixes system audio and microphone to mono, Mic records only the microphone, System only system audio.");
        toolTip.SetToolTip(micSelector, $"Microphone: {micSelector.Text}");
        toolTip.SetToolTip(systemSelector, $"System audio: {systemSelector.Text}");
        toolTip.SetToolTip(
            optionsButton,
            "Options. Format and file name are chosen when you start a recording.");
        var outputDirectory = captureService.CustomOutputDirectory ?? AppPaths.RecordingDirectory;
        outputPathSelector.Text = outputDirectory;
        toolTip.SetToolTip(outputPathSelector, $"Recordings are saved to: {outputDirectory}");
        toolTip.SetToolTip(browseButton, "Choose the folder where recordings are saved.");
    }

    private void QueueDiagnosticsRefresh()
    {
        if (previewMode)
        {
            ApplyStaticDiagnostics();
            return;
        }

        var generation = ++diagnosticsProbeGeneration;
        var probePlayback = selectedMode == CaptureMode.System;
        var preferredDeviceId = probePlayback ? selectedPlaybackDeviceId : selectedMicrophoneDeviceId;

        // The probe touches COM and the device topology; run it off the UI thread
        // and drop stale results when the selection changed in the meantime.
        Task.Run(() => probePlayback
            ? AudioEndpointDiagnostics.GetPlayback(preferredDeviceId)
            : AudioEndpointDiagnostics.GetMicrophone(preferredDeviceId))
            .ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion
                    || generation != diagnosticsProbeGeneration)
                {
                    return;
                }

                try
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    if (IsHandleCreated)
                    {
                        BeginInvoke(new Action(() => ApplyEndpointDiagnostics(task.Result)));
                    }
                    else
                    {
                        pendingDiagnosticsReport = task.Result;
                    }
                }
                catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                {
                }
            });
    }

    private void ApplyEndpointDiagnostics(EndpointDiagnosticsReport report)
    {
        lastEndpointReport = report;
        diagnosticsLabel.Text = BuildPreFlightDiagnosticsText(report);
        toolTip.SetToolTip(diagnosticsLabel, BuildPreFlightDiagnosticsTooltip(report));
    }

    private string BuildPreFlightDiagnosticsText(EndpointDiagnosticsReport report)
    {
        var lines = new List<string> { "Audio" };

        if (report.IsAvailable)
        {
            lines.Add($"Input: {Truncate(report.FriendlyName ?? "<unknown>", 44)}");
            if (report.Transport != AudioEndpointTransport.Unknown)
            {
                lines.Add($"Connection: {report.Transport.ToDisplayName()}");
            }
        }
        else
        {
            lines.Add("Input: unavailable — recording will be blocked");
        }

        if (selectedMode == CaptureMode.Both)
        {
            lines.Add($"System: {Truncate(selectedPlaybackDeviceName ?? "Windows default playback device", 40)}");
        }

        lines.Add($"Signal: {DescribeSignalFormat(report)}");
        lines.Add($"Capture: {DescribePlannedCaptureState()}");
        lines.Add($"Output: {DescribeOutputProfile(selectedFormat)}");

        var warning = report.BuildQualityWarning();
        if (warning is not null)
        {
            lines.Add($"(!) {warning}");
        }
        else if (report.IsAvailable && EndpointQualityClassifier.IsFullBandwidth(report.MixSampleRate))
        {
            lines.Add("Quality: OK");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildPreFlightDiagnosticsTooltip(EndpointDiagnosticsReport report)
    {
        var details = new List<string>
        {
            $"Selected device: {(report.IsWindowsDefault ? "Windows default" : report.PreferredDeviceId ?? "<none>")}",
            $"Used endpoint: {(report.IsAvailable ? report.FriendlyName : "unavailable")}",
            $"Endpoint ID: {report.EndpointId ?? "<n/a>"}",
            $"Endpoint state: {report.DeviceState ?? "<n/a>"}",
            $"Data flow: {report.DataFlow}; {report.RoleDescription}",
            $"Transport: {report.Transport.ToDisplayName()}",
            $"Form factor: {report.FormFactor ?? "<unknown>"}",
            $"Native/mix format: {(report.IsAvailable ? $"{report.MixSampleRate} Hz, {report.MixChannels} channel(s), {report.MixBitsPerSample}-bit, {report.MixFormatDescription ?? "unknown"}{(report.MixChannelMask is { } mask ? $", channel mask 0x{mask:X8}" : string.Empty)}" : "n/a")}",
            $"Capture request: {RecordingOutputProfile.SampleRateKhz} mono, RAW stream options (Windows adaptive processing bypassed when accepted)",
            $"Output: {DescribeOutputProfile(selectedFormat)}, encoded once from 16-bit PCM",
        };
        if (report.Error is not null)
        {
            details.Add($"Probe error: {report.Error}");
        }

        return string.Join(Environment.NewLine, details);
    }

    private static string DescribeSignalFormat(EndpointDiagnosticsReport report)
    {
        if (!report.IsAvailable || report.MixSampleRate <= 0)
        {
            return "n/a";
        }

        return $"{report.MixSampleRate / 1000D:0.#} kHz · {DescribeChannels(report.MixChannels)}";
    }

    private static string DescribeChannels(short channels)
    {
        return channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            <= 0 => "n/a",
            _ => $"{channels} ch",
        };
    }

    private string DescribePlannedCaptureState()
    {
        return selectedMode == CaptureMode.System
            ? "48 kHz · Mono · Loopback"
            : "48 kHz · Mono · RAW";
    }

    private static string DescribeOutputProfile(OutputFormat format)
    {
        return format == OutputFormat.Mp3
            ? $"MP3 · {RecordingOutputProfile.SampleRateKhz} · Mono · {RecordingOutputProfile.Mp3BitRateKbps} kbps"
            : $"WAV · {RecordingOutputProfile.SampleRateKhz} · Mono · PCM16";
    }

    private void ApplyRecordingDiagnostics()
    {
        var report = lastEndpointReport;
        var lines = new List<string> { "Audio" };

        if (selectedMode is CaptureMode.Microphone or CaptureMode.Both)
        {
            lines.Add($"Input: {Truncate(captureService.ActiveMicrophoneDeviceName ?? "<unknown>", 44)}");
            if (report is { IsAvailable: true } && report.Transport != AudioEndpointTransport.Unknown)
            {
                lines.Add($"Connection: {report.Transport.ToDisplayName()}");
            }
        }

        if (selectedMode is CaptureMode.System or CaptureMode.Both)
        {
            lines.Add($"System: {Truncate(captureService.ActiveSystemDeviceName ?? "<unknown>", 44)}");
        }

        lines.Add($"Signal: {(report is { IsAvailable: true } ? DescribeSignalFormat(report) : "n/a")}");
        lines.Add(
            $"Capture: {DescribeCaptureState(captureService.ActiveMicrophoneFormat, captureService.ActiveMicrophoneRawActivated, isLoopback: selectedMode == CaptureMode.System)}");
        lines.Add($"Output: {DescribeOutputProfile(selectedFormat)}");

        var warning = report?.BuildQualityWarning();
        if (warning is not null)
        {
            lines.Add($"(!) {warning}");
        }
        else if (report is { IsAvailable: true } && EndpointQualityClassifier.IsFullBandwidth(report.MixSampleRate))
        {
            lines.Add("Quality: OK");
        }

        diagnosticsLabel.Text = string.Join(Environment.NewLine, lines);

        var details = new List<string>
        {
            $"Selected device: {(report is { IsWindowsDefault: false } ? report.PreferredDeviceId ?? "<none>" : "Windows default")}",
            $"Used endpoint: {(report?.IsAvailable == true ? report.FriendlyName : captureService.ActiveMicrophoneDeviceName ?? "unavailable")}",
            $"Endpoint ID: {report?.EndpointId ?? captureService.ActiveMicrophoneDeviceId ?? "<n/a>"}",
            $"Endpoint state: {report?.DeviceState ?? "<n/a>"}",
            $"Data flow: {report?.DataFlow ?? "<n/a>"}; {report?.RoleDescription ?? "<n/a>"}",
            $"Transport detection: {report?.Transport.ToDisplayName() ?? "Unknown"} (PKEY_Device_EnumeratorName)",
            $"Form factor: {report?.FormFactor ?? "<unknown>"}",
            $"Native/mix format: {(report is { IsAvailable: true } ? $"{report.MixSampleRate} Hz, {report.MixChannels} channel(s), {report.MixBitsPerSample}-bit, {report.MixFormatDescription ?? "unknown"}{(report.MixChannelMask is { } mask ? $", channel mask 0x{mask:X8}" : string.Empty)}" : "n/a")}",
            $"Initialized capture format: {captureService.ActiveMicrophoneFormat?.Description ?? "<n/a>"}",
            $"RAW: Requested={(captureService.ActiveMicrophoneRawActivated is not null)}; Activated={captureService.ActiveMicrophoneRawActivated?.ToString() ?? "<n/a>"}",
            $"Output: {DescribeOutputProfile(selectedFormat)} encoded once from 16-bit PCM",
        };
        toolTip.SetToolTip(diagnosticsLabel, string.Join(Environment.NewLine, details));
    }

    private static string DescribeCaptureState(CapturedStreamFormat? format, bool? rawActivated, bool isLoopback)
    {
        if (format is not { } activeFormat)
        {
            return isLoopback
                ? "48 kHz · Mono · Loopback"
                : "48 kHz · Mono · RAW";
        }

        var rate = $"{activeFormat.SampleRate / 1000D:0.#} kHz";
        var channels = activeFormat.Channels == 1 ? "Mono" : $"{activeFormat.Channels} ch";
        if (isLoopback)
        {
            return $"{rate} · {channels} · Loopback";
        }

        return rawActivated switch
        {
            true => $"{rate} · {channels} · RAW",
            false => $"{rate} · {channels} · Shared (Windows processing may be active)",
            _ => $"{rate} · {channels}",
        };
    }

    private void ApplyStaticDiagnostics()
    {
        diagnosticsLabel.Text = $"Output: {DescribeOutputProfile(selectedFormat)}";
        toolTip.SetToolTip(diagnosticsLabel, "Diagnostics are available when the app runs normally.");
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
    }

    private void UpdateStatus(string? text, Color color, string? details)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            statusBadge.Caption = "Ready";
            statusBadge.AccentColor = Color.FromArgb(150, 136, 255);
            toolTip.SetToolTip(statusBadge, "Idle. Choose mode and devices, then start recording; the file name and format are chosen before recording starts.");
        }
        else
        {
            statusBadge.Caption = text;
            statusBadge.AccentColor = color;
            toolTip.SetToolTip(statusBadge, details ?? text);
        }

        statusBadge.Visible = true;
        levelMeter.Visible = true;
        statusBadge.Invalidate();
        LayoutCompactControls();
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

    private void RefreshMicrophoneMenu(ToolStripDropDown target)
    {
        target.Items.Clear();

        if (captureService.IsRecording || previewMode)
        {
            microphoneMenuItem.Text = "Microphone";
            target.Items.Add(new ToolStripMenuItem("Locked while recording") { Enabled = false });
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
            target.Items.Add(new ToolStripMenuItem("Could not load microphones") { Enabled = false });
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

        target.Items.Add(CreateMicrophoneMenuItem(
            defaultLabel,
            isChecked: string.IsNullOrWhiteSpace(selectedMicrophoneDeviceId),
            onClick: () =>
            {
                selectedMicrophoneDeviceId = null;
                selectedMicrophoneDeviceName = null;
                AppLogger.Info("Microphone selection reset to Windows default.");
                SaveSettings();
                UpdateModeText();
                QueueDiagnosticsRefresh();
            }));

        if (microphones.Count > 0)
        {
            target.Items.Add(new ToolStripSeparator());
        }

        var selectedDeviceMissing = !string.IsNullOrWhiteSpace(selectedMicrophoneDeviceId)
            && microphones.All(device => !string.Equals(device.Id, selectedMicrophoneDeviceId, StringComparison.Ordinal));

        if (selectedDeviceMissing)
        {
            target.Items.Add(new ToolStripMenuItem("Selected microphone is unavailable") { Enabled = false });
            target.Items.Add(new ToolStripSeparator());
        }

        foreach (var microphone in microphones)
        {
            var localMicrophone = microphone;
            target.Items.Add(CreateMicrophoneMenuItem(
                localMicrophone.MenuLabel,
                isChecked: string.Equals(localMicrophone.Id, selectedMicrophoneDeviceId, StringComparison.Ordinal),
                onClick: () =>
                {
                    selectedMicrophoneDeviceId = localMicrophone.Id;
                    selectedMicrophoneDeviceName = localMicrophone.DisplayName;
                    AppLogger.Info($"Microphone selection changed to {localMicrophone.DisplayName} [{localMicrophone.Id}]");
                    SaveSettings();
                    UpdateModeText();
                    QueueDiagnosticsRefresh();
                }));
        }

        if (microphones.Count == 0)
        {
            target.Items.Add(new ToolStripMenuItem("No active microphones found") { Enabled = false });
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

    private void RefreshSystemAudioMenu(ToolStripDropDown target)
    {
        target.Items.Clear();

        if (captureService.IsRecording || previewMode)
        {
            systemAudioMenuItem.Text = "System Audio";
            target.Items.Add(new ToolStripMenuItem("Locked while recording") { Enabled = false });
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
            target.Items.Add(new ToolStripMenuItem("Could not load playback devices") { Enabled = false });
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

        target.Items.Add(CreateMicrophoneMenuItem(
            "Windows default",
            isChecked: string.IsNullOrWhiteSpace(selectedPlaybackDeviceId),
            onClick: () =>
            {
                selectedPlaybackDeviceId = null;
                selectedPlaybackDeviceName = null;
                AppLogger.Info("System audio selection reset to Windows default.");
                SaveSettings();
                UpdateModeText();
                QueueDiagnosticsRefresh();
            }));

        if (playbackDevices.Count > 0)
        {
            target.Items.Add(new ToolStripSeparator());
        }

        var selectedDeviceMissing = !string.IsNullOrWhiteSpace(selectedPlaybackDeviceId)
            && playbackDevices.All(device => !string.Equals(device.Id, selectedPlaybackDeviceId, StringComparison.Ordinal));

        if (selectedDeviceMissing)
        {
            target.Items.Add(new ToolStripMenuItem("Selected playback device is unavailable") { Enabled = false });
            target.Items.Add(new ToolStripSeparator());
        }

        foreach (var playbackDevice in playbackDevices)
        {
            var localDevice = playbackDevice;
            target.Items.Add(CreateMicrophoneMenuItem(
                localDevice.MenuLabel,
                isChecked: string.Equals(localDevice.Id, selectedPlaybackDeviceId, StringComparison.Ordinal),
                onClick: () =>
                {
                    selectedPlaybackDeviceId = localDevice.Id;
                    selectedPlaybackDeviceName = localDevice.DisplayName;
                    AppLogger.Info($"System audio selection changed to {localDevice.DisplayName} [{localDevice.Id}]");
                    SaveSettings();
                    UpdateModeText();
                    QueueDiagnosticsRefresh();
                }));
        }

        if (playbackDevices.Count == 0)
        {
            target.Items.Add(new ToolStripMenuItem("No active playback devices found") { Enabled = false });
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

    private void BrowseForOutputFolder()
    {
        if (IsVisualRecording())
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder where new recordings are saved.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        var current = captureService.CustomOutputDirectory;
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
        {
            dialog.SelectedPath = current;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        ApplyOutputDirectorySelection(dialog.SelectedPath);
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
        if (control == recordButton || control == minimizeButton || control == closeButton
            || control == optionsButton
            || control == openLink
            || control == modeSelector || control == micSelector
            || control == systemSelector
            || control == outputPathSelector || control == browseButton)
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

    private void ShowOptions()
    {
        AppLogger.Info("Opening Options dialog.");
        using var dialog = new OptionsDialog(
            captureService.CustomOutputDirectory,
            AppPaths.RecordingDirectory,
            onOutputDirectoryChanged: ApplyOutputDirectorySelection,
            openAudioFiles: OpenAudioFolder,
            openLogFiles: OpenLogFiles);
        dialog.ShowDialog(this);
    }

    private void ApplyOutputDirectorySelection(string? selectedPath)
    {
        captureService.CustomOutputDirectory = string.IsNullOrWhiteSpace(selectedPath) ? null : selectedPath;
        SaveSettings();
        AppLogger.Info($"Recording output directory set to: {captureService.CustomOutputDirectory ?? "<default>"}");
        UpdateModeText();
    }

    private void OpenLogFiles()
    {
        Directory.CreateDirectory(captureService.LogDirectory);
        AppLogger.Info($"Opening log folder: {captureService.LogDirectory}");
        Process.Start(new ProcessStartInfo(captureService.LogDirectory) { UseShellExecute = true });
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
            Icon = AppIcon.Load(),
            Text = "ELARA",
            ContextMenuStrip = menu,
            Visible = false,
        };
        icon.DoubleClick += (_, _) => RestoreFromTray();
        return icon;
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
        Options,
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
            var color = kind switch
            {
                WindowControlButtonKind.Close => hovered ? Color.FromArgb(255, 98, 129) : ForeColor,
                WindowControlButtonKind.Options => hovered ? Color.FromArgb(178, 161, 255) : ForeColor,
                _ => hovered ? Color.FromArgb(210, 216, 240) : ForeColor,
            };

            var caption = kind switch
            {
                WindowControlButtonKind.Close => "\u2715",
                WindowControlButtonKind.Options => "\u2699",
                _ => "\u2013",
            };
            TextRenderer.DrawText(
                e.Graphics,
                caption,
                Font,
                ClientRectangle,
                color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private sealed class DarkSelector : Control
    {
        private readonly bool interactive;
        private bool hovered;

        public DarkSelector(bool interactive = true)
        {
            this.interactive = interactive;
            Size = new Size(284, 26);
            Cursor = interactive ? Cursors.Hand : Cursors.Default;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            ForeColor = Color.FromArgb(235, 238, 248);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
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

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var path = CreateRoundedRectangle(ClientRectangle, 8F);
            using var fill = new SolidBrush(Enabled && hovered ? Color.FromArgb(31, 37, 68) : Color.FromArgb(24, 29, 56));
            using var border = new Pen(Enabled
                ? (hovered ? Color.FromArgb(122, 99, 255) : Color.FromArgb(49, 57, 96))
                : Color.FromArgb(38, 44, 74));

            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            var textColor = Enabled ? ForeColor : Color.FromArgb(105, 111, 146);
            var textRect = new Rectangle(10, 0, Width - 34, Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var chevronColor = Enabled ? Color.FromArgb(150, 136, 255) : Color.FromArgb(90, 96, 128);
            var chevronRect = new Rectangle(Width - 26, 0, 18, Height);
            if (interactive)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "\u25BE",
                    Font,
                    chevronRect,
                    chevronColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    private sealed class FolderBrowseButton : Control
    {
        private bool hovered;

        public FolderBrowseButton()
        {
            Size = new Size(36, 26);
            Cursor = Cursors.Hand;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
                true);
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

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var path = CreateRoundedRectangle(ClientRectangle, 8F);
            using var fill = new SolidBrush(Enabled && hovered ? Color.FromArgb(31, 37, 68) : Color.FromArgb(24, 29, 56));
            using var border = new Pen(Enabled
                ? (hovered ? Color.FromArgb(122, 99, 255) : Color.FromArgb(49, 57, 96))
                : Color.FromArgb(38, 44, 74));

            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            var color = Enabled ? Color.FromArgb(150, 158, 198) : Color.FromArgb(90, 96, 128);
            using var brush = new SolidBrush(color);
            // Simple folder glyph: back tab + body.
            e.Graphics.FillRectangle(brush, 10, 8, 8, 3);
            e.Graphics.FillRectangle(brush, 10, 10, 16, 9);
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
            Size = new Size(368, 46);
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

            var text = isRecording ? "Stop" : "Start Recording";
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
