using System.Drawing;

namespace ELARA;

/// <summary>
/// Themes selectable in Options → Appearance. <see cref="AppTheme.System"/> follows
/// the Windows app light/dark mode; every other value picks a fixed ELARA palette.
/// </summary>
internal enum AppTheme
{
    System = 0,
    Midnight,
    Graphite,
    Light,
    Ocean,
    Teal,
    Ember,
    Rose,
}

/// <summary>Semantic status roles used to pick status colors from the active palette.</summary>
internal enum StatusKind
{
    Idle = 0,
    Starting,
    Recording,
    Saving,
    Saved,
    Blocked,
    Error,
}

/// <summary>
/// Central UI color tokens for one theme. Every painted surface of ELARA (main
/// window, dialogs, custom controls, menus) reads its colors from here so a
/// theme switch is a pure color swap without layout or behaviour changes.
/// </summary>
internal sealed record UiThemePalette
{
    public required string DisplayName { get; init; }

    public required bool IsDark { get; init; }

    public required Color WindowBack { get; init; }

    public required Color FieldBack { get; init; }

    public required Color FieldBackHover { get; init; }

    public required Color FieldBorder { get; init; }

    public required Color FieldBorderHover { get; init; }

    public required Color FieldBorderDisabled { get; init; }

    public required Color FieldText { get; init; }

    public required Color FieldTextDisabled { get; init; }

    public required Color TextTitle { get; init; }

    public required Color TextHeader { get; init; }

    public required Color TextMuted { get; init; }

    public required Color TextMutedDisabled { get; init; }

    public required Color ButtonBack { get; init; }

    public required Color ButtonBorder { get; init; }

    public required Color ButtonText { get; init; }

    public required Color Accent { get; init; }

    public required Color AccentEnd { get; init; }

    public required Color AccentSoft { get; init; }

    public required Color AccentSoftHover { get; init; }

    public required Color AccentBorderHover { get; init; }

    public required Color TextOnAccent { get; init; }

    public required Color LinkColor { get; init; }

    public required Color LinkActiveColor { get; init; }

    public required Color MeterIdle { get; init; }

    public required Color DisabledBlend { get; init; }

    public required Color StopButtonStart { get; init; }

    public required Color StopButtonEnd { get; init; }

    public required Color StatusIdle { get; init; }

    public required Color StatusRecording { get; init; }

    public required Color StatusSaving { get; init; }

    public required Color StatusSaved { get; init; }

    public required Color StatusBlocked { get; init; }

    public required Color MenuBack { get; init; }

    public required Color MenuBorder { get; init; }

    public required Color MenuHover { get; init; }

    public required Color MenuText { get; init; }

    public required Color MenuSeparator { get; init; }

    public Color StatusColor(StatusKind kind) => kind switch
    {
        StatusKind.Recording or StatusKind.Error => StatusRecording,
        StatusKind.Starting or StatusKind.Saving => StatusSaving,
        StatusKind.Saved => StatusSaved,
        StatusKind.Blocked => StatusBlocked,
        _ => StatusIdle,
    };
}
/// <summary>
/// Pure theme definitions and resolution. Windows integration (registry, live
/// change detection, title bar, menus) lives in ThemeManager; this file stays
/// free of Windows-only APIs so it can be unit-tested anywhere.
/// </summary>
internal static class UiTheme
{
    private static readonly UiThemePalette Midnight = new()
    {
        DisplayName = "Midnight",
        IsDark = true,
        WindowBack = Color.FromArgb(13, 17, 39),
        FieldBack = Color.FromArgb(24, 29, 56),
        FieldBackHover = Color.FromArgb(31, 37, 68),
        FieldBorder = Color.FromArgb(49, 57, 96),
        FieldBorderHover = Color.FromArgb(122, 99, 255),
        FieldBorderDisabled = Color.FromArgb(38, 44, 74),
        FieldText = Color.FromArgb(235, 238, 248),
        FieldTextDisabled = Color.FromArgb(105, 111, 146),
        TextTitle = Color.FromArgb(247, 248, 255),
        TextHeader = Color.FromArgb(195, 201, 231),
        TextMuted = Color.FromArgb(150, 158, 198),
        TextMutedDisabled = Color.FromArgb(90, 96, 128),
        ButtonBack = Color.FromArgb(33, 39, 76),
        ButtonBorder = Color.FromArgb(66, 73, 109),
        ButtonText = Color.White,
        Accent = Color.FromArgb(101, 77, 245),
        AccentEnd = Color.FromArgb(81, 67, 242),
        AccentSoft = Color.FromArgb(150, 136, 255),
        AccentSoftHover = Color.FromArgb(187, 176, 255),
        AccentBorderHover = Color.FromArgb(122, 99, 255),
        TextOnAccent = Color.White,
        LinkColor = Color.FromArgb(145, 136, 255),
        LinkActiveColor = Color.FromArgb(187, 176, 255),
        MeterIdle = Color.FromArgb(66, 73, 109),
        DisabledBlend = Color.FromArgb(70, 75, 110),
        StopButtonStart = Color.FromArgb(255, 98, 129),
        StopButtonEnd = Color.FromArgb(255, 88, 119),
        StatusIdle = Color.FromArgb(150, 136, 255),
        StatusRecording = Color.FromArgb(255, 142, 168),
        StatusSaving = Color.FromArgb(255, 211, 122),
        StatusSaved = Color.FromArgb(110, 230, 182),
        StatusBlocked = Color.FromArgb(255, 196, 120),
        MenuBack = Color.FromArgb(20, 25, 50),
        MenuBorder = Color.FromArgb(66, 73, 109),
        MenuHover = Color.FromArgb(35, 42, 80),
        MenuText = Color.FromArgb(235, 238, 248),
        MenuSeparator = Color.FromArgb(52, 60, 96),
    };



