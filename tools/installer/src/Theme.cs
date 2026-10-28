using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace LivingServerSetup
{
    /// <summary>The launcher's palette (OfflineDaoc.Launcher/ThemeControls.cs DaocTheme), fonts, art and drawing helpers.</summary>
    internal static class Theme
    {
        public static readonly Color Void = Color.FromArgb(14, 12, 10);
        public static readonly Color Stone = Color.FromArgb(47, 43, 36);
        public static readonly Color StoneDark = Color.FromArgb(25, 24, 21);
        public static readonly Color StoneLight = Color.FromArgb(79, 71, 56);
        public static readonly Color Iron = Color.FromArgb(102, 100, 90);
        public static readonly Color Gold = Color.FromArgb(194, 157, 77);
        public static readonly Color GoldLight = Color.FromArgb(232, 205, 135);
        public static readonly Color GoldBright = Color.FromArgb(255, 232, 168);
        public static readonly Color GoldDeep = Color.FromArgb(128, 92, 38);
        public static readonly Color Parchment = Color.FromArgb(215, 199, 157);
        public static readonly Color Ink = Color.FromArgb(43, 32, 22);
        public static readonly Color Panel = Color.FromArgb(31, 28, 23);
        public static readonly Color PanelAlt = Color.FromArgb(42, 37, 30);
        public static readonly Color Text = Color.FromArgb(226, 217, 192);
        public static readonly Color Muted = Color.FromArgb(166, 154, 128);
        public static readonly Color Faint = Color.FromArgb(118, 108, 88);
        public static readonly Color Success = Color.FromArgb(139, 191, 104);
        public static readonly Color Warning = Color.FromArgb(229, 178, 82);
        public static readonly Color Danger = Color.FromArgb(232, 95, 83);
        public static readonly Color Albion = Color.FromArgb(171, 64, 58);
        public static readonly Color Midgard = Color.FromArgb(62, 94, 144);
        public static readonly Color Hibernia = Color.FromArgb(57, 117, 66);

        /// <summary>Logical (96 dpi) to device pixels.</summary>
        public static float Scale = 1f;
        public static int S(float v) => (int)Math.Round(v * Scale);
        public static Rectangle S(float x, float y, float w, float h) => new Rectangle(S(x), S(y), S(w), S(h));

        static readonly string SerifFamily = PickFamily("Georgia", "Palatino Linotype", "Gelasio", "DejaVu Serif");
        static readonly string MonoFamily = PickFamily("Consolas", "Cascadia Mono", "DejaVu Sans Mono");
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        static string PickFamily(params string[] names)
        {
            using (var installed = new InstalledFontCollection())
            {
                foreach (var name in names)
                    foreach (var f in installed.Families)
                        if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f.Name;
            }
            return FontFamily.GenericSerif.Name;
        }

        /// <summary>A serif font in points, sized for the current scale (forms here use AutoScaleMode.None).</summary>
        public static Font Serif(float size, FontStyle style = FontStyle.Regular) => Get(SerifFamily, size, style);
        public static Font Mono(float size) => Get(MonoFamily, size, FontStyle.Regular);

        static Font Get(string family, float size, FontStyle style)
        {
            var key = family + "|" + size + "|" + style + "|" + Scale;
            if (!fonts.TryGetValue(key, out var f))
                fonts[key] = f = new Font(family, size * Scale, style, GraphicsUnit.Point);
            return f;
        }

        static readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
        public static Image Img(string name)
        {
            if (!images.TryGetValue(name, out var img))
            {
                using (var s = Resource(name))
                {
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    ms.Position = 0;
                    images[name] = img = Image.FromStream(ms); // the stream must stay open for the image's life
                }
            }
            return img;
        }

        public static Stream Resource(string name) =>
            Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("The setup is missing its " + name + ". Download it again.");

        public static Icon AppIcon()
        {
            using (var s = Resource("app.ico")) return new Icon(s);
        }

        static TextureBrush stoneBrush;
        public static void FillStone(Graphics g, Rectangle r)
        {
            if (stoneBrush == null)
            {
                var tile = new Bitmap(Img("stone.jpg"), S(256), S(256));
                stoneBrush = new TextureBrush(tile, WrapMode.Tile);
            }
            g.FillRectangle(stoneBrush, r);
        }

        /// <summary>Draws an image scaled to cover the rectangle (cropping the overflow around the focus point).</summary>
        public static void DrawCover(Graphics g, Image img, Rectangle dest, float fx = 0.5f, float fy = 0.5f, RectangleF? region = null)
        {
            var area = region ?? new RectangleF(0, 0, img.Width, img.Height);
            float s = Math.Max(dest.Width / area.Width, dest.Height / area.Height);
            float sw = dest.Width / s, sh = dest.Height / s;
            var src = new RectangleF(area.X + (area.Width - sw) * fx, area.Y + (area.Height - sh) * fy, sw, sh);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(img, dest, src, GraphicsUnit.Pixel);
        }

        /// <summary>A top-to-bottom gradient brush. Its rectangle reaches a pixel past the area on each side: GDI+ tiles a
        /// gradient brush, and one exactly as tall as the area paints a stray line of the first colour along the bottom.</summary>
        public static LinearGradientBrush VGrad(RectangleF r, Color top, Color bottom)
        {
            var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, Math.Max(1, r.Width), r.Height + 2), top, bottom, 90f);
            b.WrapMode = WrapMode.TileFlipXY;
            return b;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            if (radius <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>A recessed dark panel with a thin gold rim and small gold corner studs, like the launcher's cards.</summary>
        public static void DrawPanel(Graphics g, Rectangle r, Color? fill = null, int alpha = 225, bool studs = true)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, S(4)))
            using (var b = new SolidBrush(Color.FromArgb(alpha, fill ?? Panel)))
                g.FillPath(b, path);
            var inner = Rectangle.Inflate(r, -S(3), -S(3));
            using (var path = Round(inner, S(3)))
            using (var pen = new Pen(Color.FromArgb(60, Gold), 1))
                g.DrawPath(pen, path);
            using (var path = Round(r, S(4)))
            using (var pen = new Pen(Color.FromArgb(150, GoldDeep), Math.Max(1, S(1.2f))))
                g.DrawPath(pen, path);
            if (studs)
            {
                foreach (var c in new[] { new Point(r.Left, r.Top), new Point(r.Right, r.Top), new Point(r.Left, r.Bottom), new Point(r.Right, r.Bottom) })
                    Diamond(g, c, S(3.5f), Gold);
            }
        }

        public static void Diamond(Graphics g, Point c, float rad, Color color)
        {
            var pts = new[] { new PointF(c.X, c.Y - rad), new PointF(c.X + rad, c.Y), new PointF(c.X, c.Y + rad), new PointF(c.X - rad, c.Y) };
            using (var b = new SolidBrush(color)) g.FillPolygon(b, pts);
        }

        /// <summary>A gold hairline that fades at both ends, with a diamond in the middle.</summary>
        public static void Rule(Graphics g, int x1, int x2, int y, int alpha = 200)
        {
            int mid = (x1 + x2) / 2;
            using (var lb = new LinearGradientBrush(new Rectangle(x1, y, Math.Max(2, x2 - x1), 2), Color.Transparent, Color.Transparent, 0f))
            {
                var blend = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, Gold), Color.FromArgb(alpha, Gold), Color.FromArgb(0, Gold) },
                    Positions = new[] { 0f, 0.5f, 1f }
                };
                lb.InterpolationColors = blend;
                g.FillRectangle(lb, x1, y, x2 - x1, Math.Max(1, S(1)));
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Diamond(g, new Point(mid, y), S(3.5f), GoldLight);
        }

        public static void DrawText(Graphics g, string text, Font font, Rectangle r, Color color,
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak)
        {
            TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        /// <summary>Text with a soft dark shadow, for lettering over the art.</summary>
        public static void DrawShadowText(Graphics g, string text, Font font, Rectangle r, Color color,
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var sf = ToFormat(flags);
            using (var sh = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            {
                foreach (var o in new[] { new Point(1, 1), new Point(2, 2), new Point(0, 2) })
                {
                    var rr = r; rr.Offset(S(o.X * 0.6f), S(o.Y * 0.6f));
                    g.DrawString(text, font, sh, rr, sf);
                }
            }
            using (var b = new SolidBrush(color)) g.DrawString(text, font, b, r, sf);
        }

        static StringFormat ToFormat(TextFormatFlags flags)
        {
            var sf = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisWord };
            sf.FormatFlags &= ~StringFormatFlags.LineLimit;
            if ((flags & TextFormatFlags.HorizontalCenter) != 0) sf.Alignment = StringAlignment.Center;
            else if ((flags & TextFormatFlags.Right) != 0) sf.Alignment = StringAlignment.Far;
            if ((flags & TextFormatFlags.VerticalCenter) != 0) sf.LineAlignment = StringAlignment.Center;
            else if ((flags & TextFormatFlags.Bottom) != 0) sf.LineAlignment = StringAlignment.Far;
            if ((flags & TextFormatFlags.WordBreak) == 0) sf.FormatFlags |= StringFormatFlags.NoWrap;
            return sf;
        }

        public static Size Measure(string text, Font font, int width)
            => TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

        public static string Gb(long bytes) => bytes >= 10L << 30 ? (bytes / 1073741824.0).ToString("0") + " GB"
            : bytes >= 1L << 30 ? (bytes / 1073741824.0).ToString("0.0") + " GB"
            : bytes >= 1L << 20 ? (bytes / 1048576.0).ToString("0") + " MB"
            : Math.Max(1, bytes / 1024) + " KB";
    }
}
