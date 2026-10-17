using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace XenusDt1Decompiler
{
    // Light or dark palette following the Windows "app mode" setting. WinForms on .NET 8 has no
    // built-in dark mode, so the custom controls (Controls.cs) read their colours from here.
    // The palette takes its accent from the game's lagoon water; all text pairs are >= 4.5:1.
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

        public Font UiFont { get; } = Pick(9.75F, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
        public Font UiFontStrong { get; } = Pick(9.75F, FontStyle.Regular, "Segoe UI Variable Text Semibold", "Segoe UI Semibold");
        public Font MonoFont { get; } = Pick(9F, FontStyle.Regular, "Cascadia Mono", "Consolas");

        private static readonly Theme Light = new()
        {
            IsDark = false,
            Back = Hex("#EEF2EF"),
            Surface = Hex("#FFFFFF"),
            SurfaceHover = Hex("#E3EAE6"),
            SurfacePressed = Hex("#D5DFD9"),
            Border = Hex("#C9D3CD"),
            Text = Hex("#1B2420"),
            Muted = Hex("#56645C"),
            Disabled = Hex("#8A968F"),
            Accent = Hex("#0B7368"),
            AccentHover = Hex("#0A675D"),
            AccentPressed = Hex("#085A52"),
            OnAccent = Hex("#FFFFFF"),
            LogBack = Hex("#FFFFFF"),
            LogText = Hex("#1B2420"),
            Info = Hex("#0B5CAD"),
            Warning = Hex("#8A5A00"),
            Error = Hex("#B42318"),
            Success = Hex("#18794E"),
        };

        private static readonly Theme Dark = new()
        {
            IsDark = true,
            Back = Hex("#1C2420"),
            Surface = Hex("#26302B"),
            SurfaceHover = Hex("#2F3A34"),
            SurfacePressed = Hex("#36433C"),
            Border = Hex("#3A4740"),
            Text = Hex("#E6ECE8"),
            Muted = Hex("#98A69E"),
            Disabled = Hex("#6F7D75"),
            Accent = Hex("#45C2B1"),
            AccentHover = Hex("#5ACFBF"),
            AccentPressed = Hex("#38A898"),
            OnAccent = Hex("#0D1A16"),
            LogBack = Hex("#151B18"),
            LogText = Hex("#D5DDD8"),
            Info = Hex("#7CC7FF"),
            Warning = Hex("#F2C94C"),
            Error = Hex("#FF9B9B"),
            Success = Hex("#7BD88F"),
        };

        // XENUS_DT1_THEME=dark|light overrides the system setting (handy for screenshots).
        public static Theme Load()
        {
            active = Environment.GetEnvironmentVariable("XENUS_DT1_THEME")?.ToLowerInvariant() switch
            {
                "dark" => Dark,
                "light" => Light,
                _ => SystemUsesDarkMode() ? Dark : Light,
            };
            return active;
        }

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