    /// <summary>Neutral dark palette used when Theme=System and Windows is in dark mode.</summary>
    private static readonly UiThemePalette SystemDark = new()
    {
        DisplayName = "System (dark)",
        IsDark = true,
        WindowBack = Color.FromArgb(32, 33, 36),
        FieldBack = Color.FromArgb(44, 45, 50),
        FieldBackHover = Color.FromArgb(54, 55, 61),
        FieldBorder = Color.FromArgb(68, 70, 76),
        FieldBorderHover = Color.FromArgb(124, 101, 255),
        FieldBorderDisabled = Color.FromArgb(54, 55, 60),
        FieldText = Color.FromArgb(238, 239, 242),
        FieldTextDisabled = Color.FromArgb(120, 122, 130),
        TextTitle = Color.FromArgb(250, 250, 252),
        TextHeader = Color.FromArgb(204, 205, 212),
        TextMuted = Color.FromArgb(160, 162, 170),
        TextMutedDisabled = Color.FromArgb(108, 110, 118),
        ButtonBack = Color.FromArgb(52, 53, 58),
        ButtonBorder = Color.FromArgb(86, 88, 96),
        ButtonText = Color.FromArgb(242, 243, 246),
        Accent = Color.FromArgb(101, 77, 245),
        AccentEnd = Color.FromArgb(85, 66, 232),
        AccentSoft = Color.FromArgb(154, 140, 255),
        AccentSoftHover = Color.FromArgb(190, 180, 255),
        AccentBorderHover = Color.FromArgb(124, 101, 255),
        TextOnAccent = Color.White,
        LinkColor = Color.FromArgb(154, 140, 255),
        LinkActiveColor = Color.FromArgb(190, 180, 255),
        MeterIdle = Color.FromArgb(104, 106, 114),
        DisabledBlend = Color.FromArgb(86, 88, 96),
        StopButtonStart = Color.FromArgb(255, 98, 129),
        StopButtonEnd = Color.FromArgb(255, 88, 119),
        StatusIdle = Color.FromArgb(154, 140, 255),
        StatusRecording = Color.FromArgb(255, 142, 168),
        StatusSaving = Color.FromArgb(255, 211, 122),
        StatusSaved = Color.FromArgb(110, 230, 182),
        StatusBlocked = Color.FromArgb(255, 196, 120),
        MenuBack = Color.FromArgb(44, 45, 50),
        MenuBorder = Color.FromArgb(86, 88, 96),
        MenuHover = Color.FromArgb(58, 59, 65),
        MenuText = Color.FromArgb(238, 239, 242),
        MenuSeparator = Color.FromArgb(72, 74, 80),
    };


