using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace XenusDt1Decompiler
{
    // Light or dark palette following the Windows "app mode" setting. WinForms on .NET 8 has no
    // built-in dark mode, so the custom controls (Controls.cs) read their colours from here.
    // All text pairs in every palette are >= 4.5:1.
    internal sealed class Theme
    {
        private static Theme? active;

        // The palette in use; Load() picks it once at startup.
        public static Theme Active => active ?? Light;

        public bool IsDark { get; private init; }
        public Color Back { get; private init; }
        public Color Surface { get; private init; }
        public Color SurfaceHover { get; private init; }
        public Color SurfacePressed { get; private init; }
        public Color Border { get; private init; }
        public Color Text { get; private init; }
        public Color Muted { get; private init; }
        public Color Disabled { get; private init; }
        public Color Accent { get; private init; }
        public Color AccentHover { get; private init; }
        public Color AccentPressed { get; private init; }
        public Color OnAccent { get; private init; }
        public Color LogBack { get; private init; }
        public Color LogText { get; private init; }
        public Color Info { get; private init; }
        public Color Warning { get; private init; }
        public Color Error { get; private init; }
        public Color Success { get; private init; }

        // Start button; the accent in coloured palettes, a light/dark neutral in Mono.
        public Color Primary { get; private init; }
        public Color PrimaryHover { get; private init; }
        public Color PrimaryPressed { get; private init; }
        public Color OnPrimary { get; private init; }

        // Format switch: background of the track, and whether the selection is a raised neutral
        // pill with a small glowing accent dot (Mono) instead of an accent-filled pill.
        public Color Inset { get; private init; }
        public Color RaisedPill { get; private init; }
        public bool RaisedSelection { get; private init; }

        public Font UiFont { get; } = Pick(9.75F, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
        public Font UiFontStrong { get; } = Pick(9.75F, FontStyle.Regular, "Segoe UI Variable Text Semibold", "Segoe UI Semibold");
        public Font MonoFont { get; } = Pick(9F, FontStyle.Regular, "Cascadia Mono", "Consolas");

        private static Theme Make(bool dark, string back, string surface, string hover, string pressed, string border,
            string text, string muted, string disabled, string accent, string accentHover, string accentPressed,
            string onAccent, string logBack, string logText) => new()
        {
            IsDark = dark,
            Back = Hex(back),
            Surface = Hex(surface),
            SurfaceHover = Hex(hover),
            SurfacePressed = Hex(pressed),
            Border = Hex(border),
            Text = Hex(text),
            Muted = Hex(muted),
            Disabled = Hex(disabled),
            Accent = Hex(accent),
            AccentHover = Hex(accentHover),
            AccentPressed = Hex(accentPressed),
            OnAccent = Hex(onAccent),
            LogBack = Hex(logBack),
            LogText = Hex(logText),
            // Log message colours depend only on light/dark (all >= 5:1 on the log background).
            Info = Hex(dark ? "#7CC7FF" : "#0B5CAD"),
            Warning = Hex(dark ? "#F2C94C" : "#8A5A00"),
            Error = Hex(dark ? "#FF9B9B" : "#B42318"),
            Success = Hex(dark ? "#7BD88F" : "#18794E"),
            Primary = Hex(accent),
            PrimaryHover = Hex(accentHover),
            PrimaryPressed = Hex(accentPressed),
            OnPrimary = Hex(onAccent),
            Inset = Hex(surface),
            RaisedPill = Hex(accent),
            RaisedSelection = false,
        };

        // Monochrome neutrals (#FFFFFF / #E0E0E0 / #616161 / #212121) with a raised selection pill;
        // the only colour is a green accent on the progress line and the focus ring.
        private static Theme Mono(bool dark) => dark
            ? new()
            {
                IsDark = true,
                Back = Hex("#212121"), Surface = Hex("#2A2A2A"), SurfaceHover = Hex("#313131"), SurfacePressed = Hex("#383838"),
                Border = Hex("#363636"), Text = Hex("#F2F2F2"), Muted = Hex("#9E9E9E"), Disabled = Hex("#767676"),
                Accent = Hex("#4ADE80"), AccentHover = Hex("#6EE7A0"), AccentPressed = Hex("#22C55E"), OnAccent = Hex("#0F1A12"),
                LogBack = Hex("#1A1A1A"), LogText = Hex("#E0E0E0"),
                Info = Hex("#7CC7FF"), Warning = Hex("#F2C94C"), Error = Hex("#FF9B9B"), Success = Hex("#7BD88F"),
                Primary = Hex("#E0E0E0"), PrimaryHover = Hex("#F2F2F2"), PrimaryPressed = Hex("#C8C8C8"), OnPrimary = Hex("#1A1A1A"),
                Inset = Hex("#181818"), RaisedPill = Hex("#353535"), RaisedSelection = true,
            }
            : new()
            {
                IsDark = false,
                Back = Hex("#F5F5F5"), Surface = Hex("#FFFFFF"), SurfaceHover = Hex("#F0F0F0"), SurfacePressed = Hex("#E6E6E6"),
                Border = Hex("#E0E0E0"), Text = Hex("#212121"), Muted = Hex("#616161"), Disabled = Hex("#9E9E9E"),
                Accent = Hex("#16A34A"), AccentHover = Hex("#15803D"), AccentPressed = Hex("#166534"), OnAccent = Hex("#FFFFFF"),
                LogBack = Hex("#FFFFFF"), LogText = Hex("#212121"),
                Info = Hex("#0B5CAD"), Warning = Hex("#8A5A00"), Error = Hex("#B42318"), Success = Hex("#18794E"),
                Primary = Hex("#212121"), PrimaryHover = Hex("#333333"), PrimaryPressed = Hex("#000000"), OnPrimary = Hex("#FFFFFF"),
                Inset = Hex("#E8E8E8"), RaisedPill = Hex("#FFFFFF"), RaisedSelection = true,
            };

        public sealed record PaletteInfo(string Key, string Name);

        // Palettes the user can pick in the window; the first is the default.
        public static readonly PaletteInfo[] PaletteList =
        {
            new("mono", "Mono"),
            new("whitegold", "White Gold"),
        };

        //                 back       surface    hover      pressed    border     text       muted      disabled   accent     acc-hover  acc-press  on-accent  log-back   log-text
        private static readonly Dictionary<string, (Theme Light, Theme Dark)> Palettes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["mono"] = (Mono(false), Mono(true)),
            // Warm stone with a gold accent, after the game's title (palette from ui-ux-pro-max
            // "Luxury, premium dark + gold").
            ["whitegold"] = (
                Make(false, "#F5F5F4", "#FFFFFF", "#F0EEEC", "#E7E5E4", "#D6D3D1", "#1C1917", "#57534E", "#9C968F", "#A16207", "#854D0E", "#713F12", "#FFFFFF", "#FFFFFF", "#1C1917"),
                Make(true,  "#1C1917", "#292524", "#34302C", "#3F3A35", "#44403C", "#F5F5F4", "#A8A29E", "#78716C", "#CA8A04", "#EAB308", "#A16207", "#1C1917", "#171412", "#E7E5E4")),
        };

        private static Theme Light => Palettes["mono"].Light;

        public string PaletteKey { get; private set; } = "mono";

        // Picks the palette (unknown names fall back to the default) in the Windows light/dark mode;
        // XENUS_DT1_THEME=dark|light overrides the mode (handy for screenshots).
        public static Theme Load(string? palette)
        {
            var key = palette is not null && Palettes.ContainsKey(palette) ? palette.ToLowerInvariant() : PaletteList[0].Key;
            var pair = Palettes[key];
            active = Environment.GetEnvironmentVariable("XENUS_DT1_THEME")?.ToLowerInvariant() switch
            {
                "dark" => pair.Dark,
                "light" => pair.Light,
                _ => SystemUsesDarkMode() ? pair.Dark : pair.Light,
            };
            active.PaletteKey = key;
            return active;
        }

        // Colours used to draw a palette's swatch in the theme picker (independent of the active palette).
        public static (Color Fill, Color Half) SwatchColors(string key) => key == "whitegold"
            ? (Hex("#CA8A04"), Hex("#CA8A04"))
            : (Hex("#E0E0E0"), Hex("#212121"));

        private static bool SystemUsesDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch
            {
                return false;
            }
        }

        private static Color Hex(string hex) => ColorTranslator.FromHtml(hex);

        private static Font Pick(float size, FontStyle style, params string[] families)
        {
            foreach (var name in families)
            {
                using var probe = new Font(name, size, style);
                if (probe.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return new Font(name, size, style);
            }
            return new Font(FontFamily.GenericSansSerif, size, style);
        }

        // Window chrome: dark title bar, and on Windows 11 a caption in the window's own colour.
        public void ApplyChrome(Form form)
        {
            int dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            int caption = ColorTranslator.ToWin32(Back);
            DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref caption, sizeof(int));
            int text = ColorTranslator.ToWin32(Text);
            DwmSetWindowAttribute(form.Handle, DwmwaTextColor, ref text, sizeof(int));
        }

        public void UseDarkScrollbars(Control control)
        {
            if (IsDark)
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
    }
}
