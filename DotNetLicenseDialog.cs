namespace ELARA;

internal sealed class DotNetLicenseDialog : Form
{
    private readonly Label header;
    private readonly Label info;
    private readonly TextBox licenseBox;
    private readonly Button acceptButton;
    private readonly Button exitButton;

    public DotNetLicenseDialog()
    {
        Text = "Microsoft .NET Library License";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ClientSize = new Size(640, 500);
        BackColor = ThemeManager.Current.WindowBack;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        header = new Label
        {
            Text = "Microsoft .NET Library License",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ThemeManager.Current.TextTitle,
            Location = new Point(20, 14),
        };

        info = new Label
        {
            Text = "ELARA includes Microsoft .NET runtime components that are distributed " +
                   "under the Microsoft .NET Library License. Please review and accept the " +
                   "license terms below to use ELARA.",
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            ForeColor = ThemeManager.Current.TextHeader,
            Location = new Point(20, 46),
        };

        licenseBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = ThemeManager.Current.FieldBack,
            ForeColor = ThemeManager.Current.FieldText,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Location = new Point(20, 100),
            Size = new Size(600, 330),
            Text = LoadLicenseText(),
        };

        acceptButton = new Button
        {
            Text = "Accept",
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeManager.Current.Accent,
            ForeColor = ThemeManager.Current.TextOnAccent,
            Size = new Size(120, 34),
            Location = new Point(ClientSize.Width - 144, ClientSize.Height - 52),
        };
        acceptButton.FlatAppearance.BorderSize = 0;

        exitButton = new Button
        {
            Text = "Exit",
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeManager.Current.ButtonBack,
            ForeColor = ThemeManager.Current.ButtonText,
            Size = new Size(100, 34),
            Location = new Point(20, ClientSize.Height - 52),
        };
        exitButton.FlatAppearance.BorderColor = ThemeManager.Current.ButtonBorder;

        Controls.Add(header);
        Controls.Add(info);
        Controls.Add(licenseBox);
        Controls.Add(acceptButton);
        Controls.Add(exitButton);

        AcceptButton = acceptButton;
        CancelButton = exitButton;

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
        header.ForeColor = palette.TextTitle;
        info.ForeColor = palette.TextHeader;
        licenseBox.BackColor = palette.FieldBack;
        licenseBox.ForeColor = palette.FieldText;
        acceptButton.BackColor = palette.Accent;
        acceptButton.ForeColor = palette.TextOnAccent;
        exitButton.BackColor = palette.ButtonBack;
        exitButton.ForeColor = palette.ButtonText;
        exitButton.FlatAppearance.BorderColor = palette.ButtonBorder;

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


    private static string LoadLicenseText()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "LICENSE-DOTNET-LIBRARY.txt");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        catch
        {
        }

        return "The Microsoft .NET Library License is provided as LICENSE-DOTNET-LIBRARY.txt " +
               "next to the ELARA executable. It could not be loaded for display.";
    }
}
