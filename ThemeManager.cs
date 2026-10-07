using Microsoft.Win32;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ELARA;

/// <summary>
/// Runtime theme state: holds the selected theme (persisted setting) and the
/// effective palette, detects the Windows app light/dark mode and the Windows
/// accent color, watches for Windows theme changes at runtime and helps forms
/// apply the theme to menus and the native title bar.
/// </summary>
internal static class ThemeManager
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKeyPath = @"Software\Microsoft\Windows\DWM";

    private static bool subscribedToSystemEvents;

    public static AppTheme SelectedTheme { get; private set; } = AppTheme.System;

    public static bool UseWindowsAccentColor { get; private set; }

    public static UiThemePalette Current { get; private set; } = ResolveCurrent();

    /// <summary>Raised on the UI thread after the effective palette changed.</summary>
    public static event Action? ThemeChanged;

    /// <summary>
    /// Applies the persisted theme preferences at startup and subscribes to
    /// Windows theme changes. Call once on the UI thread.
    /// </summary>
    public static void Initialize(AppTheme theme, bool useWindowsAccentColor)
    {
        SelectedTheme = theme;
        UseWindowsAccentColor = useWindowsAccentColor;
        RefreshCurrent("initialize");

        if (subscribedToSystemEvents)
        {
            return;
        }

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            subscribedToSystemEvents = true;
        }
        catch (Exception ex)
        {
            // Live updates are a best-effort feature; ELARA still starts with the
            // resolved theme when the system event provider is unavailable.
            AppLogger.Warn($"Could not watch for Windows theme changes; theme updates need a restart. {ex.Message}");
        }
    }

    /// <summary>
    /// Switches the theme at runtime (no restart required). Used by the Options
    /// dialog for live previews and for reverting on Cancel.
    /// </summary>
    public static void SetTheme(AppTheme theme, bool useWindowsAccentColor)
    {
        SelectedTheme = theme;
        UseWindowsAccentColor = useWindowsAccentColor;
        RefreshCurrent("set-theme");
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Theme switches broadcast as WM_SETTINGCHANGE and surface here; re-read
        // the registry and only announce when the effective palette changed.
        RefreshCurrent("windows-preference");
    }

    private static void RefreshCurrent(string reason)
    {
        try
        {
            var previous = Current;
            var next = UiTheme.Resolve(SelectedTheme, IsWindowsDarkMode());
            if (UseWindowsAccentColor && GetWindowsAccentColor() is { } accent)
            {
                next = UiTheme.WithWindowsAccent(next, accent);
            }

            Current = next;
            if (EqualityComparer<UiThemePalette>.Default.Equals(previous, next))
            {
                return;
            }

            AppLogger.Info(
                $"Theme changed ({reason}). Theme={SelectedTheme}; UseWindowsAccentColor={UseWindowsAccentColor}; Effective={next.DisplayName}; WindowsDark={IsWindowsDarkMode()}");
            ThemeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Theme refresh failed.", ex);
        }
    }

    private static UiThemePalette ResolveCurrent()
    {
        var palette = UiTheme.Resolve(AppTheme.System, IsWindowsDarkMode());
        return palette;
    }

    /// <summary>
    /// Reads the Windows app mode: null when unavailable (non-Windows, older
    /// Windows or registry error), otherwise the raw AppsUseLightTheme value.
    /// </summary>
    internal static bool? ReadAppsUseLightTheme()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value != 0;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not read the Windows app light/dark mode. {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// True when Windows uses the dark app mode. Only the explicit 0 (dark) is
    /// treated as dark; anything unreadable falls back to the Windows default (light).
    /// </summary>
    public static bool IsWindowsDarkMode()
    {
        return ReadAppsUseLightTheme() is false;
    }

    /// <summary>
    /// Reads the Windows accent color (DWM store, COLORREF layout). Returns null
    /// when unavailable; the theme accent is used instead.
    /// </summary>
    internal static Color? GetWindowsAccentColor()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            using var key = Registry.CurrentUser.OpenSubKey(DwmKeyPath);
            foreach (var name in new[] { "AccentColor", "AccentColorMenu" })
            {
                if (key?.GetValue(name) is int raw)
                {
                    // Stored like a COLORREF (0x00BBGGRR) with an alpha byte that is ignored.
                    return Color.FromArgb(raw & 0xFF, (raw >> 8) & 0xFF, (raw >> 16) & 0xFF);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not read the Windows accent color. {ex.Message}");
        }

        return null;
    }

    /// <summary>Best-effort: paints the native title bar dark or light to match the theme.</summary>
    public static void ApplyTitleBarTheme(Control window)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !window.IsHandleCreated)
            {
                return;
            }

            var dark = Current.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(window.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            {
                // Pre-20H1 Windows 10 builds expose the attribute under id 19.
                var legacy = dark;
                DwmSetWindowAttribute(window.Handle, DwmwaUseImmersiveDarkModeLegacy, ref legacy, sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
            // No DWM (non-Windows platform); the title bar theme is skipped.
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not apply the title bar theme. {ex.Message}");
        }
    }

    /// <summary>
    /// Themes a context menu (renderer, background, item colors). Called for the
    /// app menu, the tray menu and the selector dropdown menus.
    /// </summary>
    public static void ApplyToMenu(ContextMenuStrip menu, UiThemePalette palette)
    {
        menu.Renderer = new ThemedMenuRenderer(palette);
        menu.BackColor = palette.MenuBack;
        menu.ForeColor = palette.MenuText;
        ApplyToMenuItems(menu.Items, palette);
    }

    private static void ApplyToMenuItems(ToolStripItemCollection items, UiThemePalette palette)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = item.Enabled ? palette.MenuText : palette.TextMutedDisabled;
            if (item is ToolStripMenuItem { HasDropDownItems: true } parent)
            {
                ApplyToMenuItems(parent.DropDownItems, palette);
            }
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int sizeOfValue);


    private sealed class ThemedMenuColorTable : ProfessionalColorTable
    {
        private readonly UiThemePalette palette;

        public ThemedMenuColorTable(UiThemePalette palette)
        {
            this.palette = palette;
        }

        public override Color ToolStripDropDownBackground => palette.MenuBack;

        public override Color ImageMarginGradientBegin => palette.MenuBack;

        public override Color ImageMarginGradientMiddle => palette.MenuBack;

        public override Color ImageMarginGradientEnd => palette.MenuBack;

        public override Color MenuBorder => palette.MenuBorder;

        public override Color MenuItemBorder => Color.Transparent;

        public override Color MenuItemSelected => palette.MenuHover;

        public override Color MenuItemSelectedGradientBegin => palette.MenuHover;

        public override Color MenuItemSelectedGradientEnd => palette.MenuHover;

        public override Color MenuItemPressedGradientBegin => palette.MenuHover;

        public override Color MenuItemPressedGradientEnd => palette.MenuHover;

        public override Color SeparatorDark => palette.MenuSeparator;

        public override Color SeparatorLight => palette.MenuSeparator;

        public override Color CheckBackground => palette.MenuHover;

        public override Color CheckSelectedBackground => palette.MenuHover;

        public override Color CheckPressedBackground => palette.MenuHover;
    }

    private sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly UiThemePalette palette;

        public ThemedMenuRenderer(UiThemePalette palette)
            : base(new ThemedMenuColorTable(palette))
        {
            this.palette = palette;
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Enabled && (e.Item.Selected || e.Item.Pressed))
            {
                using var brush = new SolidBrush(palette.MenuHover);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = palette.MenuText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Custom check glyph so the check mark stays visible on every theme.
            var rect = e.ImageRectangle;
            using (var fill = new SolidBrush(palette.MenuHover))
            {
                e.Graphics.FillRectangle(fill, rect);
            }

            using var pen = new Pen(palette.MenuText, 1.6F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            var inset = Math.Max(2, rect.Height / 6);
            var points = new[]
            {
                new Point(rect.Left + inset + 1, rect.Top + (rect.Height * 2 / 3)),
                new Point(rect.Left + (rect.Width / 2), rect.Bottom - inset - 1),
                new Point(rect.Right - inset - 1, rect.Top + inset + 1),
            };
            e.Graphics.DrawLines(pen, points);
        }
    }
}

