using System.Diagnostics;

namespace ELARA;

internal sealed class AboutDialog : Form
{
    private const string UpstreamUrl = "https://github.com/SickPuppyCoding/SimpleAudioRecorder";

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
        BackColor = Color.FromArgb(15, 19, 40);
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        var title = new Label
        {
            Text = "ELARA",
            AutoSize = true,
            ForeColor = Color.FromArgb(247, 248, 255),
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold, GraphicsUnit.Point),
            Location = new Point(22, 16),
        };

        var subtitle = new Label
        {
            Text = "Easy Local Audio Recording App",
            AutoSize = true,
            ForeColor = Color.FromArgb(195, 201, 231),
            Location = new Point(24, 50),
        };

        var description = new Label
        {
            Text = "A lightweight Windows recorder for microphone and system audio.",
            AutoSize = true,
            ForeColor = Color.FromArgb(195, 201, 231),
            Location = new Point(24, 74),
        };

        var originsNotice = new Label
        {
            Text = "Originally based on SimpleAudioRecorder by SickPuppyCoding. " +
                   "This version has been extended substantially — UI, device selection, " +
                   "output formats, configuration, tray integration, output handling and " +
                   "recording-safety behaviour — and is developed independently.",
            AutoSize = true,
            MaximumSize = new Size(376, 0),
            ForeColor = Color.FromArgb(195, 201, 231),
            Location = new Point(24, 98),
        };

        var licenseNotice = new Label
        {
            Text = "Uses NAudio (MIT), NAudio.Lame (MIT) and LAME libmp3lame (LGPL). See README.",
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 158, 198),
            Location = new Point(24, 162),
        };

        var copyright = new Label
        {
            Text = "Original SimpleAudioRecorder Copyright (c) SickPuppyCoding — MIT.",
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 158, 198),
            Location = new Point(24, 184),
        };

        var linkLabel = new LinkLabel
        {
            Text = githubUrl,
            AutoSize = true,
            LinkColor = Color.FromArgb(145, 136, 255),
            ActiveLinkColor = Color.FromArgb(186, 176, 255),
            VisitedLinkColor = Color.FromArgb(145, 136, 255),
            Location = new Point(24, 212),
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        linkLabel.LinkClicked += (_, _) =>
        {
            Process.Start(new ProcessStartInfo(githubUrl) { UseShellExecute = true });
        };

        var upstreamLinkLabel = new LinkLabel
        {
            Text = "Original project: " + UpstreamUrl,
            AutoSize = true,
            LinkColor = Color.FromArgb(145, 136, 255),
            ActiveLinkColor = Color.FromArgb(186, 176, 255),
            VisitedLinkColor = Color.FromArgb(145, 136, 255),
            Location = new Point(24, 234),
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        upstreamLinkLabel.LinkClicked += (_, _) =>
        {
            Process.Start(new ProcessStartInfo(UpstreamUrl) { UseShellExecute = true });
        };

        var openLogsButton = new Button
        {
            Text = "Open Log Files",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(33, 39, 76),
            ForeColor = Color.White,
            Size = new Size(126, 34),
            Location = new Point(24, ClientSize.Height - 54),
        };
        openLogsButton.FlatAppearance.BorderColor = Color.FromArgb(66, 73, 109);
        openLogsButton.Click += (_, _) =>
        {
            Directory.CreateDirectory(logDirectory);
            Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
        };

        var closeButton = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(101, 77, 245),
            ForeColor = Color.White,
            Size = new Size(102, 34),
            Location = new Point(ClientSize.Width - 124, ClientSize.Height - 54),
        };
        closeButton.FlatAppearance.BorderSize = 0;

        Controls.Add(title);
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
    }
}
