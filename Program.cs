namespace ELARA;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppLogger.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
        {
            AppLogger.Error("Unhandled UI exception.", eventArgs.Exception);
            MessageBox.Show(
                $"The app hit an unexpected error.\n\nA verbose log was written to:\n{AppLogger.CurrentLogFilePath}",
                "ELARA",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            AppLogger.Error("Unhandled non-UI exception.", eventArgs.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLogger.Error("Unobserved task exception.", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        AppLogger.Info("Application starting.");

        try
        {
            if (UiPreviewRunner.IsPreviewCommand(args))
            {
                AppLogger.Info("Launching UI preview renderer.");
                return UiPreviewRunner.Run(args);
            }

            // First-run acceptance of the Microsoft .NET Library License, which covers
            // components of the self-contained Windows distribution (e.g. coreclr.dll).
            var settings = AppSettings.Load();
            ThemeManager.Initialize(settings.ResolveTheme(), settings.UseWindowsAccentColor);
            if (!settings.AcceptedDotNetLibraryLicense)
            {
                AppLogger.Info("Microsoft .NET Library License acceptance required.");
                using var licenseDialog = new DotNetLicenseDialog();
                if (licenseDialog.ShowDialog() != DialogResult.OK)
                {
                    AppLogger.Info("Microsoft .NET Library License not accepted; exiting.");
                    return 1;
                }

                settings.AcceptedDotNetLibraryLicense = true;
                settings.Save();
                AppLogger.Info("Microsoft .NET Library License accepted.");
            }

            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Fatal application startup failure.", ex);
            MessageBox.Show(
                $"The app could not start.\n\nA verbose log was written to:\n{AppLogger.CurrentLogFilePath}",
                "ELARA",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            AppLogger.Info("Application exiting.");
        }
    }
}
