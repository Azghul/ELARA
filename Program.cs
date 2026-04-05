namespace SimpleAudioRecorder;

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
                "Simple Audio Recorder",
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

            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Fatal application startup failure.", ex);
            MessageBox.Show(
                $"The app could not start.\n\nA verbose log was written to:\n{AppLogger.CurrentLogFilePath}",
                "Simple Audio Recorder",
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
