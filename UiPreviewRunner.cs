using System.Text.Json;

namespace SimpleAudioRecorder;

internal static class UiPreviewRunner
{
    private const string PreviewFlag = "--preview";
    private const string PreviewOutputFlag = "--preview-output";

    public static bool IsPreviewCommand(string[] args)
    {
        return args.Any(arg => string.Equals(arg, PreviewFlag, StringComparison.OrdinalIgnoreCase));
    }

    public static int Run(string[] args)
    {
        var outputDirectory = ResolveOutputDirectory(args);
        Directory.CreateDirectory(outputDirectory);
        ClearExistingArtifacts(outputDirectory);

        var states = UiPreviewState.CreateDefaults(outputDirectory);
        var manifestEntries = new List<object>(states.Count);

        foreach (var state in states)
        {
            var filePath = Path.Combine(outputDirectory, $"{state.Slug}.png");
            RenderState(filePath, state);
            manifestEntries.Add(new
            {
                state.Slug,
                state.Title,
                File = Path.GetFileName(filePath),
            });
        }

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(
            new
            {
                generatedAt = DateTimeOffset.Now,
                outputDirectory,
                previews = manifestEntries,
            },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, manifestJson);

        Console.WriteLine($"Generated {states.Count} UI preview(s) in {outputDirectory}");
        return 0;
    }

    private static void RenderState(string filePath, UiPreviewState state)
    {
        using var form = new MainForm();
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-2400, 0);
        form.ApplyPreviewState(state);
        form.Show();
        Application.DoEvents();
        form.Refresh();
        Application.DoEvents();

        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(filePath);

        form.Close();
        Application.DoEvents();
    }

    private static string ResolveOutputDirectory(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], PreviewOutputFlag, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[index + 1]);
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "ui-previews"));
    }

    private static void ClearExistingArtifacts(string outputDirectory)
    {
        foreach (var png in Directory.GetFiles(outputDirectory, "*.png"))
        {
            File.Delete(png);
        }

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        if (File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
        }
    }
}
