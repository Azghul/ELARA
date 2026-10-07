namespace ELARA;

internal sealed record UiPreviewState(
    string Slug,
    string Title,
    CaptureMode Mode,
    bool IsRecording,
    string TimerText,
    string? StatusText,
    StatusKind Status,
    string? StatusDetails,
    IReadOnlyList<float> Levels)
{
    public static IReadOnlyList<UiPreviewState> CreateDefaults(string previewRoot)
    {
        return
        [
            new UiPreviewState(
                "idle-both",
                "Idle both capture",
                CaptureMode.Both,
                false,
                "00:00",
                null,
                StatusKind.Idle,
                null,
                [0.08F, 0.10F, 0.13F, 0.12F, 0.15F, 0.10F, 0.09F, 0.11F, 0.12F, 0.09F, 0.08F]),
            new UiPreviewState(
                "recording-both",
                "Active mixed recording",
                CaptureMode.Both,
                true,
                "12:48",
                "Recording",
                StatusKind.Recording,
                "Both capture is recording.",
                [0.22F, 0.38F, 0.61F, 0.47F, 0.71F, 0.82F, 0.68F, 0.49F, 0.33F, 0.55F, 0.41F]),
            new UiPreviewState(
                "saved-mic",
                "Saved microphone recording",
                CaptureMode.Microphone,
                false,
                "00:00",
                "Saved",
                StatusKind.Saved,
                "Saved meeting-mic-01.wav",
                [0.08F, 0.09F, 0.10F, 0.11F, 0.10F, 0.08F, 0.09F, 0.10F, 0.09F, 0.08F, 0.09F]),
            new UiPreviewState(
                "blocked-system",
                "System capture blocked",
                CaptureMode.System,
                false,
                "00:00",
                "Blocked",
                StatusKind.Blocked,
                "Could not start system audio capture. Check that a playback device is available and not in exclusive use.",
                [0.08F, 0.08F, 0.10F, 0.09F, 0.08F, 0.10F, 0.09F, 0.08F, 0.09F, 0.08F, 0.08F]),
        ];
    }
}
