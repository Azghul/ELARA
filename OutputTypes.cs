namespace ELARA;

public enum CaptureMode
{
    Both,
    Microphone,
    System,
}

public enum OutputFormat
{
    Mp3,
    Wav,
}

public static class OutputFormatExtensions
{
    public static string ToFileExtension(this OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Mp3 => ".mp3",
            OutputFormat.Wav => ".wav",
            _ => "." + format.ToString().ToLowerInvariant(),
        };
    }

    public static string ToDisplayName(this OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Mp3 => "MP3",
            OutputFormat.Wav => "WAV",
            _ => format.ToString(),
        };
    }
}

public static class CaptureModeExtensions
{
    public static string ToDisplayName(this CaptureMode mode)
    {
        return mode switch
        {
            CaptureMode.Both => "Both",
            CaptureMode.Microphone => "Mic",
            CaptureMode.System => "System",
            _ => mode.ToString(),
        };
    }
}

public readonly record struct RecordingInfo(
    string FilePath,
    OutputFormat Format,
    CaptureMode Mode,
    TimeSpan Duration,
    DateTime CreatedAt,
    bool WasRescuedToWav,
    bool WasRescuedToMp3,
    bool HadCaptureStopErrors);