namespace ELARA;

/// <summary>
/// Read-only diagnostics dialog, reachable from Options → "Audio Diagnostics...".
/// Shows endpoint, format, capture and output details for troubleshooting; it
/// never changes a device, a setting, an audio effect state or any other
/// configuration.
/// </summary>
internal sealed class DiagnosticsDialog : Form
{
    private readonly string? selectedMicrophoneDeviceId;
    private readonly string? selectedPlaybackDeviceId;
    private readonly OutputFormat outputFormat;
    private readonly AudioCaptureService captureService;
    private readonly TextBox microphoneBox;
    private readonly TextBox systemBox;
    private readonly TextBox outputBox;
    private readonly Label hintLabel;
    private readonly Label title;
    private readonly Label microphoneHeader;
    private readonly Label systemHeader;
    private readonly Label outputHeader;
    private readonly Button refreshButton;
    private readonly Button copyButton;
    private readonly Button closeButton;
    private bool probeRunning;

    public DiagnosticsDialog(
        string? selectedMicrophoneDeviceId,
        string? selectedPlaybackDeviceId,
        OutputFormat outputFormat,
        AudioCaptureService captureService)
    {
        this.selectedMicrophoneDeviceId = selectedMicrophoneDeviceId;
        this.selectedPlaybackDeviceId = selectedPlaybackDeviceId;
        this.outputFormat = outputFormat;
        this.captureService = captureService;

        Text = "Audio Diagnostics";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        ClientSize = new Size(600, 584);
        BackColor = ThemeManager.Current.WindowBack;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        title = CreateSectionHeader("Audio Diagnostics", 14F, ThemeManager.Current.TextTitle, new Point(22, 14));
        microphoneHeader = CreateSectionHeader("Microphone", 10F, ThemeManager.Current.TextHeader, new Point(22, 52));
        microphoneBox = CreateBox(new Point(22, 76), new Size(556, 178));
        systemHeader = CreateSectionHeader("System audio", 10F, ThemeManager.Current.TextHeader, new Point(22, 262));
        systemBox = CreateBox(new Point(22, 286), new Size(556, 118));
        outputHeader = CreateSectionHeader("Output", 10F, ThemeManager.Current.TextHeader, new Point(22, 412));
        outputBox = CreateBox(new Point(22, 436), new Size(556, 40));
        outputBox.Text = DescribeOutput();

        hintLabel = new Label
        {
            Text = "Read-only view. Nothing is changed by this dialog.",
            AutoSize = true,
            ForeColor = ThemeManager.Current.TextMuted,
            Location = new Point(22, 486),
        };

        refreshButton = CreateDialogButton("Refresh", new Point(22, 520), 120);
        refreshButton.Click += (_, _) => RefreshDiagnostics();

        copyButton = CreateDialogButton("Copy Diagnostics", new Point(150, 520), 160);
        copyButton.Click += (_, _) => CopyDiagnostics();

        closeButton = CreateDialogButton("Close", new Point(478, 520), 100);
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.DialogResult = DialogResult.OK;

        Controls.Add(title);
        Controls.Add(microphoneHeader);
        Controls.Add(microphoneBox);
        Controls.Add(systemHeader);
        Controls.Add(systemBox);
        Controls.Add(outputHeader);
        Controls.Add(outputBox);
        Controls.Add(hintLabel);
        Controls.Add(refreshButton);
        Controls.Add(copyButton);
        Controls.Add(closeButton);

        AcceptButton = closeButton;

        ApplyTheme();
        ThemeManager.ThemeChanged += HandleThemeChanged;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeManager.ApplyTitleBarTheme(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        ThemeManager.ThemeChanged -= HandleThemeChanged;
    }

    /// <summary>Re-applies the theme to the dialog when the active theme changes.</summary>
    private void ApplyTheme()
    {
        var palette = ThemeManager.Current;
        BackColor = palette.WindowBack;
        title.ForeColor = palette.TextTitle;
        microphoneHeader.ForeColor = palette.TextHeader;
        systemHeader.ForeColor = palette.TextHeader;
        outputHeader.ForeColor = palette.TextHeader;
        hintLabel.ForeColor = palette.TextMuted;

        microphoneBox.BackColor = palette.FieldBack;
        microphoneBox.ForeColor = palette.FieldText;
        systemBox.BackColor = palette.FieldBack;
        systemBox.ForeColor = palette.FieldText;
        outputBox.BackColor = palette.FieldBack;
        outputBox.ForeColor = palette.FieldText;

        ApplySecondaryButton(refreshButton, palette);
        ApplySecondaryButton(copyButton, palette);
        ApplyPrimaryButton(closeButton, palette);

        ThemeManager.ApplyTitleBarTheme(this);
        Invalidate(true);
    }

    private void HandleThemeChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(HandleThemeChanged);
            return;
        }

