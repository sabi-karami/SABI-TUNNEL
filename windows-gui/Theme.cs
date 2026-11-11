using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;

namespace TT
{
    /// <summary>"Shab-e Firuze" (turquoise night) palette: turquoise, gold, violet and aurora pink.</summary>
    static class Theme
    {
        public static readonly Color Bg0 = Rgb(0x0D, 0x10, 0x2C);
        public static readonly Color Bg1 = Rgb(0x03, 0x04, 0x0B);
        public static readonly Color Panel = Rgb(0x11, 0x15, 0x30);
        public static readonly Color Field = Rgb(0x08, 0x0B, 0x1C);

        public static readonly Color Turq = Rgb(0x2D, 0xE2, 0xE6), Gold = Rgb(0xF5, 0xC7, 0x6B), Violet = Rgb(0x8B, 0x5C, 0xF6);
        public static readonly Color Pink = Rgb(0xFF, 0x4F, 0xD8), Ok = Rgb(0x3E, 0xF0, 0xA1);
        public static readonly Color Stroke = Color.FromArgb(20, 255, 255, 255);
        public static readonly Color StrokeHi = Color.FromArgb(40, 255, 255, 255);
        public static readonly Color Text = Rgb(0xEF, 0xF2, 0xFF);
        public static readonly Color Muted = Rgb(0x93, 0x9C, 0xC4);
        public static readonly Color Dim = Rgb(0x5E, 0x66, 0x8E);
        public static readonly Color Track = Rgb(0x17, 0x1C, 0x3A);

        public static readonly Color On1 = Turq, On2 = Ok;
        public static readonly Color Wait1 = Gold, Wait2 = Rgb(0xFF, 0x8A, 0x4C);
        public static readonly Color Off1 = Violet, Off2 = Pink;
        public static readonly Color Idle = Rgb(0x4A, 0x52, 0x80);
        public static readonly Color Danger = Rgb(0xFF, 0x5C, 0x7A);
        public static readonly Color DangerSoft = Rgb(0xFF, 0x9A, 0xAE);