    /// <summary>ELARA light palette; also used when Theme=System and Windows is in light mode.</summary>
    private static readonly UiThemePalette Light = new()
    {
        DisplayName = "Light",
        IsDark = false,
        WindowBack = Color.FromArgb(243, 245, 250),
        FieldBack = Color.White,
        FieldBackHover = Color.FromArgb(238, 241, 249),
        FieldBorder = Color.FromArgb(204, 209, 224),
        FieldBorderHover = Color.FromArgb(122, 99, 255),
        FieldBorderDisabled = Color.FromArgb(216, 220, 232),
        FieldText = Color.FromArgb(28, 32, 52),
        FieldTextDisabled = Color.FromArgb(148, 153, 173),
        TextTitle = Color.FromArgb(22, 25, 46),
        TextHeader = Color.FromArgb(74, 82, 114),
        TextMuted = Color.FromArgb(108, 116, 146),
        TextMutedDisabled = Color.FromArgb(158, 163, 182),
        ButtonBack = Color.FromArgb(233, 236, 245),
        ButtonBorder = Color.FromArgb(198, 203, 220),
        ButtonText = Color.FromArgb(42, 48, 76),
        Accent = Color.FromArgb(95, 74, 238),
        AccentEnd = Color.FromArgb(78, 60, 224),
        AccentSoft = Color.FromArgb(104, 86, 242),
        AccentSoftHover = Color.FromArgb(74, 56, 222),
        AccentBorderHover = Color.FromArgb(122, 99, 255),
        TextOnAccent = Color.White,
        LinkColor = Color.FromArgb(104, 86, 242),
        LinkActiveColor = Color.FromArgb(74, 56, 222),
        MeterIdle = Color.FromArgb(198, 203, 220),
        DisabledBlend = Color.FromArgb(202, 206, 220),
        StopButtonStart = Color.FromArgb(226, 76, 102),
        StopButtonEnd = Color.FromArgb(214, 66, 92),
        StatusIdle = Color.FromArgb(104, 86, 242),
        StatusRecording = Color.FromArgb(198, 32, 72),
        StatusSaving = Color.FromArgb(172, 110, 0),
        StatusSaved = Color.FromArgb(0, 148, 108),
        StatusBlocked = Color.FromArgb(168, 88, 0),
        MenuBack = Color.White,
        MenuBorder = Color.FromArgb(214, 218, 230),
        MenuHover = Color.FromArgb(238, 241, 249),
        MenuText = Color.FromArgb(28, 32, 52),
        MenuSeparator = Color.FromArgb(214, 218, 230),
    };


    private static readonly UiThemePalette Graphite = new()
    {
        DisplayName = "Graphite",
        IsDark = true,
        WindowBack = Color.FromArgb(25, 26, 29),
        FieldBack = Color.FromArgb(38, 40, 44),
        FieldBackHover = Color.FromArgb(48, 50, 55),
        FieldBorder = Color.FromArgb(64, 67, 73),
        FieldBorderHover = Color.FromArgb(152, 162, 250),
        FieldBorderDisabled = Color.FromArgb(50, 52, 57),
        FieldText = Color.FromArgb(236, 238, 241),
        FieldTextDisabled = Color.FromArgb(114, 117, 124),
        TextTitle = Color.FromArgb(249, 250, 252),
        TextHeader = Color.FromArgb(200, 202, 209),
        TextMuted = Color.FromArgb(158, 161, 168),
        TextMutedDisabled = Color.FromArgb(98, 101, 108),
        ButtonBack = Color.FromArgb(50, 52, 57),
        ButtonBorder = Color.FromArgb(84, 87, 94),
        ButtonText = Color.FromArgb(243, 244, 247),
        Accent = Color.FromArgb(129, 140, 248),
        AccentEnd = Color.FromArgb(110, 121, 240),
        AccentSoft = Color.FromArgb(167, 175, 255),
        AccentSoftHover = Color.FromArgb(200, 205, 255),
        AccentBorderHover = Color.FromArgb(152, 162, 250),
        TextOnAccent = Color.White,
        LinkColor = Color.FromArgb(167, 175, 255),
        LinkActiveColor = Color.FromArgb(200, 205, 255),
        MeterIdle = Color.FromArgb(96, 99, 106),
        DisabledBlend = Color.FromArgb(92, 95, 102),
        StopButtonStart = Color.FromArgb(255, 98, 129),
        StopButtonEnd = Color.FromArgb(255, 88, 119),
        StatusIdle = Color.FromArgb(167, 175, 255),
        StatusRecording = Color.FromArgb(255, 139, 158),
        StatusSaving = Color.FromArgb(255, 203, 112),
        StatusSaved = Color.FromArgb(104, 226, 181),
        StatusBlocked = Color.FromArgb(255, 192, 110),
        MenuBack = Color.FromArgb(38, 40, 44),
        MenuBorder = Color.FromArgb(84, 87, 94),
        MenuHover = Color.FromArgb(54, 56, 62),
        MenuText = Color.FromArgb(236, 238, 241),
        MenuSeparator = Color.FromArgb(74, 77, 84),
    };

