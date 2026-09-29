using System.Text.Json;

namespace SimpleAudioRecorder;

internal sealed class AppSettings
{
    private static readonly object SyncRoot = new();

    public string? SelectedMicrophoneDeviceId { get; set; }

    public string? SelectedPlaybackDeviceId { get; set; }

    public string OutputFormat { get; set; } = nameof(SimpleAudioRecorder.OutputFormat.Mp3);

    public OutputFormat ResolveOutputFormat()
    {
        return Enum.TryParse<OutputFormat>(OutputFormat, ignoreCase: true, out var format)
            ? format
            : OutputFormat.Mp3;
    }

    public static AppSettings Load()
    {
        lock (SyncRoot)
        {
            try
            {
                var path = AppPaths.SettingsFilePath;
                if (!File.Exists(path))
                {
                    return new AppSettings();
                }

                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not read the settings file; using defaults. {ex.Message}");
                return new AppSettings();
            }
        }
    }

    public void Save()
    {
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.SettingsDirectory);
                File.WriteAllText(
                    AppPaths.SettingsFilePath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not write the settings file '{AppPaths.SettingsFilePath}'. {ex.Message}");
            }
        }
    }
}