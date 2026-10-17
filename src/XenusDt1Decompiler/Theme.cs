using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace XenusDt1Decompiler
{
    // Light or dark colours following the Windows "app mode" setting. WinForms on .NET 8 has no
    // built-in dark mode, so the dark palette is applied to the controls by hand.
    internal sealed class Theme
    {
        public bool IsDark { get; private init; }
        public Color Back { get; private init; }
        public Color Surface { get; private init; }
        public Color Text { get; private init; }
        public Color Muted { get; private init; }
        public Color Border { get; private init; }
        public Color LogBack { get; private init; }
        public Color LogText { get; private init; }
        public Color Info { get; private init; }
        public Color Warning { get; private init; }
        public Color Error { get; private init; }
        public Color Success { get; private init; }

        // XENUS_DT1_THEME=dark|light overrides the system setting (handy for screenshots).
        public static Theme Current() =>
            Environment.GetEnvironmentVariable("XENUS_DT1_THEME")?.ToLowerInvariant() switch
            {
                "dark" => Dark,
                "light" => Light,
                _ => SystemUsesDarkMode() ? Dark : Light,
            };

        private static readonly Theme Light = new()
        {
            IsDark = false,
            Back = SystemColors.Control,
            Surface = SystemColors.Window,
            Text = SystemColors.ControlText,
            Muted = SystemColors.GrayText,
            Border = SystemColors.ControlDark,
            LogBack = Color.White,
            LogText = Color.FromArgb(32, 32, 32),
            Info = Color.FromArgb(0, 95, 184),
            Warning = Color.FromArgb(157, 93, 0),
            Error = Color.FromArgb(196, 43, 28),
            Success = Color.FromArgb(16, 124, 16),
        };

        private static readonly Theme Dark = new()
        {
            IsDark = true,
            Back = Color.FromArgb(32, 32, 32),
            Surface = Color.FromArgb(45, 45, 45),
            Text = Color.FromArgb(240, 240, 240),
            Muted = Color.FromArgb(160, 160, 160),
            Border = Color.FromArgb(80, 80, 80),
            LogBack = Color.FromArgb(24, 24, 24),
            LogText = Color.FromArgb(220, 220, 220),
            Info = Color.FromArgb(96, 205, 255),
            Warning = Color.FromArgb(252, 225, 0),
            Error = Color.FromArgb(255, 153, 164),
            Success = Color.FromArgb(108, 203, 95),
        };

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

        public void Apply(Form form)
        {
            form.BackColor = Back;
            form.ForeColor = Text;
            if (IsDark)
            {
                // Dark title bar (Windows 10 2004+ / 11).
                int on = 1;
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
            }
            ApplyTo(form);
        }

        private void ApplyTo(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                switch (c)
                {
                    case RichTextBox log:
                        log.BackColor = LogBack;
                        log.ForeColor = LogText;
                        if (IsDark)
                            SetWindowTheme(log.Handle, "DarkMode_Explorer", null);
                        break;
                    case TextBox box when IsDark:
                        box.BackColor = Surface;
                        box.ForeColor = Text;
                        box.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case ComboBox combo when IsDark:
                        combo.FlatStyle = FlatStyle.Flat;
                        combo.BackColor = Surface;
                        combo.ForeColor = Text;
                        break;
                    case Button button when IsDark:
                        button.FlatStyle = FlatStyle.Flat;
                        button.BackColor = Surface;
                        button.ForeColor = Text;
                        button.FlatAppearance.BorderColor = Border;
                        break;
                    case Label label:
                        label.ForeColor = label.Tag as string == "muted" ? Muted : Text;
                        break;
                }
                ApplyTo(c);
            }
        }

        private const int DwmwaUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
    }
}