    private static readonly UiThemePalette Ocean = new()
    {
        DisplayName = "Ocean",
        IsDark = true,
        WindowBack = Color.FromArgb(10, 23, 40),
        FieldBack = Color.FromArgb(17, 35, 58),
        FieldBackHover = Color.FromArgb(23, 45, 72),
        FieldBorder = Color.FromArgb(42, 69, 104),
        FieldBorderHover = Color.FromArgb(102, 192, 250),
        FieldBorderDisabled = Color.FromArgb(31, 53, 82),
        FieldText = Color.FromArgb(228, 240, 252),
        FieldTextDisabled = Color.FromArgb(92, 116, 144),
        TextTitle = Color.FromArgb(241, 249, 255),
        TextHeader = Color.FromArgb(170, 198, 226),
        TextMuted = Color.FromArgb(126, 154, 188),
        TextMutedDisabled = Color.FromArgb(78, 102, 132),
        ButtonBack = Color.FromArgb(25, 48, 75),
        ButtonBorder = Color.FromArgb(57, 88, 126),
        ButtonText = Color.FromArgb(241, 249, 255),
        Accent = Color.FromArgb(72, 170, 240),
        AccentEnd = Color.FromArgb(54, 146, 226),
        AccentSoft = Color.FromArgb(132, 202, 255),
        AccentSoftHover = Color.FromArgb(178, 224, 255),
        AccentBorderHover = Color.FromArgb(102, 192, 250),
        TextOnAccent = Color.FromArgb(10, 23, 40),
        LinkColor = Color.FromArgb(132, 202, 255),
        LinkActiveColor = Color.FromArgb(178, 224, 255),
        MeterIdle = Color.FromArgb(54, 82, 116),
        DisabledBlend = Color.FromArgb(72, 98, 130),
        StopButtonStart = Color.FromArgb(255, 98, 129),
        StopButtonEnd = Color.FromArgb(255, 88, 119),
        StatusIdle = Color.FromArgb(132, 202, 255),
        StatusRecording = Color.FromArgb(255, 132, 150),
        StatusSaving = Color.FromArgb(255, 205, 110),
        StatusSaved = Color.FromArgb(96, 228, 180),
        StatusBlocked = Color.FromArgb(255, 192, 110),
        MenuBack = Color.FromArgb(17, 35, 58),
        MenuBorder = Color.FromArgb(57, 88, 126),
        MenuHover = Color.FromArgb(27, 52, 82),
        MenuText = Color.FromArgb(228, 240, 252),
        MenuSeparator = Color.FromArgb(48, 76, 110),
    };



    private static readonly UiThemePalette Teal = new()
    {
        DisplayName = "Teal",
        IsDark = true,
        WindowBack = Color.FromArgb(13, 31, 33),
        FieldBack = Color.FromArgb(19, 45, 48),
        FieldBackHover = Color.FromArgb(25, 57, 61),
        FieldBorder = Color.FromArgb(46, 82, 86),
        FieldBorderHover = Color.FromArgb(92, 216, 190),
        FieldBorderDisabled = Color.FromArgb(34, 64, 68),
        FieldText = Color.FromArgb(229, 246, 244),
        FieldTextDisabled = Color.FromArgb(94, 126, 122),
        TextTitle = Color.FromArgb(241, 252, 250),
        TextHeader = Color.FromArgb(170, 208, 202),
        TextMuted = Color.FromArgb(126, 166, 160),
        TextMutedDisabled = Color.FromArgb(78, 110, 106),
        ButtonBack = Color.FromArgb(25, 56, 59),
        ButtonBorder = Color.FromArgb(58, 96, 100),
        ButtonText = Color.FromArgb(241, 252, 250),
        Accent = Color.FromArgb(38, 198, 168),
        AccentEnd = Color.FromArgb(28, 172, 144),
        AccentSoft = Color.FromArgb(130, 232, 208),
        AccentSoftHover = Color.FromArgb(182, 244, 226),
        AccentBorderHover = Color.FromArgb(92, 216, 190),
        TextOnAccent = Color.FromArgb(13, 31, 33),
        LinkColor = Color.FromArgb(130, 232, 208),
        LinkActiveColor = Color.FromArgb(182, 244, 226),
        MeterIdle = Color.FromArgb(54, 92, 94),
        DisabledBlend = Color.FromArgb(80, 110, 112),
        StopButtonStart = Color.FromArgb(255, 98, 129),
        StopButtonEnd = Color.FromArgb(255, 88, 119),
        StatusIdle = Color.FromArgb(130, 232, 208),
        StatusRecording = Color.FromArgb(255, 132, 150),
        StatusSaving = Color.FromArgb(255, 205, 110),
        StatusSaved = Color.FromArgb(96, 232, 190),
        StatusBlocked = Color.FromArgb(255, 192, 110),
        MenuBack = Color.FromArgb(19, 45, 48),
        MenuBorder = Color.FromArgb(58, 96, 100),
        MenuHover = Color.FromArgb(29, 64, 68),
        MenuText = Color.FromArgb(229, 246, 244),
        MenuSeparator = Color.FromArgb(50, 88, 92),
    };

