namespace ELARA;

/// <summary>
/// Pure version-string parsing shared by the UI and the test harness.
/// The UI shows only the first three numeric components ("1.0.1") and never a
/// build suffix, even when the informational version carries one ("1.0.1+abcdef")
/// or a four-part assembly version ("1.0.1.0").
/// </summary>
internal static class VersionText
{
    public static string ParseDisplayVersion(string raw)
    {
        var clean = string.IsNullOrWhiteSpace(raw) ? "0" : raw;

        // Strip any build-metadata suffix ("1.0.1+abcdef").
        var plus = clean.IndexOf('+');
        if (plus >= 0)
        {
            clean = clean.Substring(0, plus);
        }

        var parts = clean.Split('.');
        var relevant = new string[3];
        for (var index = 0; index < relevant.Length; index++)
        {
            if (index < parts.Length && !string.IsNullOrWhiteSpace(parts[index]))
            {
                relevant[index] = parts[index].Trim();
            }
            else
            {
                relevant[index] = "0";
            }
        }

        return string.Join(".", relevant);
    }
}