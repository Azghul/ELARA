namespace ELARA;

/// <summary>
/// Central application-version accessor. The UI must not hardcode the version:
/// everything reads from the actual assembly/product version.
/// </summary>
internal static class AppVersion
{
    /// <summary>The short display form shown in the UI, e.g. "1.0.1".</summary>
    public static string DisplayVersion { get; } = VersionText.ParseDisplayVersion(Application.ProductVersion);

    /// <summary>The raw product version as reported by the runtime, e.g. "1.0.1".</summary>
    public static string FullProductVersion { get; } = Application.ProductVersion;

    /// <summary>Display form prefixed for UI labels, e.g. "v1.0.1".</summary>
    public static string DisplayLabel { get; } = "v" + DisplayVersion;
}