    private static readonly UiThemePalette Ember = new()
    {
        DisplayName = "Ember",
        IsDark = true,
        WindowBack = Color.FromArgb(33, 23, 18),
        FieldBack = Color.FromArgb(49, 34, 27),
        FieldBackHover = Color.FromArgb(61, 42, 33),
        FieldBorder = Color.FromArgb(94, 64, 50),
        FieldBorderHover = Color.FromArgb(248, 142, 92),
        FieldBorderDisabled = Color.FromArgb(72, 50, 40),
        FieldText = Color.FromArgb(248, 239, 231),
        FieldTextDisabled = Color.FromArgb(142, 118, 102),
        TextTitle = Color.FromArgb(252, 247, 241),
        TextHeader = Color.FromArgb(224, 198, 182),
        TextMuted = Color.FromArgb(188, 160, 142),
        TextMutedDisabled = Color.FromArgb(122, 100, 86),
        ButtonBack = Color.FromArgb(63, 44, 35),
        ButtonBorder = Color.FromArgb(110, 78, 60),
        ButtonText = Color.FromArgb(252, 247, 241),
        Accent = Color.FromArgb(233, 112, 66),
        AccentEnd = Color.FromArgb(212, 94, 52),
        AccentSoft = Color.FromArgb(255, 162, 112),
        AccentSoftHover = Color.FromArgb(255, 198, 162),
        AccentBorderHover = Color.FromArgb(248, 142, 92),
        TextOnAccent = Color.White,
        LinkColor = Color.FromArgb(255, 162, 112),
        LinkActiveColor = Color.FromArgb(255, 198, 162),
        MeterIdle = Color.FromArgb(106, 82, 68),
        DisabledBlend = Color.FromArgb(118, 92, 78),
        StopButtonStart = Color.FromArgb(255, 98, 129),
        StopButtonEnd = Color.FromArgb(255, 88, 119),
        StatusIdle = Color.FromArgb(255, 162, 112),
        StatusRecording = Color.FromArgb(255, 132, 150),
        StatusSaving = Color.FromArgb(255, 205, 110),
        StatusSaved = Color.FromArgb(110, 230, 182),
        StatusBlocked = Color.FromArgb(255, 196, 120),
        MenuBack = Color.FromArgb(49, 34, 27),
        MenuBorder = Color.FromArgb(110, 78, 60),
        MenuHover = Color.FromArgb(72, 50, 40),
        MenuText = Color.FromArgb(248, 239, 231),
        MenuSeparator = Color.FromArgb(96, 68, 54),
    };



