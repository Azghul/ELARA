namespace ELARA;

/// <summary>
/// Central definition of the recording output parameters. The capture service,
/// the MP3 encoder and the UI diagnostics must always report the same values,
/// so they are declared exactly once here and referenced everywhere else.
///
/// Target profile for D&amp;D session recordings destined for WhisperX
/// transcription: mono speech at 48 kHz, encoded a single time to CBR MP3.
/// </summary>
internal static class RecordingOutputProfile
{
    /// <summary>Output sample rate in Hz (matches the WASAPI capture request).</summary>
    public const int SampleRate = 48_000;

    /// <summary>Output channel count: mono.</summary>
    public const short Channels = 1;

    /// <summary>Bit depth of the stored PCM and of the MP3 encoder input.</summary>
    public const short BitsPerSample = 16;

    /// <summary>MP3 constant bit rate in kbit/s.</summary>
    public const int Mp3BitRateKbps = 128;

    public static string SampleRateKhz => $"{SampleRate / 1000D:0.#} kHz";

    /// <summary>Short single-line MP3 output description for the diagnostics panel.</summary>
    public static string Mp3ShortDescription =>
        $"MP3 · Mono · {SampleRateKhz} · {Mp3BitRateKbps} kbps";

    /// <summary>Short single-line WAV output description for the diagnostics panel.</summary>
    public static string WavShortDescription =>
        $"WAV · PCM16 · Mono · {SampleRateKhz}";

    /// <summary>Output description for an arbitrary output format.</summary>
    public static string DescriptionFor(OutputFormat format)
    {
        return format == OutputFormat.Mp3 ? Mp3ShortDescription : WavShortDescription;
    }
}