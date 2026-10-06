namespace ELARA;

/// <summary>
/// Pure file-naming/path helpers shared by the recording service and the test
/// harness. Everything here is platform-neutral (no Gaya desktop dependencies).
/// </summary>
internal static class OutputPathUtility
{
    public static string DefaultRecordingFileName(DateTime now)
    {
        return $"{now:yyyy-MM-dd HH-mm-ss}";
    }

    /// <summary>
    /// Makes sure the file extension matches the selected output format. An
    /// existing but mismatching extension is replaced; a missing extension is
    /// appended. Never leaves "meeting.mp3.wav"-style artifacts behind.
    /// </summary>
    public static string EnsureExtensionMatches(string filePath, OutputFormat format)
    {
        var expected = format.ToFileExtension();
        var current = GetFileExtension(filePath);
        if (current.Length == 0)
        {
            return filePath + expected;
        }

        return string.Equals(current, expected, StringComparison.OrdinalIgnoreCase)
            ? filePath
            : filePath.Substring(0, filePath.Length - current.Length) + expected;
    }

    /// <summary>
    /// Resolves the WAV target used when MP3 encoding fails and the recording is
    /// rescued. Never overwrites an existing WAV: "meeting.wav" becomes
    /// "meeting (2).wav", "meeting (3).wav" and so on.
    /// </summary>
    public static string ResolveRescueWavPath(string mp3Path)
    {
        var directory = Path.GetDirectoryName(mp3Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = ".";
        }

        var baseName = Path.GetFileNameWithoutExtension(mp3Path);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "recording";
        }

        var candidate = Path.Combine(directory, baseName + OutputFormat.Wav.ToFileExtension());
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName} ({suffix})" + OutputFormat.Wav.ToFileExtension());
            suffix++;
        }

        return candidate;
    }

    private static string GetFileExtension(string filePath)
    {
        var lastDot = filePath.LastIndexOf('.');
        var lastSlash = Math.Max(filePath.LastIndexOf('/'), filePath.LastIndexOf('\\'));
        if (lastDot < 0 || lastDot <= lastSlash)
        {
            return "";
        }

        return filePath.Substring(lastDot);
    }
}