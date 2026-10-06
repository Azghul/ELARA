namespace ELARA;

internal sealed class OptionsDialog : Form
{
    private readonly string defaultOutputDirectory;
    private readonly Action<string?> onOutputDirectoryChanged;
    private readonly Action<MicrophoneProcessingMode> onMicrophoneProcessingChanged;
    private readonly Action openAudioFiles;
    private readonly Action openLogFiles;
    private readonly TextBox outputPathBox;
    private readonly RadioButton rawProcessingRadio;
    private readonly RadioButton windowsNoiseSuppressionRadio;
    private readonly ToolTip toolTip = new() { ShowAlways = true };

    public OptionsDialog(
        string? customOutputDirectory,
        string defaultOutputDirectory,
        MicrophoneProcessingMode processingMode,
        Action<string?> onOutputDirectoryChanged,
        Action<MicrophoneProcessingMode> onMicrophoneProcessingChanged,
        Action openAudioFiles,
        Action openLogFiles)
    {
        this.defaultOutputDirectory = defaultOutputDirectory;
        this.onOutputDirectoryChanged = onOutputDirectoryChanged;
        this.onMicrophoneProcessingChanged = onMicrophoneProcessingChanged;
        this.openAudioFiles = openAudioFiles;
        this.openLogFiles = openLogFiles;

        Text = "Options";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 352);
        BackColor = Color.FromArgb(15, 19, 40);
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        var fieldColor = Color.FromArgb(235, 238, 248);

        var title = new Label
        {
            Text = "Options",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(247, 248, 255),
            Location = new Point(22, 16),
        };

        var recordingHeader = new Label
        {
            Text = "Recording location",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(195, 201, 231),
            Location = new Point(22, 52),
        };

        outputPathBox = new TextBox
        {
            ReadOnly = true,
            BackColor = Color.FromArgb(24, 29, 56),
            ForeColor = fieldColor,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(22, 78),
            Size = new Size(416, 24),
            Text = DisplayPath(customOutputDirectory),
        };

        var chooseButton = CreateDialogButton("Choose Folder...", new Point(22, 110), 150);
        chooseButton.Click += (_, _) => ChooseFolder();

        var resetButton = CreateDialogButton("Reset to Default", new Point(180, 110), 150);
        resetButton.Click += (_, _) => ResetToDefault();

        var processingHeader = new Label
        {
            Text = "Microphone processing",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(195, 201, 231),
            Location = new Point(22, 154),
        };

        rawProcessingRadio = new RadioButton
        {
            Text = "Raw",
            Checked = processingMode == MicrophoneProcessingMode.Raw,
            ForeColor = fieldColor,
            BackColor = Color.Transparent,
            Location = new Point(22, 178),
            Size = new Size(150, 24),
        };
        rawProcessingRadio.Click += (_, _) => SelectProcessingMode(MicrophoneProcessingMode.Raw);
        toolTip.SetToolTip(
            rawProcessingRadio,
            "Captures the microphone with Windows adaptive audio processing bypassed where supported.");

        windowsNoiseSuppressionRadio = new RadioButton
        {
            Text = "Windows noise suppression",
            Checked = processingMode == MicrophoneProcessingMode.WindowsNoiseSuppression,
            ForeColor = fieldColor,
            BackColor = Color.Transparent,
            Location = new Point(174, 178),
            Size = new Size(264, 24),
        };
        windowsNoiseSuppressionRadio.Click += (_, _) => SelectProcessingMode(MicrophoneProcessingMode.WindowsNoiseSuppression);
        toolTip.SetToolTip(
            windowsNoiseSuppressionRadio,
            "Uses Windows/device speech processing and requests available noise suppression.");

        var actionsHeader = new Label
        {
            Text = "Actions",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(195, 201, 231),
            Location = new Point(22, 216),
        };

        var openAudioButton = CreateDialogButton("Open Audio Files", new Point(22, 244), 150);
        openAudioButton.Click += (_, _) => openAudioFiles();

        var openLogsButton = CreateDialogButton("Open Log Files", new Point(180, 244), 150);
        openLogsButton.Click += (_, _) => openLogFiles();

        var closeButton = CreateDialogButton("Close", new Point(338, 300), 100);
        closeButton.BackColor = Color.FromArgb(101, 77, 245);
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.DialogResult = DialogResult.OK;

        Controls.Add(title);
        Controls.Add(recordingHeader);
        Controls.Add(outputPathBox);
        Controls.Add(chooseButton);
        Controls.Add(resetButton);
        Controls.Add(processingHeader);
        Controls.Add(rawProcessingRadio);
        Controls.Add(windowsNoiseSuppressionRadio);
        Controls.Add(actionsHeader);
        Controls.Add(openAudioButton);
        Controls.Add(openLogsButton);
        Controls.Add(closeButton);

        AcceptButton = closeButton;
    }

    private void SelectProcessingMode(MicrophoneProcessingMode mode)
    {
        rawProcessingRadio.Checked = mode == MicrophoneProcessingMode.Raw;
        windowsNoiseSuppressionRadio.Checked = mode == MicrophoneProcessingMode.WindowsNoiseSuppression;
        onMicrophoneProcessingChanged(mode);
    }

    private string DisplayPath(string? customOutputDirectory)
    {
        return string.IsNullOrWhiteSpace(customOutputDirectory) ? defaultOutputDirectory : customOutputDirectory;
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder where new recordings are saved.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        if (Directory.Exists(outputPathBox.Text))
        {
            dialog.SelectedPath = outputPathBox.Text;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        outputPathBox.Text = dialog.SelectedPath;
        onOutputDirectoryChanged(dialog.SelectedPath);
    }

    private void ResetToDefault()
    {
        outputPathBox.Text = defaultOutputDirectory;
        onOutputDirectoryChanged(null);
    }

    private static Button CreateDialogButton(string text, Point location, int width)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(33, 39, 76),
            ForeColor = Color.White,
            Size = new Size(width, 32),
            Location = location,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(66, 73, 109);
        return button;
    }
}