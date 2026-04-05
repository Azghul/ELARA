namespace SimpleAudioRecorder;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (UiPreviewRunner.IsPreviewCommand(args))
        {
            return UiPreviewRunner.Run(args);
        }

        Application.Run(new MainForm());
        return 0;
    }
}
