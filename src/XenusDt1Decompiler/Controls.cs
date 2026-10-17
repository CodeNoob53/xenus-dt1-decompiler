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
                fill = !Enabled ? Blend(t.Primary, t.Back, 0.45f) : pressed ? t.PrimaryPressed : hover ? t.PrimaryHover : t.Primary;
                text = Enabled ? t.OnPrimary : Blend(t.OnPrimary, t.Back, 0.35f);
                border = fill;
            }
            else
            {
                fill = !Enabled ? t.Back : pressed ? t.SurfacePressed : hover ? t.SurfaceHover : t.Surface;
                text = Enabled ? t.Text : t.Disabled;
                border = t.Border;
            }

            // Fully rounded (pill) shape.
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Draw.Rounded(rect, rect.Height / 2))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            if (Focused && ShowFocusCues)
            {
                var ring = new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f);
                using var path = Draw.Rounded(ring, ring.Height / 2);
                using var pen = new Pen(Primary ? t.OnPrimary : t.Accent, 1.5f);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
    }

    // Round colour swatch for the theme picker: a radio button drawn as a circle in the palette's
    // colours, with a ring around the selected one.
    internal sealed class PaletteSwatch : RadioButton
    {
        private bool hover;

        public string PaletteKey { get; }

        public PaletteSwatch(string key, string name)
        {
            PaletteKey = key;
            Text = name;
            AccessibleName = $"{name} theme";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(26, 26);
            Margin = new Padding(2, 0, 2, 0);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Active;
            var g = e.Graphics;
            Draw.Prepare(g, this);
            var (fill, half) = Theme.SwatchColors(PaletteKey);

            var dot = new RectangleF(Width / 2f - 8, Height / 2f - 8, 16, 16);
            using (var brush = new SolidBrush(fill))
                g.FillEllipse(brush, dot);
            if (half != fill)
            {
                using var brush = new SolidBrush(half);
                g.FillPie(brush, dot.X, dot.Y, dot.Width, dot.Height, -90, 180); // right half
            }
            using (var outline = new Pen(t.Border))
                g.DrawEllipse(outline, dot);

            if (Checked || hover || (Focused && ShowFocusCues))
            {
                var ring = RectangleF.Inflate(dot, 3.5f, 3.5f);
                var color = Focused && ShowFocusCues ? t.Accent : Checked ? t.Text : t.Muted;
                using var pen = new Pen(color, 1.5f);
                g.DrawEllipse(pen, ring);
            }
        }
    }

    // Respects Windows Settings > Accessibility > Visual effects > Animation effects.
    internal static class Motion
    {
        public static bool Enabled
        {
            get
            {
                bool on = true;
                return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref on, 0) || on;
            }
        }

        private const uint SpiGetClientAreaAnimation = 0x1042;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);

        public static float EaseOutCubic(float t) => 1 - MathF.Pow(1 - Math.Clamp(t, 0, 1), 3);
    }

    // A row of mutually exclusive options (the output format). Each option is a real RadioButton
    // drawn as a segment, so screen readers see radio buttons with a checked state, arrow keys move
    // between options, Tab lands on the selected one, and every option has its own tooltip.
    // The selection pill slides to the chosen option; text under the pill switches colour as it passes.
    internal sealed class SegmentedControl : Panel
    {
        public sealed record Option(string Text, string Value, string Description);

        private const int SlideMs = 160;

        private readonly List<Segment> segments = new();
        private readonly ToolTip toolTip = new() { InitialDelay = 300, AutoPopDelay = 12000 };
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
        private RectangleF pill, pillFrom, pillTo;
        private long slideStart;

        public event EventHandler? SelectedIndexChanged;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = new Padding(3);
            Height = 34;
            AccessibleRole = AccessibleRole.Grouping;
            timer.Tick += (_, _) => Step();
        }

        // Selection pill in this control's coordinates (segments draw their part of it).
        internal RectangleF Pill => pill;

        public void SetOptions(IEnumerable<Option> items)
        {
            SuspendLayout();
            foreach (var s in segments) s.Dispose();
            segments.Clear();
            int x = Padding.Left;
            foreach (var option in items)
            {
                // Raised style: segments span the full height so the pill (inset equally on all sides)
                // can drop its shadow into the bottom padding.
                bool raised = Theme.Active.RaisedSelection;
                var segment = new Segment(this, option)
                {
                    Location = new Point(x, raised ? 0 : Padding.Top),
                    Height = raised ? Height : Height - Padding.Vertical,
                };
                segment.Width = TextRenderer.MeasureText(option.Text, Theme.Active.UiFontStrong).Width + 28;
                segment.CheckedChanged += (_, _) =>
                {
                    if (!segment.Checked) return;
                    SlideTo(segment.Bounds);
                    SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                };
                segment.GotFocus += (_, _) => Invalidate();
                segment.LostFocus += (_, _) => Invalidate();
                toolTip.SetToolTip(segment, option.Description);
                segments.Add(segment);
                Controls.Add(segment);
                x += segment.Width;
            }
            Width = x + Padding.Right;
            if (segments.Count > 0)
            {
                segments[0].Checked = true;
                pill = pillTo = segments[0].Bounds;
            }
            ResumeLayout();
        }

        public int SelectedIndex
        {
            get => Math.Max(0, segments.FindIndex(s => s.Checked));
            set
            {
                if (value < 0 || value >= segments.Count) return;
                segments[value].Checked = true;
                if (!IsHandleCreated) JumpTo(segments[value].Bounds); // initial value: no slide
            }
        }

        public Option SelectedOption => segments[SelectedIndex].Option;

        private void JumpTo(RectangleF target)
        {
            timer.Stop();
            pill = pillTo = target;
            RedrawSegments();
        }

        private void SlideTo(RectangleF target)
        {
            if (!IsHandleCreated || !Motion.Enabled)
            {
                JumpTo(target);
                return;
            }
            pillFrom = pill;
            pillTo = target;
            slideStart = Environment.TickCount64;
            timer.Start();
        }

        private void Step()
        {
            float t = Motion.EaseOutCubic((Environment.TickCount64 - slideStart) / (float)SlideMs);
            pill = new RectangleF(
                pillFrom.X + (pillTo.X - pillFrom.X) * t,
                pillFrom.Y,
                pillFrom.Width + (pillTo.Width - pillFrom.Width) * t,
                pillFrom.Height);
            if (t >= 1)
            {
                pill = pillTo;
                timer.Stop();
            }
            RedrawSegments();
        }

        private void RedrawSegments()
        {
            foreach (var s in segments) s.Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            RedrawSegments();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                toolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Active;
            var g = e.Graphics;
            Draw.Prepare(g, this);
            bool focused = segments.Any(s => s.Focused);
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var track = Draw.Rounded(rect, rect.Height / 2);
            using var brush = new SolidBrush(t.Inset);
            g.FillPath(brush, track);
            if (focused || !t.RaisedSelection)
            {
                using var pen = new Pen(focused ? t.Accent : t.Border, focused ? 1.5f : 1f);
                g.DrawPath(pen, track);
            }
        }

        private sealed class Segment : RadioButton
        {
            private readonly SegmentedControl owner;
            private bool hover;

            public Option Option { get; }

            public Segment(SegmentedControl owner, Option option)
            {
                this.owner = owner;
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
                // Repaint this segment's part of the rounded track, so the end segments keep its round ends.
                g.Clear(owner.Parent?.BackColor ?? t.Back);
                var trackRect = new RectangleF(0.5f - Left, 0.5f - Top, owner.Width - 1.5f, owner.Height - 1.5f);
                using (var trackPath = Draw.Rounded(trackRect, trackRect.Height / 2))
                using (var trackBrush = new SolidBrush(t.Inset))
                    g.FillPath(trackBrush, trackPath);

                // This segment's part of the (possibly moving) pill, in this segment's coordinates.
                var p = owner.Pill;
                var pill = t.RaisedSelection
                    ? new RectangleF(p.X - Left, 3, p.Width - 1, Height - 7) // same 3 px gap above and below
                    : new RectangleF(p.X - Left, p.Y - Top, p.Width - 1, p.Height - 1);
                float covered = Math.Max(0, Math.Min(p.Right, Right) - Math.Max(p.Left, Left)) / Width;
                bool onPill = covered > 0.5f;

                if (hover && Enabled && !Checked)
                {
                    var h = t.RaisedSelection ? new RectangleF(0, 3, Width - 1, Height - 7) : new RectangleF(0, 0, Width - 1, Height - 1);
                    using var hoverPath = Draw.Rounded(h, h.Height / 2);
                    using var hoverBrush = new SolidBrush(t.RaisedSelection ? Color.FromArgb(110, t.RaisedPill) : t.SurfaceHover);
                    g.FillPath(hoverBrush, hoverPath);
                }

                using var pillPath = Draw.Rounded(pill, pill.Height / 2);
                if (t.RaisedSelection)
                    PaintRaisedPill(g, t, pill, pillPath);
                else
                {
                    using var pillBrush = new SolidBrush(Enabled ? t.Accent : t.Border);
                    g.FillPath(pillBrush, pillPath);
                }

                // The label switches colour once the pill covers most of the segment, so it flips
                // halfway through the slide. (TextRenderer ignores complex clip regions, so the label
                // cannot be split along the pill edge.)
                var font = Checked ? t.UiFontStrong : t.UiFont;
                Color color = !Enabled ? (onPill ? t.Muted : t.Disabled)
                    : t.RaisedSelection ? (onPill ? t.Text : t.Muted)
                    : (onPill ? t.OnAccent : t.Text);
                // Raised style: centre the label on the pill, not on the full-height segment.
                var textRect = t.RaisedSelection ? new Rectangle(0, 3, Width, Height - 6) : ClientRectangle;
                TextRenderer.DrawText(g, Text, font, textRect, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                if (Checked && Focused && ShowFocusCues)
                {
                    var ring = RectangleF.Inflate(pill, -2, -2);
                    using var ringPath = Draw.Rounded(ring, ring.Height / 2);
                    using var pen = new Pen(t.RaisedSelection ? t.Accent : t.OnAccent, 1.5f);
                    g.DrawPath(pen, ringPath);
                }
            }

            private void PaintRaisedPill(Graphics g, Theme t, RectangleF pill, System.Drawing.Drawing2D.GraphicsPath pillPath)
            {
                // Soft shadow under the pill.
                int strong = t.IsDark ? 90 : 26, soft = t.IsDark ? 45 : 12;
                foreach (var (dy, alpha) in new[] { (2.5f, soft), (1f, strong) })
                {
                    var r = pill;
                    r.Offset(0, dy);
                    using var shadowPath = Draw.Rounded(r, r.Height / 2);
                    using var shadow = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                    g.FillPath(shadow, shadowPath);
                }

                using (var fill = new SolidBrush(Enabled ? t.RaisedPill : t.Border))
                    g.FillPath(fill, pillPath);

                // Light catching the top edge (dark), or a hairline outline (light).
                if (t.IsDark)
                {
                    using var edge = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new RectangleF(pill.X, pill.Y - 1, pill.Width, pill.Height + 2),
                        Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f);
                    using var pen = new Pen(edge, 1f);
                    g.DrawPath(pen, pillPath);
                }
                else
                {
                    using var pen = new Pen(t.Border, 1f);
                    g.DrawPath(pen, pillPath);
                }
            }
        }
    }

    // Thin themed progress line (the stock ProgressBar ignores colours under visual styles).
    // While Busy it shows a running segment when the total is not known yet (Indeterminate),
    // and a soft highlight moving across the filled part otherwise.
    internal sealed class ThinProgressBar : Control
    {
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
        private readonly long start = Environment.TickCount64;
        private float value;
        private int maximum = 100;
        private bool busy, indeterminate;

        public ThinProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 4;
            AccessibleRole = AccessibleRole.ProgressBar;
            timer.Tick += (_, _) => Invalidate();
        }

        public int Maximum { get => maximum; set { maximum = Math.Max(1, value); Invalidate(); } }

        // Fractional so the bar can move smoothly between reported values.
        public float Value { get => value; set { this.value = Math.Clamp(value, 0, maximum); Invalidate(); } }

        public bool Indeterminate { get => indeterminate; set { indeterminate = value; UpdateTimer(); Invalidate(); } }

        public bool Busy { get => busy; set { busy = value; UpdateTimer(); Invalidate(); } }

        private void UpdateTimer() => timer.Enabled = busy && Motion.Enabled;

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Active;
            var g = e.Graphics;
            Draw.Prepare(g, this);
            float radius = Height / 2f;
            float trackWidth = Width - 1;
            var trackRect = new RectangleF(0, 0, trackWidth, Height - 1);
            using (var track = Draw.Rounded(trackRect, radius))
            using (var brush = new SolidBrush(t.Border))
                g.FillPath(brush, track);

            float phase = (Environment.TickCount64 - start) / 1000f;
            if (busy && indeterminate)
            {
                if (!Motion.Enabled) return;
                // A segment a quarter of the track wide, sweeping across every 1.4 s.
                float seg = trackWidth * 0.25f;
                float x = (phase % 1.4f) / 1.4f * (trackWidth + seg) - seg;
                var r = RectangleF.Intersect(new RectangleF(x, 0, seg, Height - 1), trackRect);
                if (r.Width >= Height)
                {
                    using var bar = Draw.Rounded(r, radius);
                    using var brush = new SolidBrush(t.Accent);
                    g.FillPath(brush, bar);
                }
                return;
            }

            float w = trackWidth * value / maximum;
            if (w < Height) return;
            using var fillPath = Draw.Rounded(new RectangleF(0, 0, w, Height - 1), radius);
            using (var brush = new SolidBrush(t.Accent))
                g.FillPath(brush, fillPath);

            if (busy && Motion.Enabled)
            {
                // Soft highlight crossing the filled part every 1.6 s.
                const float band = 90;
                float x = (phase % 1.6f) / 1.6f * (w + band) - band;
                var glow = Color.FromArgb(t.IsDark ? 140 : 110, 255, 255, 255);
                using var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new RectangleF(x, 0, band, Height), Color.Transparent, Color.Transparent, 0f);
                gradient.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, 255, 255, 255), glow, Color.FromArgb(0, 255, 255, 255) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                g.SetClip(fillPath);
                g.FillRectangle(gradient, new RectangleF(x, 0, band, Height));
                g.ResetClip();
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
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var path = Draw.Rounded(rect, Height <= 44 ? rect.Height / 2 : 12); // pill for single-line fields
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(focused ? t.Accent : t.Border, focused ? 1.5f : 1f);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
    }
}
