namespace ELARA;

using System.Drawing.Drawing2D;

/// <summary>
/// Loads the single ELARA application icon (assets/elara.ico) for the main
/// window, the tray icon and the About dialog. Falls back to a tiny drawn icon
/// if the asset is missing, so the app never crashes over an icon.
/// </summary>
internal static class AppIcon
{
    /// <summary>The icon asset path bundled next to the executable.</summary>
    public static string IconFilePath { get; } = ResolveIconFilePath();

    public static Icon Load()
    {
        try
        {
            if (File.Exists(IconFilePath))
            {
                return new Icon(IconFilePath);
            }

            AppLogger.Warn($"Application icon asset not found at '{IconFilePath}'; using the drawn fallback icon.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not load the application icon from '{IconFilePath}'; using the drawn fallback icon. {ex.Message}");
        }

        return DrawFallbackIcon();
    }

    private static string ResolveIconFilePath()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "elara.ico"),
            Path.Combine(AppContext.BaseDirectory, "elara.ico"),
        })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "assets", "elara.ico");
    }

    private static Icon DrawFallbackIcon()
    {
        // Matches the asset design: dark navy/indigo square with a violet waveform.
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = new SolidBrush(Color.FromArgb(13, 17, 39));
            using var waveform = new SolidBrush(Color.FromArgb(101, 77, 245));
            graphics.FillRectangle(background, 1, 1, 30, 30);
            for (var x = 2; x < 30; x++)
            {
                var t = (x - 2) / 27F;
                var center = 16 + MathF.Sin(t * MathF.PI * 5F) * 6F;
                graphics.FillRectangle(waveform, x, (int)center - 2, 1, 4);
            }
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}