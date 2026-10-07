using System.Diagnostics;

namespace ELARA;

internal sealed class AboutDialog : Form
{
    private const string UpstreamUrl = "https://github.com/SickPuppyCoding/SimpleAudioRecorder";

    private readonly Label title;
    private readonly Label versionLine;
    private readonly Label subtitle;
    private readonly Label description;
    private readonly Label originsNotice;
    private readonly Label licenseNotice;
    private readonly Label copyright;
    private readonly LinkLabel linkLabel;
    private readonly LinkLabel upstreamLinkLabel;
    private readonly Button openLogsButton;
    private readonly Button closeButton;

    public AboutDialog(string githubUrl, string logDirectory)
    {
        Text = "About ELARA";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 320);
        BackColor = ThemeManager.Current.WindowBack;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        Icon = AppIcon.Load();

        title = new Label
        {
            Text = "ELARA",
            AutoSize = true,
            ForeColor = ThemeManager.Current.TextTitle,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold, GraphicsUnit.Point),
            Location = new Point(22, 16),
        };

        versionLine = new Label
        {
            Text = AppVersion.DisplayLabel,
            AutoSize = true,
            ForeColor = ThemeManager.Current.TextMuted,
            Location = new Point(28 + title.PreferredWidth, 22),
        };

        subtitle = new Label
        {
            Text = "Easy Local Audio Recording App",
            AutoSize = true,
            ForeColor = ThemeManager.Current.TextHeader,
            Location = new Point(24, 50),
        };

        description = new Label
        {
            Text = "A lightweight Windows recorder for microphone and system audio.",
            AutoSize = true,
            ForeColor = ThemeManager.Current.TextHeader,
            Location = new Point(24, 74),
        };

        originsNotice = new Label
        {
            Text = "Originally based on SimpleAudioRecorder by SickPuppyCoding. " +
                   "This version has been extended substantially — UI, device selection, " +
                   "output formats, configuration, tray integration, output handling and " +
                   "recording-safety behaviour — and is developed independently.",
            AutoSize = true,
            MaximumSize = new Size(376, 0),
            ForeColor = ThemeManager.Current.TextHeader,
            Location = new Point(24, 98),
        };

        licenseNotice = new Label
        {
            Text = "Uses NAudio (MIT), NAudio.Lame (MIT) and LAME libmp3lame (GNU Library GPL v2). See README.",
            AutoSize = true,
            ForeColor = ThemeManager.Current.TextMuted,
            Location = new Point(24, 162),
        };

        copyright = new Label
        {
            Text = "Original SimpleAudioRecorder Copyright (c) SickPuppyCoding — MIT.",
            AutoSize = true,
            ForeColor = ThemeManager.Current.TextMuted,
            Location = new Point(24, 184),
        };

        linkLabel = new LinkLabel
        {
            Text = githubUrl,
            AutoSize = true,
            LinkColor = ThemeManager.Current.LinkColor,
            ActiveLinkColor = ThemeManager.Current.LinkActiveColor,
            VisitedLinkColor = ThemeManager.Current.LinkColor,
            Location = new Point(24, 212),
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        linkLabel.LinkClicked += (_, _) =>
        {
            Process.Start(new ProcessStartInfo(githubUrl) { UseShellExecute = true });
        };

        upstreamLinkLabel = new LinkLabel
        {
            Text = "Original project: " + UpstreamUrl,
            AutoSize = true,
            LinkColor = ThemeManager.Current.LinkColor,
            ActiveLinkColor = ThemeManager.Current.LinkActiveColor,
            VisitedLinkColor = ThemeManager.Current.LinkColor,
            Location = new Point(24, 234),
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        upstreamLinkLabel.LinkClicked += (_, _) =>
        {
            Process.Start(new ProcessStartInfo(UpstreamUrl) { UseShellExecute = true });
        };

        openLogsButton = new Button
        {
            Text = "Open Log Files",
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeManager.Current.ButtonBack,
            ForeColor = ThemeManager.Current.ButtonText,
            Size = new Size(126, 34),
            Location = new Point(24, ClientSize.Height - 54),
        };
        openLogsButton.FlatAppearance.BorderColor = ThemeManager.Current.ButtonBorder;
        openLogsButton.Click += (_, _) =>
        {
            Directory.CreateDirectory(logDirectory);
            Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
        };

        closeButton = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeManager.Current.Accent,
            ForeColor = ThemeManager.Current.TextOnAccent,
            Size = new Size(102, 34),
            Location = new Point(ClientSize.Width - 124, ClientSize.Height - 54),
        };
        closeButton.FlatAppearance.BorderSize = 0;

        Controls.Add(title);
        Controls.Add(versionLine);
        Controls.Add(subtitle);
        Controls.Add(description);
        Controls.Add(originsNotice);
        Controls.Add(licenseNotice);
        Controls.Add(copyright);
        Controls.Add(linkLabel);
        Controls.Add(upstreamLinkLabel);
        Controls.Add(openLogsButton);
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
        versionLine.ForeColor = palette.TextMuted;
        subtitle.ForeColor = palette.TextHeader;
        description.ForeColor = palette.TextHeader;
        originsNotice.ForeColor = palette.TextHeader;
        licenseNotice.ForeColor = palette.TextMuted;
        copyright.ForeColor = palette.TextMuted;
        linkLabel.LinkColor = palette.LinkColor;
        linkLabel.ActiveLinkColor = palette.LinkActiveColor;
        linkLabel.VisitedLinkColor = palette.LinkColor;
        upstreamLinkLabel.LinkColor = palette.LinkColor;
        upstreamLinkLabel.ActiveLinkColor = palette.LinkActiveColor;
        upstreamLinkLabel.VisitedLinkColor = palette.LinkColor;

        openLogsButton.BackColor = palette.ButtonBack;
        openLogsButton.ForeColor = palette.ButtonText;
        openLogsButton.FlatAppearance.BorderColor = palette.ButtonBorder;
        closeButton.BackColor = palette.Accent;
        closeButton.ForeColor = palette.TextOnAccent;

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
}
