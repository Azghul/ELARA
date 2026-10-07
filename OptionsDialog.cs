namespace ELARA;

/// <summary>
/// Modal options dialog with an Appearance section (theme + Windows accent
/// color), recording location, actions and audio diagnostics. Theme changes are
/// applied live while the dialog is open; Cancel (button, ESC or title-bar
/// close) restores the theme that was active when the dialog was opened.
/// </summary>
internal sealed class OptionsDialog : Form
{
    private readonly string defaultOutputDirectory;
    private readonly Action<string?> onOutputDirectoryChanged;
    private readonly Action openAudioFiles;
    private readonly Action openLogFiles;
    private readonly Action openDiagnostics;
    private readonly Action persistThemePreferences;
    private readonly AppTheme initialTheme;
    private readonly bool initialUseWindowsAccentColor;
    private readonly TextBox outputPathBox;
    private readonly ToolTip toolTip = new() { ShowAlways = true };
    private readonly Label title;
    private readonly Label appearanceHeader;
    private readonly Label themeLabel;
    private readonly ComboBox themeBox;
    private readonly CheckBox accentCheckBox;
    private readonly Label recordingHeader;
    private readonly Button chooseButton;
    private readonly Button resetButton;
    private readonly Label actionsHeader;
    private readonly Button openAudioButton;
    private readonly Button openLogsButton;
    private readonly Label diagnosticsHeader;
    private readonly Button diagnosticsButton;
    private readonly Button cancelButton;
    private readonly Button closeButton;

    public OptionsDialog(
        string? customOutputDirectory,
        string defaultOutputDirectory,
        Action<string?> onOutputDirectoryChanged,
        Action openAudioFiles,
        Action openLogFiles,
        Action openDiagnostics,
        Action persistThemePreferences)
    {
        this.defaultOutputDirectory = defaultOutputDirectory;
        this.onOutputDirectoryChanged = onOutputDirectoryChanged;
        this.openAudioFiles = openAudioFiles;
        this.openLogFiles = openLogFiles;
        this.openDiagnostics = openDiagnostics;
        this.persistThemePreferences = persistThemePreferences;
        initialTheme = ThemeManager.SelectedTheme;
        initialUseWindowsAccentColor = ThemeManager.UseWindowsAccentColor;

        Text = "Options";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 390);
        BackColor = ThemeManager.Current.WindowBack;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        var fieldColor = ThemeManager.Current.FieldText;

        title = CreateHeaderLabel("Options", 14F, ThemeManager.Current.TextTitle, new Point(22, 16));

        appearanceHeader = CreateHeaderLabel("Appearance", 10F, ThemeManager.Current.TextHeader, new Point(22, 52));

        themeLabel = new Label
        {
            Text = "Theme",
            AutoSize = true,
            ForeColor = ThemeManager.Current.TextMuted,
            Location = new Point(22, 82),
        };

        themeBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(22, 104),
            Size = new Size(200, 26),
            BackColor = ThemeManager.Current.FieldBack,
            ForeColor = fieldColor,
        };
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            themeBox.Items.Add(UiTheme.GetThemeDisplayName(theme));
        }

        themeBox.SelectedIndex = (int)initialTheme;
        themeBox.SelectedIndexChanged += (_, _) => ApplySelectedThemeFromControls();

        accentCheckBox = new CheckBox
        {
            Text = "Use Windows accent color",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = fieldColor,
            Location = new Point(240, 106),
            Checked = initialUseWindowsAccentColor,
        };
        accentCheckBox.CheckedChanged += (_, _) => ApplySelectedThemeFromControls();

        recordingHeader = CreateHeaderLabel("Recording location", 10F, ThemeManager.Current.TextHeader, new Point(22, 142));

        outputPathBox = new TextBox
        {
            ReadOnly = true,
            BackColor = ThemeManager.Current.FieldBack,
            ForeColor = fieldColor,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(22, 166),
            Size = new Size(416, 24),
            Text = DisplayPath(customOutputDirectory),
        };

        chooseButton = CreateDialogButton("Choose Folder...", new Point(22, 198), 150);
        chooseButton.Click += (_, _) => ChooseFolder();

        resetButton = CreateDialogButton("Reset to Default", new Point(180, 198), 150);
        resetButton.Click += (_, _) => ResetToDefault();

        actionsHeader = CreateHeaderLabel("Actions", 10F, ThemeManager.Current.TextHeader, new Point(22, 244));

        openAudioButton = CreateDialogButton("Open Audio Files", new Point(22, 272), 150);
        openAudioButton.Click += (_, _) => openAudioFiles();

        openLogsButton = CreateDialogButton("Open Log Files", new Point(180, 272), 150);
        openLogsButton.Click += (_, _) => openLogFiles();

        diagnosticsHeader = CreateHeaderLabel("Diagnostics", 10F, ThemeManager.Current.TextHeader, new Point(22, 316));

        diagnosticsButton = CreateDialogButton("Audio Diagnostics...", new Point(22, 344), 150);
        diagnosticsButton.Click += (_, _) => openDiagnostics();

        cancelButton = CreateDialogButton("Cancel", new Point(230, 344), 100);
        cancelButton.DialogResult = DialogResult.Cancel;

        closeButton = CreateDialogButton("Close", new Point(338, 344), 100);
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.DialogResult = DialogResult.OK;

        Controls.Add(title);
        Controls.Add(appearanceHeader);
        Controls.Add(themeLabel);
        Controls.Add(themeBox);
        Controls.Add(accentCheckBox);
        Controls.Add(recordingHeader);
        Controls.Add(outputPathBox);
        Controls.Add(chooseButton);
        Controls.Add(resetButton);
        Controls.Add(actionsHeader);
        Controls.Add(openAudioButton);
        Controls.Add(openLogsButton);
        Controls.Add(diagnosticsHeader);
        Controls.Add(diagnosticsButton);
        Controls.Add(cancelButton);
        Controls.Add(closeButton);

        AcceptButton = closeButton;
        CancelButton = cancelButton;

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

        if (DialogResult == DialogResult.OK)
        {
            persistThemePreferences();
            return;
        }

        // Cancel via the button, ESC or the title-bar close: restore the theme
        // that was active when the dialog was opened (live preview reverts).
        ThemeManager.SetTheme(initialTheme, initialUseWindowsAccentColor);
    }

    /// <summary>Applies the selector state as the live theme while the dialog is open.</summary>
    private void ApplySelectedThemeFromControls()
    {
        var theme = Enum.IsDefined((AppTheme)themeBox.SelectedIndex) ? (AppTheme)themeBox.SelectedIndex : AppTheme.System;
        ThemeManager.SetTheme(theme, accentCheckBox.Checked);
    }


    /// <summary>Re-applies the theme to the dialog when the active theme changes.</summary>
    private void ApplyTheme()
    {
        var palette = ThemeManager.Current;
        BackColor = palette.WindowBack;
        title.ForeColor = palette.TextTitle;
        appearanceHeader.ForeColor = palette.TextHeader;
        themeLabel.ForeColor = palette.TextMuted;
        recordingHeader.ForeColor = palette.TextHeader;
        actionsHeader.ForeColor = palette.TextHeader;
        diagnosticsHeader.ForeColor = palette.TextHeader;

        themeBox.BackColor = palette.FieldBack;
        themeBox.ForeColor = palette.FieldText;
        accentCheckBox.ForeColor = palette.FieldText;
        outputPathBox.BackColor = palette.FieldBack;
        outputPathBox.ForeColor = palette.FieldText;

        ApplySecondaryButton(chooseButton, palette);
        ApplySecondaryButton(resetButton, palette);
        ApplySecondaryButton(openAudioButton, palette);
        ApplySecondaryButton(openLogsButton, palette);
        ApplySecondaryButton(diagnosticsButton, palette);
        ApplySecondaryButton(cancelButton, palette);
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

    private static Label CreateHeaderLabel(string text, float size, Color color, Point location)
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
}
