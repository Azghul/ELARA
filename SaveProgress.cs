namespace ELARA;

/// <summary>Phases of the stop/save pipeline that are surfaced to the UI.</summary>
public enum SaveStage
{
    Stopping = 0,
    Mixing,
    Encoding,
    Finalizing,
}

/// <summary>
/// Immutable progress notification for the stop/save pipeline. A null
/// <see cref="Percent"/> means the operation is indeterminate.
/// </summary>
public readonly record struct SaveProgress
{
    public SaveProgress(SaveStage stage, int? percent, string? message)
    {
        Stage = stage;
        Percent = percent is null ? null : Math.Clamp(percent.Value, 0, 100);
        Message = message ?? string.Empty;
    }

    public SaveStage Stage { get; }

    public int? Percent { get; }

    public string Message { get; }

    public static SaveProgress Indeterminate(SaveStage stage, string? message) => new(stage, null, message);

    public static SaveProgress Determinate(SaveStage stage, int percent, string? message) => new(stage, percent, message);
}

/// <summary>
/// Throttles progress notifications: every stage change is reported, but a
/// percentage is only forwarded when it changes by at least one point. This
/// keeps the UI responsive without flooding it with per-sample messages.
/// </summary>
internal sealed class SaveProgressReporter
{
    private readonly IProgress<SaveProgress>? progress;
    private SaveStage? lastStage;
    private int? lastPercent;

    public SaveProgressReporter(IProgress<SaveProgress>? progress)
    {
        this.progress = progress;
    }

    public void Report(SaveStage stage, int? percent, string? message)
    {
        if (progress is null)
        {
            return;
        }

        int? clamped = percent is null ? null : Math.Clamp(percent.Value, 0, 100);
        if (lastStage == stage)
        {
            // Indeterminate stages are announced once; determinate stages only
            // when the visible percentage actually changes.
            if (clamped is null)
            {
                return;
            }

            if (lastPercent is not null && Math.Abs(clamped.Value - lastPercent.Value) < 1)
            {
                return;
            }
        }

        lastStage = stage;
        lastPercent = clamped;
        progress.Report(new SaveProgress(stage, clamped, message));
    }
}

/// <summary>
/// Accumulates the separate stop/save timing measurements. Encode helpers
/// report their own mixing/writing/encoding durations; the capture service
/// owns stopping, classification, validation, cleanup and the total.
/// </summary>
internal sealed class SaveTimings
{
    public TimeSpan StopSources { get; set; }

    public TimeSpan Classification { get; set; }

    public TimeSpan Mixing { get; set; }

    public TimeSpan WavWriting { get; set; }

    public TimeSpan Mp3Encoding { get; set; }

    public TimeSpan Validation { get; set; }

    public TimeSpan Cleanup { get; set; }

    public void AddEncodeTimings(SaveTimings other)
    {
        Mixing += other.Mixing;
        WavWriting += other.WavWriting;
        Mp3Encoding += other.Mp3Encoding;
    }
}
