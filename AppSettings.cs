using System.Text.Json;
using System.Text.Json.Serialization;

namespace ELARA;

internal sealed class AppSettings
{
    private static readonly object SyncRoot = new();

    public string? SelectedMicrophoneDeviceId { get; set; }

    public string? SelectedPlaybackDeviceId { get; set; }

    public string? CustomOutputDirectory { get; set; }

    public bool AcceptedDotNetLibraryLicense { get; set; }

    /// <summary>
    /// Last saved window bounds (null = default placement). Restored only when
    /// the saved rectangle still lies on a visible screen.
    /// </summary>
    public int? WindowLeft { get; set; }

    public int? WindowTop { get; set; }

    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    [JsonPropertyName("OutputFormat")]
    public string Format { get; set; } = nameof(OutputFormat.Mp3);

    public OutputFormat ResolveOutputFormat()
    {
        return Enum.TryParse<OutputFormat>(Format, ignoreCase: true, out var format)
            ? format
            : OutputFormat.Mp3;
    }

    public static AppSettings Load()
    {
        MigrateLegacySettingsIfNeeded();

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

    private static void MigrateLegacySettingsIfNeeded()
    {
        // One-time migration from ELARA 0.8.0 (then branded Simple Audio Recorder).
        // The legacy file is never deleted or modified; failures are non-fatal.
        try
        {
            var newPath = AppPaths.SettingsFilePath;
            var legacyPath = AppPaths.LegacySettingsFilePath;
            if (File.Exists(newPath) || !File.Exists(legacyPath))
            {
                return;
            }

            Directory.CreateDirectory(AppPaths.SettingsDirectory);
            File.Copy(legacyPath, newPath);
            AppLogger.Info("Migrated legacy 0.8.0 settings to the new ELARA settings location.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not migrate legacy settings; continuing without migration. {ex.Message}");
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