        public static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);
        public static Color A(Color c, int a) => Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);
        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                                  (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>Embedded Vazirmatn (OFL) with a system fallback.</summary>
    static class FontKit
    {
        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pb, uint cb, IntPtr pdv, out uint pcFonts);

        static readonly PrivateFontCollection pfc = new PrivateFontCollection();
        static readonly FontFamily regular, medium, black;
        static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
        public static readonly bool Embedded;
        public static readonly string Mono;

        static FontKit()
        {
            try
            {
                foreach (var n in new[] { "Vazirmatn-Regular.ttf", "Vazirmatn-Bold.ttf", "Vazirmatn-Medium.ttf", "Vazirmatn-Black.ttf" }) Load(n);
                foreach (var f in pfc.Families)
                {
                    if (f.Name == "Vazirmatn") regular = f;
                    else if (f.Name.EndsWith("Medium")) medium = f;
                    else if (f.Name.EndsWith("Black")) black = f;
                }
                Embedded = regular != null;
            }
            catch { Embedded = false; }
            Mono = Installed("Cascadia Mono") ? "Cascadia Mono" : "Consolas";
        }

        static bool Installed(string name)
        {
            try { using (var f = new Font(name, 9f)) return f.Name == name; } catch { return false; }
        }

        static void Load(string name)
        {
            using (var s = typeof(FontKit).Assembly.GetManifestResourceStream("fonts." + name))
            {
                if (s == null) return;
                var data = new byte[s.Length];
                int read = 0; while (read < data.Length) { int k = s.Read(data, read, data.Length - read); if (k <= 0) break; read += k; }
                IntPtr p = Marshal.AllocCoTaskMem(data.Length);          // must stay alive for the process lifetime
                Marshal.Copy(data, 0, p, data.Length);
                pfc.AddMemoryFont(p, data.Length);
                AddFontMemResourceEx(p, (uint)data.Length, IntPtr.Zero, out _); // lets GDI controls (TextBox, menus) use it too
            }
        }

        /// <summary>Pixel-sized font for custom painting (scaled by the canvas transform). weight: 400/500/700/900.</summary>
        public static Font Px(float px, int weight = 400) => Get(px, weight, GraphicsUnit.Pixel);

        /// <summary>Point-sized font for native controls (Windows scales points with DPI).</summary>
        public static Font Pt(float pt, int weight = 400) => Get(pt, weight, GraphicsUnit.Point);

        static Font Get(float size, int weight, GraphicsUnit unit)
        {
            var key = size + "|" + weight + "|" + unit;
            lock (cache)
            {
                if (cache.TryGetValue(key, out var f)) return f;
                f = Make(size, weight, unit);
                cache[key] = f;
                return f;
            }
        }

        static Font Make(float size, int weight, GraphicsUnit unit)
        {
            if (Embedded)
            {
                try
                {
                    if (weight >= 850 && black != null) return new Font(black, size, FontStyle.Regular, unit);
                    if (weight >= 600) return new Font(regular, size, FontStyle.Bold, unit);
                    if (weight >= 500 && medium != null) return new Font(medium, size, FontStyle.Regular, unit);
                    return new Font(regular, size, FontStyle.Regular, unit);
                }
                catch { }
            }
            var fam = Installed("Segoe UI") ? "Segoe UI" : "Tahoma";
            return new Font(fam, size, weight >= 600 ? FontStyle.Bold : FontStyle.Regular, unit);
        }

        public static Font MonoPt(float pt)
        {
            var key = "mono|" + pt;
            lock (cache)
            {
                if (cache.TryGetValue(key, out var f)) return f;
                f = new Font(Mono, pt, FontStyle.Regular, GraphicsUnit.Point);
                cache[key] = f;
                return f;
            }
        }
    }

    enum Al { Right, Center, Left }

    static class Gfx
    {
        public static void Hq(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Max(0.1f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float rad, Color c, float w = 1f)
        {
            var rr = new RectangleF(r.X + w / 2, r.Y + w / 2, r.Width - w, r.Height - w);
            using (var p = Round(rr, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        /// <summary>A frosted glass card: tinted translucent fill, aurora-gradient hairline and a lit top edge.</summary>
        public static void Card(Graphics g, RectangleF r, float rad = 16, int fill = 12)
        {
            using (var p = Round(r, rad))
            {
                using (var b = Art.Grad(r, 90f, Color.FromArgb(fill + 8, 190, 205, 255), Color.FromArgb(Math.Max(3, fill - 2), 150, 140, 255), Color.FromArgb(Math.Max(3, fill - 6), 120, 110, 220)))
                    g.FillPath(b, p);
                using (var b = Art.Grad(r, 25f, Theme.A(Theme.Turq, 60 + fill), Color.FromArgb(14, 255, 255, 255), Theme.A(Theme.Violet, 40 + fill), Theme.A(Theme.Gold, 34 + fill)))
                using (var pen = new Pen(b, 1f))
                    g.DrawPath(pen, p);
            }
            using (var b = Art.Grad(new RectangleF(r.X + rad, r.Y, Math.Max(4, r.Width - rad * 2), 1), 0f, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
            using (var pen = new Pen(b, 1f))
                g.DrawLine(pen, r.X + rad, r.Y + 0.5f, r.Right - rad, r.Y + 0.5f);
        }

        /// <summary>Soft radial glow.</summary>
        public static void Glow(Graphics g, PointF c, float rx, float ry, Color col, int alpha)
        {
            if (alpha <= 0 || rx < 1 || ry < 1) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - rx, c.Y - ry, rx * 2, ry * 2);
                using (var b = new PathGradientBrush(path))
                {
                    b.CenterPoint = c;
                    b.CenterColor = Theme.A(col, alpha);
                    b.SurroundColors = new[] { Theme.A(col, 0) };
                    b.InterpolationColors = new ColorBlend(4)
                    {
                        Colors = new[] { Theme.A(col, 0), Theme.A(col, alpha / 6), Theme.A(col, alpha / 2), Theme.A(col, alpha) },
                        Positions = new[] { 0f, 0.35f, 0.72f, 1f }
                    };
                    g.FillPath(b, path);
                }
            }
        }

        static readonly Dictionary<int, StringFormat> formats = new Dictionary<int, StringFormat>();

        internal static StringFormat Fmt(Al al, bool rtl, bool wrap, StringAlignment v)
        {
            int key = (int)al | (rtl ? 8 : 0) | (wrap ? 16 : 0) | ((int)v << 5);
            lock (formats)
            {
                if (formats.TryGetValue(key, out var f)) return f;
                f = new StringFormat(rtl ? StringFormatFlags.DirectionRightToLeft : 0);
                if (!wrap) { f.FormatFlags |= StringFormatFlags.NoWrap; f.Trimming = StringTrimming.EllipsisCharacter; }
                else f.Trimming = StringTrimming.Word;
                f.LineAlignment = v;
                if (al == Al.Center) f.Alignment = StringAlignment.Center;
                else if (al == Al.Right) f.Alignment = rtl ? StringAlignment.Near : StringAlignment.Far;
                else f.Alignment = rtl ? StringAlignment.Far : StringAlignment.Near;
                formats[key] = f;
                return f;
            }
        }

        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, Al al = Al.Right, bool rtl = true,
                                bool wrap = false, StringAlignment v = StringAlignment.Center)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, r, Fmt(al, rtl, wrap, v));
        }

        public static SizeF Measure(Graphics g, string s, Font f, float width = 10000, bool rtl = true)
            => g.MeasureString(string.IsNullOrEmpty(s) ? " " : s, f, new SizeF(width, 10000), Fmt(Al.Right, rtl, width < 10000, StringAlignment.Near));

        /// <summary>Latin digits to Persian digits.</summary>
        public static string Fa(string s)
        {
            if (s == null) return null;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(ch >= '0' && ch <= '9' ? (char)('\u06F0' + (ch - '0')) : ch == '.' ? '\u066B' : ch);
            return sb.ToString();
        }

        public static string Bytes(double b, bool rate = false)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1000 && i < u.Length - 1) { b /= 1024; i++; }
            string n = b >= 100 || i == 0 ? b.ToString("0") : b >= 10 ? b.ToString("0.0") : b.ToString("0.00");
            return n + " " + u[i] + (rate ? "/s" : "");
        }

        public static float Ease(float t) { t = Math.Max(0, Math.Min(1, t)); return 1 - (float)Math.Pow(1 - t, 3); }
    }

    /// <summary>Line icons drawn with GDI+ (font-independent).</summary>
    static class Glyph
    {
        static Pen P(Color c, float w) => new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        public static void Power(Graphics g, RectangleF r, Color c, float w = 2.6f)
        {
            using (var p = P(c, w))
            {
                var rr = RectangleF.Inflate(r, -w, -w);
                g.DrawArc(p, rr, -55, 290);
                g.DrawLine(p, r.X + r.Width / 2, r.Y + w * 0.3f, r.X + r.Width / 2, r.Y + r.Height * 0.46f);
            }
        }

        public static void Close(Graphics g, RectangleF r, Color c, float w = 1.5f)
        {
            using (var p = P(c, w)) { g.DrawLine(p, r.Left, r.Top, r.Right, r.Bottom); g.DrawLine(p, r.Right, r.Top, r.Left, r.Bottom); }
        }

        public static void Minimize(Graphics g, RectangleF r, Color c, float w = 1.5f)
        {
            using (var p = P(c, w)) g.DrawLine(p, r.Left, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
        }

        public static void Gear(Graphics g, RectangleF r, Color c, float w = 1.6f)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, R = r.Width / 2, ri = R * 0.34f;
            using (var p = P(c, w))
            {
                var pts = new List<PointF>();
                const int teeth = 8;
                for (int i = 0; i < teeth * 4; i++)
                {
                    double a = (i - 0.5) * Math.PI * 2 / (teeth * 4) - Math.PI / 2;
                    float rad = (i % 4 == 0 || i % 4 == 1) ? R : R * 0.76f;
                    pts.Add(new PointF(cx + (float)Math.Cos(a) * rad, cy + (float)Math.Sin(a) * rad));
                }
                g.DrawPolygon(p, pts.ToArray());
                g.DrawEllipse(p, cx - ri, cy - ri, ri * 2, ri * 2);
            }
        }

        /// <summary>Arrow pointing right (= "back" in an RTL layout).</summary>
        public static void Back(Graphics g, RectangleF r, Color c, float w = 1.9f)
        {
            using (var p = P(c, w))
            {
                float cy = r.Y + r.Height / 2;
                g.DrawLine(p, r.Left, cy, r.Right, cy);
                g.DrawLine(p, r.Right - r.Width * 0.45f, r.Top + r.Height * 0.06f, r.Right, cy);
                g.DrawLine(p, r.Right - r.Width * 0.45f, r.Bottom - r.Height * 0.06f, r.Right, cy);
            }
        }

        public static void Arrow(Graphics g, RectangleF r, Color c, bool down, float w = 1.8f)
        {
            using (var p = P(c, w))
            {
                float cx = r.X + r.Width / 2, t = r.Top, b = r.Bottom, k = r.Width * 0.4f;
                if (down) { g.DrawLine(p, cx, t, cx, b); g.DrawLine(p, cx - k, b - k, cx, b); g.DrawLine(p, cx + k, b - k, cx, b); }
                else { g.DrawLine(p, cx, b, cx, t); g.DrawLine(p, cx - k, t + k, cx, t); g.DrawLine(p, cx + k, t + k, cx, t); }
            }
        }

        public static void Link(Graphics g, RectangleF r, Color c, float w = 1.6f)
        {
            using (var p = P(c, w))
            {
                var st = g.Save();
                g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
                g.RotateTransform(-45);
                float h = r.Height * 0.40f, ww = r.Width * 0.60f;
                using (var a = Gfx.Round(new RectangleF(-ww + h * 0.55f, -h / 2, ww, h), h / 2)) g.DrawPath(p, a);
                using (var b = Gfx.Round(new RectangleF(-h * 0.55f, -h / 2, ww, h), h / 2)) g.DrawPath(p, b);
                g.Restore(st);
            }
        }

        public static void Globe(Graphics g, RectangleF r, Color c, float w = 1.5f)
        {
            using (var p = P(c, w))
            {
                g.DrawEllipse(p, r);
                g.DrawEllipse(p, r.X + r.Width * 0.28f, r.Y, r.Width * 0.44f, r.Height);
                g.DrawLine(p, r.X, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
            }
        }

        public static void Doc(Graphics g, RectangleF r, Color c, float w = 1.5f)
        {
            using (var p = P(c, w))
            {
                var rr = new RectangleF(r.X + r.Width * 0.14f, r.Y, r.Width * 0.72f, r.Height);
                using (var path = Gfx.Round(rr, 2.5f)) g.DrawPath(p, path);
                for (int i = 0; i < 3; i++)
                {
                    float y = r.Y + r.Height * (0.32f + i * 0.2f);
                    g.DrawLine(p, rr.X + rr.Width * 0.24f, y, rr.Right - rr.Width * (i == 2 ? 0.45f : 0.24f), y);
                }
            }
        }

        public static void Folder(Graphics g, RectangleF r, Color c, float w = 1.5f)
        {
            using (var p = P(c, w))
            {
                var pts = new[] {
                    new PointF(r.Left, r.Top + r.Height * 0.15f), new PointF(r.Left + r.Width * 0.38f, r.Top + r.Height * 0.15f),
                    new PointF(r.Left + r.Width * 0.5f, r.Top + r.Height * 0.3f), new PointF(r.Right, r.Top + r.Height * 0.3f),
                    new PointF(r.Right, r.Bottom - r.Height * 0.08f), new PointF(r.Left, r.Bottom - r.Height * 0.08f) };
                g.DrawPolygon(p, pts);
            }
        }

        public static void Trash(Graphics g, RectangleF r, Color c, float w = 1.5f)
        {
            using (var p = P(c, w))
            {
                float x = r.X, y = r.Y, W = r.Width, H = r.Height;
                g.DrawLine(p, x + W * 0.08f, y + H * 0.22f, x + W * 0.92f, y + H * 0.22f);
                g.DrawLine(p, x + W * 0.36f, y + H * 0.06f, x + W * 0.64f, y + H * 0.06f);
                g.DrawLine(p, x + W * 0.36f, y + H * 0.06f, x + W * 0.36f, y + H * 0.22f);
                g.DrawLine(p, x + W * 0.64f, y + H * 0.06f, x + W * 0.64f, y + H * 0.22f);
                g.DrawLines(p, new[] { new PointF(x + W * 0.2f, y + H * 0.3f), new PointF(x + W * 0.26f, y + H * 0.96f), new PointF(x + W * 0.74f, y + H * 0.96f), new PointF(x + W * 0.8f, y + H * 0.3f) });
                g.DrawLine(p, x + W * 0.42f, y + H * 0.42f, x + W * 0.42f, y + H * 0.8f);
                g.DrawLine(p, x + W * 0.58f, y + H * 0.42f, x + W * 0.58f, y + H * 0.8f);
            }
        }

        public static void Copy(Graphics g, RectangleF r, Color c, float w = 1.5f)
        {
            using (var p = P(c, w))
            {
                using (var a = Gfx.Round(new RectangleF(r.X + r.Width * 0.3f, r.Y, r.Width * 0.7f, r.Height * 0.7f), 2.5f)) g.DrawPath(p, a);
                using (var b = Gfx.Round(new RectangleF(r.X, r.Y + r.Height * 0.3f, r.Width * 0.7f, r.Height * 0.7f), 2.5f)) g.DrawPath(p, b);
            }
        }

        public static void Shield(Graphics g, RectangleF r, Color c, float w = 1.6f, bool check = true)
        {
            using (var p = P(c, w))
            using (var path = new GraphicsPath())
            {
                float cx = r.X + r.Width / 2;
                path.AddBezier(cx, r.Top, cx + r.Width * 0.25f, r.Top + r.Height * 0.1f, r.Right, r.Top + r.Height * 0.12f, r.Right, r.Top + r.Height * 0.18f);
                path.AddBezier(r.Right, r.Top + r.Height * 0.18f, r.Right, r.Top + r.Height * 0.62f, cx + r.Width * 0.2f, r.Bottom - r.Height * 0.1f, cx, r.Bottom);
                path.AddBezier(cx, r.Bottom, cx - r.Width * 0.2f, r.Bottom - r.Height * 0.1f, r.Left, r.Top + r.Height * 0.62f, r.Left, r.Top + r.Height * 0.18f);
                path.AddBezier(r.Left, r.Top + r.Height * 0.18f, r.Left, r.Top + r.Height * 0.12f, cx - r.Width * 0.25f, r.Top + r.Height * 0.1f, cx, r.Top);
                g.DrawPath(p, path);
                if (check)
                    g.DrawLines(p, new[] { new PointF(r.X + r.Width * 0.32f, r.Y + r.Height * 0.5f), new PointF(r.X + r.Width * 0.46f, r.Y + r.Height * 0.64f), new PointF(r.X + r.Width * 0.7f, r.Y + r.Height * 0.38f) });
            }
        }

        public static void Pulse(Graphics g, RectangleF r, Color c, float w = 1.6f)
        {
            using (var p = P(c, w))
            {
                float y = r.Y + r.Height / 2;
                g.DrawLines(p, new[] {
                    new PointF(r.Left, y), new PointF(r.Left + r.Width * 0.28f, y), new PointF(r.Left + r.Width * 0.4f, r.Top),
                    new PointF(r.Left + r.Width * 0.58f, r.Bottom), new PointF(r.Left + r.Width * 0.7f, y), new PointF(r.Right, y) });
            }
        }

        public static void Info(Graphics g, RectangleF r, Color c, char kind, float w = 2.2f)
        {
            using (var p = P(c, w))
            {
                float cx = r.X + r.Width / 2;
                if (kind == '!')
                {
                    g.DrawLine(p, cx, r.Top + r.Height * 0.12f, cx, r.Top + r.Height * 0.6f);
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - w * 0.8f, r.Bottom - r.Height * 0.16f - w * 0.8f, w * 1.6f, w * 1.6f);
                }
                else if (kind == '?')
                {
                    g.DrawArc(p, cx - r.Width * 0.24f, r.Top + r.Height * 0.06f, r.Width * 0.48f, r.Height * 0.42f, 190, 250);
                    g.DrawLine(p, cx, r.Top + r.Height * 0.48f, cx, r.Top + r.Height * 0.62f);
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - w * 0.8f, r.Bottom - r.Height * 0.14f - w * 0.8f, w * 1.6f, w * 1.6f);
                }
                else if (kind == 'v')
                {
                    g.DrawLines(p, new[] { new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.74f), new PointF(r.X + r.Width * 0.8f, r.Y + r.Height * 0.3f) });
                }
                else
                {
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - w * 0.8f, r.Top + r.Height * 0.16f - w * 0.8f, w * 1.6f, w * 1.6f);
                    g.DrawLine(p, cx, r.Top + r.Height * 0.4f, cx, r.Bottom - r.Height * 0.1f);
                }
            }
        }

        /// <summary>The brand mark, same as the Android icon, app.ico and the web logo (all drawn by the same rules):
        /// a gold khatam of two squares on a night-filled star, a neon pointed Persian arch with nested arches and a
        /// light at the end of the tunnel. Three levels of detail: tiny (&lt;24px: star outline, arch, light dot),
        /// mid (24-39px: + khatam lines, nested arches, glow) and big (40px+: + circle, gold halo, sparkles).</summary>
        public static void Brand(Graphics g, RectangleF r) => Brand(g, r, Theme.Turq, Color.White, Theme.Gold);

        public static void Brand(Graphics g, RectangleF r, Color neon, Color light) => Brand(g, r, neon, light, Theme.Gold);

        public static void Brand(Graphics g, RectangleF r, Color neon, Color light, Color star)
        {
            float S = Math.Min(r.Width, r.Height);
            if (S < 8) return;
            int kind = S < 24 ? 0 : S < 40 ? 1 : 2;
            float gw = kind == 0 ? Math.Max(1.05f, 0.068f * S) : kind == 1 ? Math.Max(1.15f, 0.05f * S) : Math.Max(1.3f, 0.03f * S);
            float aw = kind == 0 ? Math.Max(1.25f, 0.09f * S) : kind == 1 ? Math.Max(1.4f, 0.066f * S) : Math.Max(1.6f, 0.036f * S);
            float gm = kind == 0 ? 0f : kind == 1 ? 0.012f * S : 0.03f * S;
            float R = S / 2 - gw / 2 - gm - 0.25f;
            var c = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            float a, ys, yb, vp;
            float[] nest;
            if (kind == 0) { a = 0.46f; ys = 0f; yb = 0.46f; vp = 0.27f; nest = new float[0]; }
            else if (kind == 1) { a = 0.38f; ys = 0f; yb = 0.38f; vp = 0.16f; nest = S >= 28 ? new[] { 0.62f, 0.32f } : new[] { 0.55f }; }
            else
            {
                a = 0.42f; ys = -0.02f; yb = 0.55f; vp = 0.26f;
                nest = S >= 96 ? new[] { 0.80f, 0.62f, 0.46f, 0.33f, 0.22f } : S >= 56 ? new[] { 0.74f, 0.52f, 0.33f } : new[] { 0.70f, 0.42f };
            }
            a *= R; ys = c.Y + ys * R; yb = c.Y + yb * R; vp = c.Y + vp * R;
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // night-filled star + gold khatam
            using (var starPath = Art.Star(c, R, R * Art.Khatam, 0))
            {
                if (kind == 2)
                    for (int i = 3; i >= 1; i--)
                        using (var p = new Pen(Theme.A(star, 20), gw * (1 + i * 0.9f)) { LineJoin = LineJoin.Round })
                            g.DrawPath(p, starPath);
                using (var b = new LinearGradientBrush(new RectangleF(c.X - R, c.Y - R - 1, R * 2, R * 2 + 2), Theme.Rgb(0x0B, 0x10, 0x30), Theme.Rgb(0x06, 0x08, 0x14), 90f))
                    g.FillPath(b, starPath);
                using (var p = new Pen(star, gw) { LineJoin = LineJoin.Round })
                {
                    if (kind == 0) g.DrawPath(p, starPath);
                    else { g.DrawPolygon(p, Square(c, R, 45f)); g.DrawPolygon(p, Square(c, R, 0f)); }
                }
            }
            if (kind == 2)
            {
                float r2 = R * 0.70f;
                using (var p = new Pen(Theme.A(star, 140), Math.Max(0.8f, gw * 0.5f))) g.DrawEllipse(p, c.X - r2, c.Y - r2, r2 * 2, r2 * 2);
            }

            // the tunnel: dark inside, glow, nested arches, light at the end
            float apex = ys - a * 1.3f * 0.97301f;
            using (var tun = ArchPath(c.X, a, ys, yb, true))
            using (var b = new LinearGradientBrush(new RectangleF(c.X - a, apex - 1, a * 2, yb - apex + 2), Theme.Rgb(5, 8, 28), Theme.Mix(Theme.Rgb(5, 8, 28), neon, 0.2f), 90f))
                g.FillPath(b, tun);
            if (kind > 0) Gfx.Glow(g, new PointF(c.X, vp), a * 1.05f, a * 1.05f, neon, kind == 2 ? 190 : 128);
            for (int i = 0; i < nest.Length; i++)
            {
                float f = nest[i];
                float w = kind == 2 ? Math.Max(0.9f, aw * (0.55f - 0.06f * i)) : Math.Max(0.85f, aw * 0.5f);
                using (var path = ArchPath(c.X, a * f, vp + (ys - vp) * f, vp + (yb - vp) * f, false))
                using (var p = P(Theme.A(neon, 235 - 40 * i), w))
                    g.DrawPath(p, path);
            }
            bool white = light.ToArgb() == Color.White.ToArgb();
            if (kind == 0)
            {
                float d = R * 0.115f;
                using (var b = new SolidBrush(white ? Theme.Mix(neon, Color.White, 0.6f) : Theme.Mix(light, Color.White, 0.3f)))
                    g.FillEllipse(b, c.X - d, vp - d, d * 2, d * 2);
            }
            else
                Gfx.Glow(g, new PointF(c.X, vp), a * 0.42f, a * 0.42f, Theme.Mix(light, Color.White, 0.5f), 255);

            // neon outer arch
            using (var arch = ArchPath(c.X, a, ys, yb, false))
            {
                if (kind == 2)
                {
                    using (var p = P(Theme.A(neon, 36), aw * 4.2f)) g.DrawPath(p, arch);
                    using (var p = P(Theme.A(neon, 70), aw * 2.4f)) g.DrawPath(p, arch);
                }
                else if (kind == 1)
                    using (var p = P(Theme.A(neon, 64), aw * 2.2f)) g.DrawPath(p, arch);
                using (var p = P(neon, aw)) g.DrawPath(p, arch);
                if (kind > 0)
                    using (var p = P(Theme.A(Theme.Mix(neon, Color.White, 0.8f), 230), Math.Max(0.6f, aw * 0.32f))) g.DrawPath(p, arch);
            }
            if (kind == 2 && S >= 64)
            {
                var sp = new[] { new[] { 0.74f, 0.26f, 1f }, new[] { 0.25f, 0.33f, 0.65f }, new[] { 0.71f, 0.73f, 0.5f } };
                foreach (var q in sp)
                    Art.Sparkle(g, new PointF(c.X + (q[0] - 0.5f) * R * 2.5f, c.Y + (q[1] - 0.5f) * R * 2.5f), 0.08f * R * q[2], Color.White, 240);
            }
            g.SmoothingMode = oldMode;
        }

        /// <summary>Square with corners on a circle of radius R, first corner at rotDeg (45 = upright square, 0 = diamond).</summary>
        static PointF[] Square(PointF c, float R, float rotDeg)
        {
            var pts = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                double t = (rotDeg + 90 * i) * Math.PI / 180;
                pts[i] = new PointF(c.X + (float)Math.Cos(t) * R, c.Y + (float)Math.Sin(t) * R);
            }
            return pts;
        }

        /// <summary>Pointed Persian arch, open at the bottom: legs at cx-a and cx+a from yb up to ys, then two arcs of
        /// radius 1.3a whose centres sit 0.3a past the axis, meeting in a point.</summary>
        static GraphicsPath ArchPath(float cx, float a, float ys, float yb, bool closed)
        {
            const float Th = 76.6582f;   // acos(0.3 / 1.3)
            float rr = a * 1.3f;
            var p = new GraphicsPath();
            p.AddLine(cx + a, yb, cx + a, ys);
            p.AddArc(cx - 0.3f * a - rr, ys - rr, rr * 2, rr * 2, 0f, -Th);
            p.AddArc(cx + 0.3f * a - rr, ys - rr, rr * 2, rr * 2, 180f + Th, -Th);
            p.AddLine(cx - a, ys, cx - a, yb);
            if (closed) p.CloseFigure();
            return p;
        }

        /// <summary>Signal bars (0-4 lit).</summary>
        public static void Bars(Graphics g, RectangleF r, Color on, Color off, int lit)
        {
            float bw = r.Width / 5.5f;
            for (int i = 0; i < 4; i++)
            {
                float h = r.Height * (0.35f + 0.65f * i / 3f);
                var br = new RectangleF(r.X + i * bw * 1.5f, r.Bottom - h, bw, h);
                Gfx.FillRound(g, br, bw / 2, i < lit ? on : off);
            }
        }

        /// <summary>Up + down arrows (data volume).</summary>
        public static void Data(Graphics g, RectangleF r, Color c, float w = 1.5f)
        {
            float hw = r.Width / 2;
            Arrow(g, new RectangleF(r.X, r.Y, hw - 1, r.Height), c, false, w);
            Arrow(g, new RectangleF(r.X + hw + 1, r.Y, hw - 1, r.Height), c, true, w);
        }
    }
}
