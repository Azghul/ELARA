namespace ELARA;

internal sealed class DotNetLicenseDialog : Form
{
    public DotNetLicenseDialog()
    {
        Text = "Microsoft .NET Library License";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ClientSize = new Size(640, 500);
        BackColor = Color.FromArgb(15, 19, 40);
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        var header = new Label
        {
            Text = "Microsoft .NET Library License",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(247, 248, 255),
            Location = new Point(20, 14),
        };

        var info = new Label
        {
            Text = "ELARA includes Microsoft .NET runtime components that are distributed " +
                   "under the Microsoft .NET Library License. Please review and accept the " +
                   "license terms below to use ELARA.",
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            ForeColor = Color.FromArgb(195, 201, 231),
            Location = new Point(20, 46),
        };

        var licenseBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(24, 29, 56),
            ForeColor = Color.FromArgb(220, 224, 240),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Location = new Point(20, 100),
            Size = new Size(600, 330),
            Text = LoadLicenseText(),
        };

        var acceptButton = new Button
        {
            Text = "Accept",
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(101, 77, 245),
            ForeColor = Color.White,
            Size = new Size(120, 34),
            Location = new Point(ClientSize.Width - 144, ClientSize.Height - 52),
        };
        acceptButton.FlatAppearance.BorderSize = 0;

        var exitButton = new Button
        {
            Text = "Exit",
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(33, 39, 76),
            ForeColor = Color.White,
            Size = new Size(100, 34),
            Location = new Point(20, ClientSize.Height - 52),
        };
        exitButton.FlatAppearance.BorderColor = Color.FromArgb(66, 73, 109);

        Controls.Add(header);
        Controls.Add(info);
        Controls.Add(licenseBox);
        Controls.Add(acceptButton);
        Controls.Add(exitButton);

        AcceptButton = acceptButton;
        CancelButton = exitButton;
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