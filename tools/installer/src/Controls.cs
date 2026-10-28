using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LivingServerSetup
{
    /// <summary>Base for the themed controls: double-buffered, transparent over the page's art.</summary>
    internal abstract class ThemedControl : Control
    {
        protected bool Hover, Pressed;

        protected ThemedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected void FocusCue(Graphics g, Rectangle r)
        {
            if (!Focused || !ShowFocusCues) return;
            using (var pen = new Pen(Color.FromArgb(160, Theme.GoldBright), 1) { DashStyle = DashStyle.Dot })
            using (var path = Theme.Round(Rectangle.Inflate(r, -Theme.S(3), -Theme.S(3)), Theme.S(2)))
                g.DrawPath(pen, path);
        }
    }

    /// <summary>The launcher's chunky action button: gold (primary) or dark iron with a gold rim (secondary).</summary>
    internal sealed class GoldButton : ThemedControl, IButtonControl
    {
        public bool Primary { get; set; }
        public Color? Accent { get; set; }
        public DialogResult DialogResult { get; set; }

        public GoldButton(string text, bool primary = false)
        {
            Text = text;
            Primary = primary;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(Theme.S(primary ? 168 : 132), Theme.S(40));
        }

        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { PerformClick(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 2.5f);
            float rad = Theme.S(4);
            // drop shadow
            using (var path = Theme.Round(new RectangleF(r.X + 1, r.Y + 2, r.Width, r.Height), rad))
            using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillPath(b, path);

            Color top, bottom, rim, text;
            if (!Enabled)
            {
                top = Color.FromArgb(58, 54, 46); bottom = Color.FromArgb(40, 37, 32); rim = Color.FromArgb(80, 74, 62); text = Theme.Faint;
            }
            else if (Primary)
            {
                var a = Accent;
                top = a.HasValue ? Lighten(a.Value, 0.25f) : Color.FromArgb(246, 214, 136);
                bottom = a.HasValue ? Darken(a.Value, 0.35f) : Color.FromArgb(158, 112, 44);
                rim = a.HasValue ? Darken(a.Value, 0.6f) : Color.FromArgb(92, 62, 20);
                text = a.HasValue ? Color.White : Theme.Ink;
                if (Hover) { top = Lighten(top, 0.12f); bottom = Lighten(bottom, 0.12f); }
                if (Pressed) { var t = top; top = bottom; bottom = t; }
            }
            else
            {
                top = Hover ? Color.FromArgb(78, 70, 56) : Color.FromArgb(62, 56, 46);
                bottom = Hover ? Color.FromArgb(44, 40, 33) : Color.FromArgb(33, 30, 25);
                rim = Hover ? Theme.Gold : Theme.GoldDeep;
                text = Hover ? Theme.GoldBright : Theme.GoldLight;
                if (Pressed) { var t = top; top = bottom; bottom = t; }
            }
            using (var path = Theme.Round(r, rad))
            {
                using (var lb = Theme.VGrad(Rectangle.Round(r), top, bottom))
                    g.FillPath(lb, path);
                // a glassy upper half
                var gloss = new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height * 0.45f);
                using (var gp = Theme.Round(gloss, rad - 1))
                using (var gb = Theme.VGrad(Rectangle.Round(gloss), Color.FromArgb(Enabled ? 70 : 20, 255, 255, 240), Color.FromArgb(0, 255, 255, 240)))
                    g.FillPath(gb, gp);
                using (var pen = new Pen(rim, Math.Max(1f, Theme.S(1.4f))))
                    g.DrawPath(pen, path);
            }
            var font = Theme.Serif(Primary ? 10.5f : 9.5f, FontStyle.Bold);
            var tr = Rectangle.Round(r);
            if (Pressed) tr.Offset(0, 1);
            if (Primary && Enabled && !Accent.HasValue)
            {
                var hi = tr; hi.Offset(0, 1);
                TextRenderer.DrawText(g, Text, font, hi, Color.FromArgb(120, 255, 240, 200), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            TextRenderer.DrawText(g, Text, font, tr, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            FocusCue(g, Rectangle.Round(r));
        }

        static Color Lighten(Color c, float f) => Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));
        static Color Darken(Color c, float f) => Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
    }

    /// <summary>A selectable card: a realm-coloured stripe, a title, a description and status lines.</summary>
    internal sealed class ChoiceCard : ThemedControl
    {
        public string Title { get; set; }
        public string Sub { get; set; }      // muted, on the title's line (a folder path)
        public string Body { get; set; }
        public List<(string text, Color color)> Lines { get; } = new List<(string, Color)>();
        public Color Stripe { get; set; } = Theme.Gold;
        public string Badge { get; set; }
        public Color BadgeColor { get; set; } = Theme.Gold;
        public object Tag2 { get; set; }
        bool selected;
        public bool Selected { get => selected; set { if (selected != value) { selected = value; Invalidate(); } } }
        public event EventHandler Chosen;

        public ChoiceCard() { Cursor = Cursors.Hand; TabStop = true; }
        public void Choose() => Chosen?.Invoke(this, EventArgs.Empty);

        protected override void OnClick(EventArgs e) { Focus(); Chosen?.Invoke(this, EventArgs.Empty); base.OnClick(e); }
        protected override void OnDoubleClick(EventArgs e) { base.OnDoubleClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Chosen?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        public int PreferredHeight(int width)
        {
            int textW = width - Theme.S(76);
            int h = Theme.S(10) + Theme.Measure(Title ?? "", Theme.Serif(11f, FontStyle.Bold), textW).Height;
            if (!string.IsNullOrEmpty(Body)) h += Theme.S(3) + Theme.Measure(Body, Theme.Serif(9f), textW).Height;
            foreach (var l in Lines) h += Theme.S(2) + Theme.Measure(l.text, Theme.Serif(8.75f, FontStyle.Italic), textW).Height;
            return Math.Max(Theme.S(56), h + Theme.S(11));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            int a = Selected ? 238 : Hover ? 228 : 205;
            Theme.DrawPanel(g, r, Selected ? Color.FromArgb(44, 38, 28) : Theme.Panel, a, studs: Selected);
            // realm stripe
            var stripe = new Rectangle(r.X + Theme.S(4), r.Y + Theme.S(4), Theme.S(5), r.Height - Theme.S(8));
            using (var b = Theme.VGrad(stripe, Stripe, Color.FromArgb(Stripe.R / 2, Stripe.G / 2, Stripe.B / 2)))
                g.FillRectangle(b, stripe);
            if (Selected || Hover)
            {
                using (var path = Theme.Round(r, Theme.S(4)))
                using (var pen = new Pen(Selected ? Theme.GoldLight : Color.FromArgb(150, Theme.Gold), Selected ? Theme.S(2f) : 1f))
                    g.DrawPath(pen, path);
            }
            // radio
            var rc = new Rectangle(r.X + Theme.S(24), r.Y + Theme.S(12), Theme.S(18), Theme.S(18));
            using (var b = new SolidBrush(Color.FromArgb(20, 17, 13))) g.FillEllipse(b, rc);
            using (var pen = new Pen(Selected ? Theme.GoldLight : Theme.GoldDeep, Theme.S(1.5f))) g.DrawEllipse(pen, rc);
            if (Selected)
            {
                var dot = Rectangle.Inflate(rc, -Theme.S(4.5f), -Theme.S(4.5f));
                using (var b = Theme.VGrad(dot, Theme.GoldBright, Theme.Gold)) g.FillEllipse(b, dot);
            }
            int x = r.X + Theme.S(56), y = r.Y + Theme.S(9), w = r.Right - x - Theme.S(14);
            var titleFont = Theme.Serif(11f, FontStyle.Bold);
            int badgeW = 0;
            if (!string.IsNullOrEmpty(Badge))
            {
                var bf = Theme.Serif(7.5f, FontStyle.Bold);
                var bs = TextRenderer.MeasureText(Badge, bf);
                badgeW = bs.Width + Theme.S(12);
                var br = new Rectangle(r.Right - Theme.S(12) - badgeW, y + Theme.S(1), badgeW, bs.Height + Theme.S(4));
                using (var path = Theme.Round(br, Theme.S(3)))
                {
                    using (var b = new SolidBrush(Color.FromArgb(60, BadgeColor))) g.FillPath(b, path);
                    using (var pen = new Pen(BadgeColor)) g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, Badge, bf, br, BadgeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            var th = Theme.Measure(Title ?? "", titleFont, w - badgeW).Height;
            var tw = TextRenderer.MeasureText(Title ?? "", titleFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            Theme.DrawText(g, Title, titleFont, new Rectangle(x, y, w - badgeW, th), Selected ? Theme.GoldBright : Theme.GoldLight);
            if (!string.IsNullOrEmpty(Sub))
            {
                var sf = Theme.Serif(9f, FontStyle.Italic);
                var sx = x + tw + Theme.S(12);
                Theme.DrawText(g, Sub, sf, new Rectangle(sx, y + Theme.S(3), Math.Max(10, x + w - badgeW - Theme.S(10) - sx), th), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis);
            }
            y += th;
            if (!string.IsNullOrEmpty(Body))
            {
                y += Theme.S(3);
                var bh = Theme.Measure(Body, Theme.Serif(9f), w).Height;
                Theme.DrawText(g, Body, Theme.Serif(9f), new Rectangle(x, y, w, bh), Theme.Text);
                y += bh;
            }
            foreach (var l in Lines)
            {
                y += Theme.S(2);
                var lf = Theme.Serif(8.75f, FontStyle.Italic);
                var lh = Theme.Measure(l.text, lf, w).Height;
                Theme.DrawText(g, l.text, lf, new Rectangle(x, y, w, lh), l.color);
                y += lh;
            }
            FocusCue(g, r);
        }
    }

    /// <summary>A gold checkbox (or radio) with a bold label and a muted explanation under it.</summary>
    internal sealed class OptionToggle : ThemedControl
    {
        public string Label { get; set; }
        public string Hint { get; set; }
        public bool Radio { get; set; }
        public string Group { get; set; }
        bool isChecked;
        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                if (value && Radio && Parent != null)
                    foreach (Control c in Parent.Controls)
                        if (c != this && c is OptionToggle o && o.Radio && o.Group == Group) o.Checked = false;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler CheckedChanged;

        public OptionToggle(string label, string hint = null, bool radio = false, string group = null)
        {
            Label = label; Hint = hint; Radio = radio; Group = group;
            Cursor = Cursors.Hand; TabStop = true;
        }

        public int PreferredHeight(int width)
        {
            int w = width - Theme.S(32);
            int h = Theme.Measure(Label ?? "", Theme.Serif(10f, FontStyle.Bold), w).Height;
            if (!string.IsNullOrEmpty(Hint)) h += Theme.S(2) + Theme.Measure(Hint, Theme.Serif(8.75f), w).Height;
            return Math.Max(Theme.S(24), h + Theme.S(4));
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            if (Radio) Checked = true; else Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(Theme.S(2), Theme.S(2), Theme.S(18), Theme.S(18));
            var on = Enabled ? Theme.GoldLight : Theme.Faint;
            using (var b = new SolidBrush(Color.FromArgb(Enabled ? 255 : 120, 20, 17, 13)))
            using (var pen = new Pen(Hover && Enabled ? Theme.GoldBright : Enabled ? Theme.Gold : Theme.Iron, Theme.S(1.5f)))
            {
                if (Radio) { g.FillEllipse(b, box); g.DrawEllipse(pen, box); }
                else
                {
                    using (var path = Theme.Round(box, Theme.S(3))) { g.FillPath(b, path); g.DrawPath(pen, path); }
                }
            }
            if (Checked)
            {
                if (Radio)
                {
                    var dot = Rectangle.Inflate(box, -Theme.S(4.5f), -Theme.S(4.5f));
                    using (var b = Theme.VGrad(dot, Theme.GoldBright, Theme.Gold)) g.FillEllipse(b, dot);
                }
                else
                {
                    using (var pen = new Pen(on, Theme.S(2.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, new[] {
                            new PointF(box.X + box.Width * 0.22f, box.Y + box.Height * 0.52f),
                            new PointF(box.X + box.Width * 0.42f, box.Y + box.Height * 0.72f),
                            new PointF(box.X + box.Width * 0.80f, box.Y + box.Height * 0.28f) });
                }
            }
            int x = Theme.S(30), w = Width - x - Theme.S(2);
            var lf = Theme.Serif(10f, FontStyle.Bold);
            var lh = Theme.Measure(Label ?? "", lf, w).Height;
            Theme.DrawText(g, Label, lf, new Rectangle(x, Theme.S(2), w, lh), Enabled ? (Checked ? Theme.GoldBright : Theme.GoldLight) : Theme.Faint);
            if (!string.IsNullOrEmpty(Hint))
            {
                var hf = Theme.Serif(8.75f);
                var hh = Theme.Measure(Hint, hf, w).Height;
                Theme.DrawText(g, Hint, hf, new Rectangle(x, Theme.S(4) + lh, w, hh), Enabled ? Theme.Muted : Theme.Faint);
            }
            FocusCue(g, new Rectangle(0, 0, Width, Height));
        }
    }

    /// <summary>A recessed track with a molten-gold fill. Indeterminate shows a moving sheen.</summary>
    internal sealed class GoldProgress : ThemedControl
    {
        double value;
        public double Value { get => value; set { var v = Math.Max(0, Math.Min(1, value)); if (Math.Abs(v - this.value) > 0.0005) { this.value = v; Invalidate(); } } }
        bool indeterminate;
        public bool Indeterminate { get => indeterminate; set { indeterminate = value; timer.Enabled = value || Sheen; Invalidate(); } }
        public bool Sheen { get; set; } = true;
        public Color Fill { get; set; } = Theme.Gold;
        readonly Timer timer = new Timer { Interval = 33 };
        float phase;

        public GoldProgress()
        {
            timer.Tick += (s, e) => { phase = (phase + 0.012f) % 1f; Invalidate(); };
            timer.Enabled = true;
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Math.Min(Theme.S(4), r.Height / 2);
            using (var track = Theme.Round(r, rad))
            {
                using (var b = Theme.VGrad(Rectangle.Round(r), Color.FromArgb(10, 8, 6), Color.FromArgb(34, 30, 24)))
                    g.FillPath(b, track);
                var clip = g.Clip;
                g.SetClip(track, CombineMode.Intersect);
                if (Indeterminate)
                {
                    float w = r.Width * 0.28f;
                    float x = r.X - w + (r.Width + w) * phase;
                    var band = new RectangleF(x, r.Y, w, r.Height);
                    using (var lb = new LinearGradientBrush(new RectangleF(x - 1, r.Y, w + 2, r.Height), Color.Transparent, Color.Transparent, 0f))
                    {
                        lb.InterpolationColors = new ColorBlend
                        {
                            Colors = new[] { Color.FromArgb(0, Fill), Color.FromArgb(200, Fill), Color.FromArgb(0, Fill) },
                            Positions = new[] { 0f, 0.5f, 1f }
                        };
                        g.FillRectangle(lb, band);
                    }
                }
                else if (Value > 0)
                {
                    var fr = new RectangleF(r.X, r.Y, Math.Max(rad * 2, r.Width * (float)Value), r.Height);
                    using (var fp = Theme.Round(fr, rad))
                    {
                        using (var lb = Theme.VGrad(Rectangle.Round(fr), Color.Transparent, Color.Transparent))
                        {
                            lb.InterpolationColors = new ColorBlend
                            {
                                Colors = new[] { Color.FromArgb(255, 240, 190), Fill, Color.FromArgb(Fill.R * 3 / 5, Fill.G * 3 / 5, Fill.B * 3 / 5), Color.FromArgb(Fill.R * 4 / 5, Fill.G * 4 / 5, Fill.B * 4 / 5) },
                                Positions = new[] { 0f, 0.35f, 0.75f, 1f }
                            };
                            g.FillPath(lb, fp);
                        }
                        if (Sheen && Value < 1)
                        {
                            float w = Math.Min(fr.Width, Theme.S(90));
                            float x = fr.X - w + (fr.Width + w) * phase;
                            using (var lb = new LinearGradientBrush(new RectangleF(x - 1, fr.Y, w + 2, fr.Height), Color.Transparent, Color.Transparent, 0f))
                            {
                                lb.InterpolationColors = new ColorBlend
                                {
                                    Colors = new[] { Color.FromArgb(0, 255, 250, 230), Color.FromArgb(110, 255, 250, 230), Color.FromArgb(0, 255, 250, 230) },
                                    Positions = new[] { 0f, 0.5f, 1f }
                                };
                                var old = g.Clip; g.SetClip(fp, CombineMode.Intersect);
                                g.FillRectangle(lb, x, fr.Y, w, fr.Height);
                                g.Clip = old;
                            }
                        }
                    }
                }
                g.Clip = clip;
                using (var pen = new Pen(Color.FromArgb(170, Theme.GoldDeep), 1)) g.DrawPath(pen, track);
            }
            // tick marks every 10 %
            using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0)))
                for (int i = 1; i < 10; i++) { float x = r.X + r.Width * i / 10f; g.DrawLine(pen, x, r.Y + 2, x, r.Bottom - 2); }
        }
    }

    internal enum StepState { Pending, Active, Done, Failed, Skipped }

    internal sealed class StepItem
    {
        public string Title;
        public string Detail;
        public StepState State;
    }

    /// <summary>The install's steps as a vertical list: pending rings, a spinning active ring, gold checks.</summary>
    internal sealed class StepList : ThemedControl
    {
        public List<StepItem> Items { get; } = new List<StepItem>();
        readonly Timer timer = new Timer { Interval = 40 };
        float angle;

        public StepList()
        {
            timer.Tick += (s, e) => { angle = (angle + 9) % 360; if (Items.Exists(i => i.State == StepState.Active)) Invalidate(); };
            timer.Start();
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        /// <summary>Row heights: 44 px each when they fit; otherwise the active (or failed) step keeps 44 px with its
        /// detail line and the others share what's left, titles only.</summary>
        int[] Rows(out bool roomy)
        {
            int n = Items.Count, full = Theme.S(44);
            var h = new int[n];
            roomy = n * full <= Height;
            if (roomy) { for (int i = 0; i < n; i++) h[i] = full; return h; }
            int big = Items.Count(x => x.State == StepState.Active || x.State == StepState.Failed);
            int small = Math.Max(Theme.S(28), (Height - big * full) / Math.Max(1, n - big));
            for (int i = 0; i < n; i++) h[i] = Items[i].State == StepState.Active || Items[i].State == StepState.Failed ? full : small;
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y = 0;
            var rows = Rows(out bool roomy);
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                var c = new Rectangle(Theme.S(4), y + Theme.S(3), Theme.S(22), Theme.S(22));
                if (i < Items.Count - 1)
                {
                    using (var pen = new Pen(it.State == StepState.Done ? Color.FromArgb(150, Theme.Gold) : Color.FromArgb(70, Theme.Iron), Theme.S(1.5f)))
                        g.DrawLine(pen, c.X + c.Width / 2f, c.Bottom + Theme.S(3), c.X + c.Width / 2f, y + rows[i] + Theme.S(1));
                }
                switch (it.State)
                {
                    case StepState.Done:
                        using (var b = Theme.VGrad(c, Theme.GoldBright, Theme.GoldDeep)) g.FillEllipse(b, c);
                        using (var pen = new Pen(Theme.Ink, Theme.S(2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            g.DrawLines(pen, new[] { new PointF(c.X + c.Width * 0.27f, c.Y + c.Height * 0.53f), new PointF(c.X + c.Width * 0.44f, c.Y + c.Height * 0.70f), new PointF(c.X + c.Width * 0.75f, c.Y + c.Height * 0.32f) });
                        break;
                    case StepState.Active:
                        using (var b = new SolidBrush(Color.FromArgb(30, 26, 20))) g.FillEllipse(b, c);
                        using (var pen = new Pen(Color.FromArgb(70, Theme.Gold), Theme.S(2.2f))) g.DrawEllipse(pen, c);
                        using (var pen = new Pen(Theme.GoldBright, Theme.S(2.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawArc(pen, c, angle, 100);
                        Theme.Diamond(g, new Point(c.X + c.Width / 2, c.Y + c.Height / 2), Theme.S(3.5f), Theme.GoldLight);
                        break;
                    case StepState.Failed:
                        using (var b = new SolidBrush(Color.FromArgb(120, 40, 34))) g.FillEllipse(b, c);
                        using (var pen = new Pen(Theme.Danger, Theme.S(1.5f))) g.DrawEllipse(pen, c);
                        using (var pen = new Pen(Color.White, Theme.S(2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            float k = c.Width * 0.3f;
                            g.DrawLine(pen, c.X + k, c.Y + k, c.Right - k, c.Bottom - k);
                            g.DrawLine(pen, c.Right - k, c.Y + k, c.X + k, c.Bottom - k);
                        }
                        break;
                    case StepState.Skipped:
                        using (var pen = new Pen(Color.FromArgb(90, Theme.Iron), Theme.S(1.5f)) { DashStyle = DashStyle.Dot }) g.DrawEllipse(pen, c);
                        break;
                    default:
                        using (var b = new SolidBrush(Color.FromArgb(24, 21, 17))) g.FillEllipse(b, c);
                        using (var pen = new Pen(Color.FromArgb(120, Theme.Iron), Theme.S(1.5f))) g.DrawEllipse(pen, c);
                        break;
                }
                int x = c.Right + Theme.S(12), w = Width - x;
                var tf = Theme.Serif(10f, it.State == StepState.Active ? FontStyle.Bold : FontStyle.Regular);
                var col = it.State == StepState.Active ? Theme.GoldBright : it.State == StepState.Done ? Theme.GoldLight
                    : it.State == StepState.Failed ? Theme.Danger : it.State == StepState.Skipped ? Theme.Faint : Theme.Muted;
                Theme.DrawText(g, it.Title, tf, new Rectangle(x, y + Theme.S(4), w, Theme.S(20)), col, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                if (!string.IsNullOrEmpty(it.Detail) && (roomy || it.State == StepState.Active || it.State == StepState.Failed))
                    Theme.DrawText(g, it.Detail, Theme.Serif(8.5f, FontStyle.Italic), new Rectangle(x, y + Theme.S(23), w, Theme.S(18)),
                        it.State == StepState.Failed ? Theme.Danger : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                y += rows[i];
            }
        }
    }

    /// <summary>A gold text link.</summary>
    internal sealed class LinkText : ThemedControl
    {
        public float FontSize = 9f;
        public LinkText(string text) { Text = text; Cursor = Cursors.Hand; TabStop = true; }

        public void FitWidth() => Width = TextRenderer.MeasureText(Text, Theme.Serif(FontSize, FontStyle.Underline)).Width + Theme.S(4);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var f = Theme.Serif(FontSize, Hover ? FontStyle.Underline : FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, Text, f, ClientRectangle, Hover ? Theme.GoldBright : Theme.Gold,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            FocusCue(e.Graphics, ClientRectangle);
        }
    }

    /// <summary>A text box framed like the launcher's fields.</summary>
    internal sealed class PathBox : Panel
    {
        public readonly TextBox Box = new TextBox();

        public PathBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(18, 16, 13);
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = BackColor;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.Serif(10f);
            Controls.Add(Box);
            Height = Theme.S(34);
        }

        public override string Text { get => Box.Text; set => Box.Text = value; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            Box.SetBounds(Theme.S(10), (Height - Box.PreferredHeight) / 2, Width - Theme.S(20), Box.PreferredHeight);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.S(3)))
            using (var pen = new Pen(Box.Focused ? Theme.Gold : Theme.GoldDeep, Theme.S(1.2f)))
                g.DrawPath(pen, path);
        }
    }

    /// <summary>Minimize and close for the borderless window, drawn as small iron pills over the art.</summary>
    internal sealed class WindowButton : ThemedControl
    {
        readonly bool close;
        public WindowButton(bool close) { this.close = close; Size = new Size(Theme.S(34), Theme.S(28)); Cursor = Cursors.Hand; TabStop = false; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Round(r, Theme.S(4)))
            {
                var fill = Hover ? (close ? Color.FromArgb(220, 150, 50, 42) : Color.FromArgb(210, 70, 63, 50)) : Color.FromArgb(150, 12, 10, 8);
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var pen = new Pen(Color.FromArgb(Hover ? 220 : 120, Theme.Gold))) g.DrawPath(pen, path);
            }
            using (var pen = new Pen(Hover ? Color.White : Theme.GoldLight, Theme.S(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float cx = Width / 2f, cy = Height / 2f, k = Theme.S(5);
                if (close) { g.DrawLine(pen, cx - k, cy - k, cx + k, cy + k); g.DrawLine(pen, cx + k, cy - k, cx - k, cy + k); }
                else g.DrawLine(pen, cx - k, cy + Theme.S(1), cx + k, cy + Theme.S(1));
            }
        }
    }
}
