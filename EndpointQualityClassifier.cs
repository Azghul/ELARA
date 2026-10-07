namespace ELARA;

/// <summary>
/// Vendor-neutral quality classification for capture endpoints. ELARA never
/// changes device configuration or switches endpoints because of this
/// classification: it is a read-only warning heuristic based on the actual
/// capture format and, in addition, on unambiguous technical profile names.
///
/// Deliberately NOT treated as quality indicators (they would flag high-quality
/// USB speakerphones and headsets as false positives): device-type words like
/// speakerphone, headset, chat, communications, the transport (USB/Bluetooth)
/// and the endpoint form factor alone. Warnings require a concrete indicator:
/// a low-bandwidth capture format, or an unambiguous telephony profile name
/// together with a reduced-bandwidth format.
/// </summary>
internal static class EndpointQualityClassifier
{
    /// <summary>
    /// Unambiguous technical telephony profile naming (as used by Windows for
    /// Bluetooth hands-free endpoints). Plain device-type words are
    /// deliberately excluded from this list.
    /// </summary>
    private static readonly string[] StrongTelephonyProfileHints =
    [
        "hands-free ag audio",
        "handsfree ag audio",
        "hands free ag audio",
        "hfp",
        "hsp",
    ];

    /// <summary>
    /// True only for unambiguous telephony profile naming (for example
    /// "Hands-Free AG Audio", "HFP", "HSP"). Device-type words like
    /// speakerphone, headset, chat or communications never match.
    /// </summary>
    public static bool IsStrongTelephonyProfileName(string? friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return false;
        }

        var normalized = friendlyName.ToLowerInvariant();
        foreach (var hint in StrongTelephonyProfileHints)
        {
            if (hint is "hfp" or "hsp")
            {
                if (ContainsWord(normalized, hint))
                {
                    return true;
                }
            }
            else if (normalized.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the input rate carries full speech bandwidth (>= 44.1 kHz).</summary>
    public static bool IsFullBandwidth(int sampleRateHz)
    {
        return sampleRateHz >= 44_100;
    }

    /// <summary>
    /// True when the input rate indicates reduced bandwidth (<= 32 kHz): this is
    /// the additional condition under which a telephony profile name is
    /// surfaced as a warning.
    /// </summary>
    public static bool HasReducedBandwidthIndicator(int sampleRateHz)
    {
        return sampleRateHz > 0 && sampleRateHz <= 32_000;
    }

    /// <summary>
    /// Warning (or hint) derived from the actual input sample rate. Returns null
    /// when the rate does not indicate a bandwidth limitation: 8/16 kHz produce
    /// a clear low-bandwidth warning, 32 kHz only a mild hint, 44.1 kHz and
    /// above nothing at all.
    /// </summary>
    public static string? BuildLowBandwidthWarning(int sampleRateHz)
    {
        return sampleRateHz switch
        {
            <= 0 => null,
            <= 16_000 => $"Low-bandwidth microphone input detected ({FormatKhz(sampleRateHz)}). Audio quality may be reduced.",
            <= 32_000 => $"Capture format below 44.1 kHz ({FormatKhz(sampleRateHz)}). Speech detail may be slightly reduced compared to higher-rate endpoints.",
            _ => null,
        };
    }

    /// <summary>Neutral warning text for a telephony-style endpoint.</summary>
    public const string TelephonyWarning =
        "Possible telephony / hands-free endpoint. Capture quality may be reduced. If available, select a higher-quality microphone endpoint.";

    private static bool ContainsWord(string text, string word)
    {
        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var after = index + word.Length;
            var afterOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            index = after;
        }

        return false;
    }

    private static string FormatKhz(int sampleRateHz)
    {
        return sampleRateHz % 1000 == 0
            ? $"{sampleRateHz / 1000} kHz"
            : $"{sampleRateHz / 1000D:0.#} kHz";
    }
}