    private static readonly UiThemePalette Rose = new()
    {
        DisplayName = "Rose",
        IsDark = true,
        WindowBack = Color.FromArgb(35, 19, 29),
        FieldBack = Color.FromArgb(51, 29, 43),
        FieldBackHover = Color.FromArgb(63, 37, 53),
        FieldBorder = Color.FromArgb(96, 56, 78),
        FieldBorderHover = Color.FromArgb(246, 122, 166),
        FieldBorderDisabled = Color.FromArgb(74, 44, 60),
        FieldText = Color.FromArgb(250, 237, 243),
        FieldTextDisabled = Color.FromArgb(144, 114, 128),
        TextTitle = Color.FromArgb(252, 245, 249),
        TextHeader = Color.FromArgb(226, 188, 206),
        TextMuted = Color.FromArgb(190, 154, 172),
        TextMutedDisabled = Color.FromArgb(124, 98, 112),
        ButtonBack = Color.FromArgb(65, 40, 54),
        ButtonBorder = Color.FromArgb(112, 70, 90),
        ButtonText = Color.FromArgb(252, 245, 249),
        Accent = Color.FromArgb(233, 86, 134),
        AccentEnd = Color.FromArgb(210, 68, 116),
        AccentSoft = Color.FromArgb(255, 152, 188),
        AccentSoftHover = Color.FromArgb(255, 198, 218),
        AccentBorderHover = Color.FromArgb(246, 122, 166),
        TextOnAccent = Color.White,
        LinkColor = Color.FromArgb(255, 152, 188),
        LinkActiveColor = Color.FromArgb(255, 198, 218),
        MeterIdle = Color.FromArgb(110, 74, 92),
        DisabledBlend = Color.FromArgb(120, 90, 106),
        StopButtonStart = Color.FromArgb(255, 98, 129),
        StopButtonEnd = Color.FromArgb(255, 88, 119),
        StatusIdle = Color.FromArgb(255, 152, 188),
        StatusRecording = Color.FromArgb(255, 110, 140),
        StatusSaving = Color.FromArgb(255, 205, 110),
        StatusSaved = Color.FromArgb(110, 230, 182),
        StatusBlocked = Color.FromArgb(255, 196, 120),
        MenuBack = Color.FromArgb(51, 29, 43),
        MenuBorder = Color.FromArgb(112, 70, 90),
        MenuHover = Color.FromArgb(74, 46, 62),
        MenuText = Color.FromArgb(250, 237, 243),
        MenuSeparator = Color.FromArgb(98, 62, 80),
    };

    /// <summary>Resolves a theme selection to a concrete palette; System follows Windows.</summary>
    public static UiThemePalette Resolve(AppTheme theme, bool windowsDark)
    {
        return theme switch
        {
            AppTheme.Midnight => Midnight,
            AppTheme.Graphite => Graphite,
            AppTheme.Light => Light,
            AppTheme.Ocean => Ocean,
            AppTheme.Teal => Teal,
            AppTheme.Ember => Ember,
            AppTheme.Rose => Rose,
            _ => windowsDark ? SystemDark : Light,
        };
    }

    /// <summary>Parses the persisted theme value; unknown or empty values fall back to System.</summary>
    public static AppTheme ParseTheme(string? value)
    {
        return Enum.TryParse<AppTheme>(value, ignoreCase: true, out var theme)
            ? theme
            : AppTheme.System;
    }

    /// <summary>Human-readable theme name for the Options selector.</summary>
    public static string GetThemeDisplayName(AppTheme theme)
    {
        return theme == AppTheme.System ? "System (Windows)" : theme.ToString();
    }

    /// <summary>
    /// Returns a copy of the palette whose accent-derived tokens come from the
    /// Windows accent color; structural colors stay from the theme.
    /// </summary>
    public static UiThemePalette WithWindowsAccent(UiThemePalette palette, Color accent)
    {
        accent = Color.FromArgb(255, accent);
        var softTarget = palette.IsDark ? Color.White : Color.Black;
        var soft = Blend(accent, softTarget, 0.38F);
        var softHover = Blend(accent, softTarget, 0.62F);
        return palette with
        {
            Accent = accent,
            AccentEnd = Blend(accent, Color.Black, palette.IsDark ? 0.12F : 0.16F),
            AccentSoft = soft,
            AccentSoftHover = softHover,
            AccentBorderHover = palette.IsDark ? Blend(accent, Color.White, 0.18F) : Blend(accent, Color.Black, 0.14F),
            TextOnAccent = UiTheme.Luminance(accent) >= 0.62F ? Color.FromArgb(28, 28, 32) : Color.White,
            LinkColor = soft,
            LinkActiveColor = softHover,
            StatusIdle = soft,
        };
    }

    internal static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0F, 1F);
        var r = (int)(from.R + (to.R - from.R) * amount);
        var g = (int)(from.G + (to.G - from.G) * amount);
        var b = (int)(from.B + (to.B - from.B) * amount);
        return Color.FromArgb(r, g, b);
    }

    internal static float Luminance(Color color)
    {
        return (0.299F * color.R + 0.587F * color.G + 0.114F * color.B) / 255F;
    }
}
