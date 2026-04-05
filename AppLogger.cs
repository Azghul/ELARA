using System.Text;

namespace SimpleAudioRecorder;

internal static class AppLogger
{
    private static readonly object SyncRoot = new();
    private static bool initialized;

    public static string RootDirectory { get; } = Path.GetDirectoryName(AppPaths.LogDirectory) ?? AppPaths.LogDirectory;

    public static string LogDirectory { get; } = AppPaths.LogDirectory;

    public static string CurrentLogFilePath { get; private set; } = string.Empty;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (initialized)
            {
                return;
            }

            Directory.CreateDirectory(LogDirectory);
            CurrentLogFilePath = Path.Combine(LogDirectory, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}.log");
            initialized = true;

            WriteCore("INFO", $"Logger initialized. Version={Application.ProductVersion}; OS={Environment.OSVersion.VersionString}; ProcessId={Environment.ProcessId}");
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public static void Debug(string message) => Write("DEBUG", message);

    private static void Write(string level, string message, Exception? exception = null)
    {
        lock (SyncRoot)
        {
            if (!initialized)
            {
                Initialize();
            }

            WriteCore(level, message, exception);
        }
    }

    private static void WriteCore(string level, string message, Exception? exception = null)
    {
        Directory.CreateDirectory(LogDirectory);

        var builder = new StringBuilder();
        builder.Append('[')
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append("] [")
            .Append(level)
            .Append("] ")
            .AppendLine(message);

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        File.AppendAllText(CurrentLogFilePath, builder.ToString(), Encoding.UTF8);
    }
}
