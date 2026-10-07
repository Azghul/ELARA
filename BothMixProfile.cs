namespace ELARA;

/// <summary>
/// Pure mixing policy for Both mode (system audio + microphone). Both sources
/// are mixed 50/50 while both carry a usable signal. When one entire source
/// contains no usable signal, the active source is used at unity so its level
/// is not halved. No gain is ever boosted beyond unity.
/// </summary>
internal static class BothMixProfile
{
    public const float UnityGain = 1F;
    public const float HalfGain = 0.5F;
    public const float SilenceGain = 0F;

    public static (float SystemGain, float MicrophoneGain) SelectMixGains(
        bool systemHasSignal,
        bool microphoneHasSignal)
    {
        return (systemHasSignal, microphoneHasSignal) switch
        {
            (true, true) => (HalfGain, HalfGain),
            (true, false) => (UnityGain, SilenceGain),
            (false, true) => (SilenceGain, UnityGain),
            _ => (SilenceGain, SilenceGain),
        };
    }

    /// <summary>
    /// Mixes one frame. The 50/50 path uses the established MixToMono rounding;
    /// a unity path passes the active sample through unchanged; silence yields 0.
    /// </summary>
    public static short MixFrame(
        short systemSample,
        short microphoneSample,
        float systemGain,
        float microphoneGain)
    {
        if (systemGain == SilenceGain && microphoneGain == UnityGain)
        {
            return microphoneSample;
        }

        if (systemGain == UnityGain && microphoneGain == SilenceGain)
        {
            return systemSample;
        }

        return WavUtility.MixToMono(systemSample, microphoneSample);
    }

    /// <summary>Neutral description of the selected mix mode for logs.</summary>
    public static string DescribeMixMode(bool systemHasSignal, bool microphoneHasSignal)
    {
        return (systemHasSignal, microphoneHasSignal) switch
        {
            (true, true) => "System50Percent+Microphone50Percent",
            (true, false) => "SystemUnity",
            (false, true) => "MicrophoneUnity",
            _ => "Silence",
        };
    }
}