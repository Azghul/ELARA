namespace SimpleAudioRecorder;

internal static class AppPaths
{
    public static string RecordingDirectory { get; } = ResolveWritableDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Simple Audio Recorder"),
        Path.Combine(AppContext.BaseDirectory, "Recordings"),
        Path.Combine(Path.GetTempPath(), "SimpleAudioRecorder", "Recordings"));

    public static string LogDirectory { get; } = ResolveWritableDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SickPuppyCoding", "SimpleAudioRecorder", "Logs"),
        Path.Combine(AppContext.BaseDirectory, "Logs"),
        Path.Combine(Path.GetTempPath(), "SimpleAudioRecorder", "Logs"));

    public static string SettingsDirectory { get; } = ResolveWritableDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SickPuppyCoding", "SimpleAudioRecorder"),
        Path.Combine(AppContext.BaseDirectory, "Settings"),
        Path.Combine(Path.GetTempPath(), "SimpleAudioRecorder", "Settings"));

    public static string SettingsFilePath { get; } = Path.Combine(SettingsDirectory, "settings.json");

    private static string ResolveWritableDirectory(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (TryEnsureWritableDirectory(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not resolve a writable application data directory.");
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, $".write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
