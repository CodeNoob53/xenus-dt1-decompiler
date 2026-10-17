using System.Drawing;
using System.Drawing.Drawing2D;

namespace XenusDt1Decompiler
{
    internal static class Draw
    {
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Prepare(Graphics g, Control c)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(c.Parent?.BackColor ?? Theme.Active.Back);
        }
    }

    // Button drawn from the theme, so its text stays readable in every state (the stock flat
    // button draws disabled text in system grey, which is near-black on a dark background).
    internal sealed class ThemedButton : Button
    {
        private bool hover, pressed;

        public bool Primary { get; init; }

        public ThemedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            AutoSize = true; // layouts size the button from GetPreferredSize
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Height = 32;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(Math.Max(text.Width + 32, MinimumSize.Width), 32);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Active;
            var g = e.Graphics;
            Draw.Prepare(g, this);

            Color fill, text, border;
            if (Primary)
            {
                fill = !Enabled ? Blend(t.Accent, t.Back, 0.45f) : pressed ? t.AccentPressed : hover ? t.AccentHover : t.Accent;
                text = Enabled ? t.OnAccent : Blend(t.OnAccent, t.Back, 0.35f);
                border = fill;
            }
            else
            {
                fill = !Enabled ? t.Back : pressed ? t.SurfacePressed : hover ? t.SurfaceHover : t.Surface;
                text = Enabled ? t.Text : t.Disabled;
                border = t.Border;
            }

            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Draw.Rounded(rect, 6))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            if (Focused && ShowFocusCues)
            {
                using var path = Draw.Rounded(new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f), 4);
                using var pen = new Pen(Primary ? t.OnAccent : t.Accent, 1.5f);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
    }

    // A row of mutually exclusive options (the output format). Each option is a real RadioButton
    // drawn as a segment, so screen readers see radio buttons with a checked state, arrow keys move
    // between options, Tab lands on the selected one, and every option has its own tooltip.
    internal sealed class SegmentedControl : Panel
    {
        public sealed record Option(string Text, string Value, string Description);

        private readonly List<Segment> segments = new();
        private readonly ToolTip toolTip = new() { InitialDelay = 300, AutoPopDelay = 12000 };

        public event EventHandler? SelectedIndexChanged;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = new Padding(3);
            Height = 34;
            AccessibleRole = AccessibleRole.Grouping;
        }

        public void SetOptions(IEnumerable<Option> items)
        {
            SuspendLayout();
            foreach (var s in segments) s.Dispose();
            segments.Clear();
            int x = Padding.Left;
            foreach (var option in items)
            {
                var segment = new Segment(option) { Location = new Point(x, Padding.Top), Height = Height - Padding.Vertical };
                segment.Width = TextRenderer.MeasureText(option.Text, Theme.Active.UiFontStrong).Width + 28;
                segment.CheckedChanged += (_, _) =>
                {
                    if (segment.Checked) SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                    Invalidate();
                };
                segment.GotFocus += (_, _) => Invalidate();
                segment.LostFocus += (_, _) => Invalidate();
                toolTip.SetToolTip(segment, option.Description);
                segments.Add(segment);
                Controls.Add(segment);
                x += segment.Width;
            }
            Width = x + Padding.Right;
            if (segments.Count > 0) segments[0].Checked = true;
            ResumeLayout();
        }

        public int SelectedIndex
        {
            get => Math.Max(0, segments.FindIndex(s => s.Checked));
            set { if (value >= 0 && value < segments.Count) segments[value].Checked = true; }
        }

        public Option SelectedOption => segments[SelectedIndex].Option;

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            foreach (var s in segments) s.Invalidate();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Active;
            var g = e.Graphics;
            Draw.Prepare(g, this);
            bool focused = segments.Any(s => s.Focused);
            using var track = Draw.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8);
            using var brush = new SolidBrush(t.Surface);
            using var pen = new Pen(focused ? t.Accent : t.Border, focused ? 1.5f : 1f);
            g.FillPath(brush, track);
            g.DrawPath(pen, track);
        }

        private sealed class Segment : RadioButton
        {
            private bool hover;

            public Option Option { get; }

            public Segment(Option option)
            {
                Option = option;
                Text = option.Text;
                AccessibleDescription = option.Description;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
                Margin = Padding.Empty;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var t = Theme.Active;
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(t.Surface);
                var pill = new RectangleF(0, 0, Width - 1, Height - 1);

                Color text;
                if (Checked)
                {
                    using var path = Draw.Rounded(pill, 6);
                    using var brush = new SolidBrush(Enabled ? t.Accent : t.Border);
                    g.FillPath(brush, path);
                    text = Enabled ? t.OnAccent : t.Muted;
                    if (Focused && ShowFocusCues)
                    {
                        using var ring = Draw.Rounded(new RectangleF(2, 2, Width - 5, Height - 5), 4);
                        using var pen = new Pen(t.OnAccent, 1.5f);
                        g.DrawPath(pen, ring);
                    }
                }
                else
                {
                    if (hover && Enabled)
                    {
                        using var path = Draw.Rounded(pill, 6);
                        using var brush = new SolidBrush(t.SurfaceHover);
                        g.FillPath(brush, path);
                    }
                    text = Enabled ? t.Text : t.Disabled;
                }
                TextRenderer.DrawText(g, Text, Checked ? t.UiFontStrong : t.UiFont, ClientRectangle, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    // Thin themed progress line (the stock ProgressBar ignores colours under visual styles).
    internal sealed class ThinProgressBar : Control
    {
        private int value, maximum = 100;

        public ThinProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 4;
            AccessibleRole = AccessibleRole.ProgressBar;
        }

        public int Maximum { get => maximum; set { maximum = Math.Max(1, value); Invalidate(); } }
        public int Value { get => value; set { this.value = Math.Clamp(value, 0, maximum); Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Active;
            var g = e.Graphics;
            Draw.Prepare(g, this);
            float radius = Height / 2f;
            using (var track = Draw.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), radius))
            using (var brush = new SolidBrush(t.Border))
                g.FillPath(brush, track);
            float w = (Width - 1) * value / (float)maximum;
            if (w >= Height)
            {
                using var bar = Draw.Rounded(new RectangleF(0, 0, w, Height - 1), radius);
                using var brush = new SolidBrush(t.Accent);
                g.FillPath(brush, bar);
            }
        }
    }

    // Rounded field frame around a borderless child (TextBox, RichTextBox); the border turns
    // accent while the child has focus.
    internal sealed class FieldFrame : Panel
    {
        private readonly Control child;
        private readonly Color fill;

        public FieldFrame(Control child, Padding padding, Color fill)
        {
            this.child = child;
            this.fill = fill;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = padding;
            child.Dock = DockStyle.Fill;
            child.BackColor = fill;
            Controls.Add(child);
            child.GotFocus += (_, _) => Invalidate();
            child.LostFocus += (_, _) => Invalidate();
            Click += (_, _) => child.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Active;
            var g = e.Graphics;
            Draw.Prepare(g, this);
            bool focused = child.Focused;
            using var path = Draw.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(focused ? t.Accent : t.Border, focused ? 1.5f : 1f);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
    }
}
