namespace ELARA;

/// <summary>
/// Pure, testable text descriptions for WAVE format tags and WAVE_FORMAT_
/// EXTENSIBLE sub-format GUIDs. Shared by the endpoint diagnostics and the
/// initialized-capture-format reporting so both always describe formats alike.
/// </summary>
internal static class AudioFormatText
{
    public const ushort PcmTag = 1;
    public const ushort IeeeFloatTag = 3;
    public const ushort ExtensibleTag = 0xFFFE;

    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT / KSDATAFORMAT_SUBTYPE_PCM.
    public static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");
    public static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00AA00389B71");

    public static string ForTagAndBits(ushort formatTag, ushort bitsPerSample)
    {
        return formatTag switch
        {
            IeeeFloatTag => "32-bit float",
            PcmTag => $"{bitsPerSample}-bit PCM",
            ExtensibleTag => "extensible",
            _ => $"format tag 0x{formatTag:X4}",
        };
    }

    public static string ForExtensibleSubFormat(Guid subFormat, ushort bitsPerSample)
    {
        if (subFormat == IeeeFloatSubFormat)
        {
            return "32-bit float";
        }

        if (subFormat == PcmSubFormat)
        {
            return $"{bitsPerSample}-bit PCM";
        }

        return $"extensible {subFormat:B}";
    }
}