namespace ELARA;

/// <summary>
/// Vendor-neutral quality classification for capture endpoints. ELARA never
/// changes device configuration or switches endpoints because of this
/// classification: it is a read-only warning heuristic based on the endpoint's
/// reported form factor/name and on the actual format, never on a vendor name
/// and never on a blanket transport assumption (transport alone does not imply
/// quality).
/// </summary>
internal static class EndpointQualityClassifier
{
    /// <summary>Name hints for telephony-style capture profiles. These are
    /// neutral feature terms used by Windows endpoint naming, not vendor
    /// identifiers.</summary>
    private static readonly string[] TelephonyNameHints =
    [
        "hands-free",
        "handsfree",
        "hands free",
        "ag audio",
        "headset",
        "communications",
        "chat",
        "telephony",
    ];

    /// <summary>
    /// True when the endpoint looks like a telephony/hands-free style capture
    /// (form factor Headset/Handset, or a matching neutral name hint). Such
    /// endpoints can expose reduced-bandwidth capture formats.
    /// </summary>
    public static bool IsTelephonyStyleEndpoint(string? formFactor, string? friendlyName)
    {
        if (formFactor is "Headset" or "Handset")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return false;
        }

        foreach (var hint in TelephonyNameHints)
        {
            if (friendlyName.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Warning (or hint) derived from the actual capture format sample rate.
    /// Returns null when the rate does not indicate a bandwidth limitation.
    /// 8/16 kHz: clear low-bandwidth warning. 32 kHz: hint. 44.1/48 kHz and
    /// above: no warning — the transport alone says nothing about quality.
    /// </summary>
    public static string? BuildLowBandwidthWarning(int sampleRateHz)
    {
        return sampleRateHz switch
        {
            <= 0 => null,
            <= 16_000 => "Low-bandwidth capture format detected (8/16 kHz). Speech detail may be reduced compared to higher-rate endpoints.",
            <= 32_000 => "Capture format below 44.1 kHz. Speech detail may be slightly reduced compared to higher-rate endpoints.",
            _ => null,
        };
    }

    /// <summary>Neutral warning text for a telephony-style endpoint.</summary>
    public const string TelephonyWarning =
        "Possible telephony / hands-free endpoint. Capture quality may be reduced. If available, select a higher-quality microphone endpoint.";
}