        ApplyTheme();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshDiagnostics();
    }

    private static Label CreateSectionHeader(string text, float size, Color color, Point location)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", size, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = color,
            Location = location,
        };
    }

    private static TextBox CreateBox(Point location, Size size)
    {
        return new TextBox
        {
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = ThemeManager.Current.FieldBack,
            ForeColor = ThemeManager.Current.FieldText,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Cascadia Mono", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
            Location = location,
            Size = size,
        };
    }

    private static Button CreateDialogButton(string text, Point location, int width)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeManager.Current.ButtonBack,
            ForeColor = ThemeManager.Current.ButtonText,
            Size = new Size(width, 32),
            Location = location,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = ThemeManager.Current.ButtonBorder;
        return button;
    }

    private static void ApplySecondaryButton(Button button, UiThemePalette palette)
    {
        button.BackColor = palette.ButtonBack;
        button.ForeColor = palette.ButtonText;
        button.FlatAppearance.BorderColor = palette.ButtonBorder;
    }

    private static void ApplyPrimaryButton(Button button, UiThemePalette palette)
    {
        button.BackColor = palette.Accent;
        button.ForeColor = palette.TextOnAccent;
        button.FlatAppearance.BorderSize = 0;
    }

    private void RefreshDiagnostics()
    {
        if (probeRunning)
        {
            return;
        }

        probeRunning = true;
        SetHint("Collecting diagnostics...");
        microphoneBox.Text = "---";
        systemBox.Text = "---";

        Task.Run(() =>
        {
            var microphone = AudioEndpointDiagnostics.GetMicrophone(selectedMicrophoneDeviceId);
            var playback = AudioEndpointDiagnostics.GetPlayback(selectedPlaybackDeviceId);
            return (Microphone: microphone, Playback: playback);
        }).ContinueWith(task =>
        {
            probeRunning = false;
            if (IsDisposed || task.Status != TaskStatus.RanToCompletion)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(() => ApplyProbeResult(task.Result.Microphone, task.Result.Playback)));
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
            }
        });
    }

    private void ApplyProbeResult(EndpointDiagnosticsReport microphone, EndpointDiagnosticsReport playback)
    {
        microphoneBox.Text = BuildMicrophoneText(microphone);
        systemBox.Text = BuildSystemText(playback);
        SetHint($"Collected {DateTime.Now:HH:mm:ss}. Use Copy Diagnostics to share this report.");
    }

    private void SetHint(string text)
    {
        hintLabel.Text = text;
    }

    private string BuildMicrophoneText(EndpointDiagnosticsReport report)
    {
        var lines = new List<string>
        {
            $"Selected input: {(report.IsWindowsDefault ? "Windows default" : "Explicit selection")}{(string.IsNullOrWhiteSpace(report.PreferredDeviceId) ? string.Empty : $" ({report.PreferredDeviceId})")}",
            $"Endpoint: {(report.IsAvailable ? report.FriendlyName : "unavailable")}",
            $"Endpoint ID: {report.EndpointId ?? "<n/a>"}",
            $"Connection: {report.Transport.ToDisplayName()}",
            $"Device state: {report.DeviceState ?? "<n/a>"}",
            $"Form factor: {report.FormFactor ?? "<unknown>"}",
            $"Native/mix format: {DescribeMixDetails(report)}",
            "Requested capture: 48 kHz mono, 32-bit float first (16-bit PCM fallback); RAW stream options (fallback: Windows shared mode)",
            $"Initialized capture: {DescribeInitializedMicrophoneCapture()}",
        };

        var warning = report.BuildQualityWarning();
        lines.Add(warning is not null
            ? $"Quality: (!) {warning}"
            : EndpointQualityClassifier.IsFullBandwidth(report.MixSampleRate)
                ? "Quality: OK"
                : "Quality: n/a");

        if (report.Error is not null)
        {
            lines.Add($"Probe error: {report.Error}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildSystemText(EndpointDiagnosticsReport report)
    {
        var lines = new List<string>
        {
            $"Endpoint: {(report.IsAvailable ? report.FriendlyName : "unavailable")}",
            $"Endpoint ID: {report.EndpointId ?? "<n/a>"}",
            $"Connection: {report.Transport.ToDisplayName()}",
            $"Device state: {report.DeviceState ?? "<n/a>"}",
            $"Form factor: {report.FormFactor ?? "<unknown>"}",
            $"Native/mix format: {DescribeMixDetails(report)}",
        };

        if (captureService.IsRecording && captureService.ActiveSystemFormat is { } activeSystemFormat)
        {
            lines.Add($"Initialized capture: {activeSystemFormat.Description} · Loopback");
        }
        else
        {
            lines.Add("Initialized capture: 48 kHz · Mono · Loopback (initialized when recording starts)");
        }

        if (report.Error is not null)
        {
            lines.Add($"Probe error: {report.Error}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string DescribeMixDetails(EndpointDiagnosticsReport report)
    {
        if (!report.IsAvailable || report.MixSampleRate <= 0)
        {
            return "n/a";
        }

        return $"{report.MixSampleRate} Hz, {report.MixChannels} channel(s), {report.MixBitsPerSample}-bit, {report.MixFormatDescription ?? "unknown"}"
            + (report.MixChannelMask is { } mask ? $", channel mask 0x{mask:X8}" : string.Empty);
    }

    private string DescribeInitializedMicrophoneCapture()
    {
        if (!captureService.IsRecording || captureService.ActiveMicrophoneFormat is not { } activeFormat)
        {
            return "not initialized yet (recording not started)";
        }

        return activeFormat.Description + (captureService.ActiveMicrophoneRawActivated switch
        {
            true => " · RAW",
            false => " · Shared (Windows processing may be active)",
            _ => string.Empty,
        });
    }

    private string DescribeOutput()
    {
        return outputFormat == OutputFormat.Mp3
            ? $"MP3 · {RecordingOutputProfile.SampleRateKhz} · Mono · {RecordingOutputProfile.Mp3BitRateKbps} kbps · single encode from {RecordingOutputProfile.BitsPerSample}-bit PCM"
            : $"WAV · {RecordingOutputProfile.SampleRateKhz} · Mono · PCM16 · single encode";
    }

    private void CopyDiagnostics()
    {
        var text = $"ELARA diagnostics {AppVersion.DisplayVersion}"
            + $"{Environment.NewLine}Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            + $"{Environment.NewLine}Selected format: {outputFormat.ToDisplayName()}"
            + $"{Environment.NewLine}{Environment.NewLine}[Microphone]{Environment.NewLine}{microphoneBox.Text}"
            + $"{Environment.NewLine}{Environment.NewLine}[System audio]{Environment.NewLine}{systemBox.Text}"
            + $"{Environment.NewLine}{Environment.NewLine}[Output]{Environment.NewLine}{outputBox.Text}";

        try
        {
            Clipboard.SetText(text);
            SetHint("Diagnostics copied to the clipboard.");
        }
        catch (Exception ex)
        {
            SetHint($"Could not copy diagnostics. {ex.Message}");
            AppLogger.Warn($"Could not copy diagnostics to the clipboard. {ex.Message}");
        }
    }
}