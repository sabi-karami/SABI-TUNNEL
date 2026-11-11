using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TT
{
    /// <summary>"Shab-e Firuze" (turquoise night) art kit: khatam stars, girih, aurora sky, cached layers.</summary>
    static class Art
    {
        /// <summary>Inner/outer radius ratio of a khatam (two overlapped squares).</summary>
        public const float Khatam = 0.7654f;

        public static GraphicsPath Star(PointF c, float R, float r, float rotDeg, int points = 8)
        {
            var pts = new PointF[points * 2];
            for (int i = 0; i < points * 2; i++)
            {
                double a = (rotDeg - 90) * Math.PI / 180 + i * Math.PI / points;
                float rad = i % 2 == 0 ? R : r;
                pts[i] = new PointF(c.X + (float)Math.Cos(a) * rad, c.Y + (float)Math.Sin(a) * rad);
            }
            var p = new GraphicsPath();
            p.AddPolygon(pts);
            return p;
        }

        public static PointF Tip(PointF c, float R, float rotDeg, int i, int points = 8)
        {
            double a = (rotDeg - 90) * Math.PI / 180 + i * 2 * Math.PI / points;
            return new PointF(c.X + (float)Math.Cos(a) * R, c.Y + (float)Math.Sin(a) * R);
        }

        public static ColorBlend Blend(params Color[] cols)
        {
            var pos = new float[cols.Length];
            for (int i = 0; i < cols.Length; i++) pos[i] = i / (float)(cols.Length - 1);
            return new ColorBlend(cols.Length) { Colors = cols, Positions = pos };
        }

        public static LinearGradientBrush Grad(RectangleF r, float angle, params Color[] cols)
        {
            var rr = new RectangleF(r.X - 1, r.Y - 1, Math.Max(2, r.Width + 2), Math.Max(2, r.Height + 2));
            var b = new LinearGradientBrush(rr, cols[0], cols[cols.Length - 1], angle);
            if (cols.Length > 2) b.InterpolationColors = Blend(cols);
            return b;
        }

        /// <summary>Text filled with a horizontal gradient.</summary>
        public static void GradText(Graphics g, string s, Font f, RectangleF r, Al al, bool rtl, params Color[] cols)
        {
            if (string.IsNullOrEmpty(s)) return;
            var sz = Gfx.Measure(g, s, f, 10000, rtl);
            float x = al == Al.Center ? r.X + (r.Width - sz.Width) / 2 : al == Al.Right ? r.Right - sz.Width : r.X;
            using (var b = Grad(new RectangleF(x, r.Y, Math.Max(4, sz.Width), r.Height), 0f, cols))
                g.DrawString(s, f, b, r, Gfx.Fmt(al, rtl, false, StringAlignment.Center));
        }

        /// <summary>Four-point twinkle.</summary>
        public static void Sparkle(Graphics g, PointF c, float s, Color col, int alpha)
        {
            if (alpha <= 0) return;
            using (var path = new GraphicsPath())
            {
                float k = s * 0.22f;
                path.AddPolygon(new[] {
                    new PointF(c.X, c.Y - s), new PointF(c.X + k, c.Y - k), new PointF(c.X + s, c.Y), new PointF(c.X + k, c.Y + k),
                    new PointF(c.X, c.Y + s), new PointF(c.X - k, c.Y + k), new PointF(c.X - s, c.Y), new PointF(c.X - k, c.Y - k) });
                using (var b = new SolidBrush(Theme.A(col, alpha))) g.FillPath(b, path);
            }
        }

        /// <summary>Faint girih lattice (khatam stars + small diamonds), fading downwards.</summary>
        public static void Girih(Graphics g, RectangleF area, int maxAlpha, float s = 44)
        {
            for (float y = area.Y; y <= area.Bottom + s; y += s)
            {
                float fade = 1 - Math.Max(0, Math.Min(1, (y - area.Y) / area.Height));
                int a = (int)(maxAlpha * fade * fade);
                if (a <= 1) continue;
                using (var p = new Pen(Theme.A(Theme.Turq, a), 1f))
                using (var q = new Pen(Theme.A(Theme.Gold, a * 2 / 3), 1f))
                    for (float x = area.X; x <= area.Right + s; x += s)
                    {
                        using (var st = Star(new PointF(x, y), s * 0.5f, s * 0.5f * Khatam, 0)) g.DrawPath(p, st);
                        float d = s * 0.207f;
                        var c = new PointF(x + s / 2, y + s / 2);
                        g.DrawPolygon(q, new[] { new PointF(c.X, c.Y - d), new PointF(c.X + d, c.Y), new PointF(c.X, c.Y + d), new PointF(c.X - d, c.Y) });
                    }
            }
        }

        /// <summary>Window sky: deep gradient, aurora blobs, girih veil and a star field.</summary>
        public static void Sky(Graphics g, RectangleF L, int seed)
        {
            using (var b = Grad(L, 90f, Theme.Bg0, Theme.Rgb(0x07, 0x09, 0x1A), Theme.Bg1)) g.FillRectangle(b, L);
            Gfx.Glow(g, new PointF(L.Right - 30, L.Y - 20), L.Width * 0.8f, 200, Theme.Turq, 34);
            Gfx.Glow(g, new PointF(L.X - 40, L.Y + L.Height * 0.40f), L.Width * 0.62f, 230, Theme.Violet, 34);
            Gfx.Glow(g, new PointF(L.X + L.Width * 0.72f, L.Bottom + 30), L.Width * 0.75f, 210, Theme.Pink, 20);
            Gfx.Glow(g, new PointF(L.X + L.Width * 0.22f, L.Y + 24), L.Width * 0.45f, 110, Theme.Gold, 12);
            Girih(g, new RectangleF(L.X - 6, L.Y - 6, L.Width + 12, Math.Min(L.Height * 0.5f, 250)), 15);
            var rnd = new Random(seed);
            int n = (int)(L.Width * L.Height / 1900);
            using (var br = new SolidBrush(Color.White))
                for (int i = 0; i < n; i++)
                {
                    float x = L.X + (float)rnd.NextDouble() * L.Width, y = L.Y + (float)rnd.NextDouble() * L.Height;
                    double k = rnd.NextDouble();
                    float s = k < 0.12 ? 1.9f : k < 0.45 ? 1.25f : 0.85f;
                    int tint = rnd.Next(10);
                    Color c = tint == 0 ? Theme.Turq : tint == 1 ? Theme.Gold : tint == 2 ? Theme.Rgb(0xC9, 0xB8, 0xFF) : Theme.Rgb(0xD6, 0xDE, 0xFF);
                    br.Color = Theme.A(c, rnd.Next(22, s > 1.5f ? 120 : 80));
                    g.FillEllipse(br, x - s / 2, y - s / 2, s, s);
                }
            for (int i = 0; i < 6; i++)
            {
                var p = new PointF(L.X + (float)rnd.NextDouble() * L.Width, L.Y + (float)rnd.NextDouble() * L.Height);
                var c = i % 3 == 0 ? Theme.Gold : i % 3 == 1 ? Theme.Turq : Color.White;
                Gfx.Glow(g, p, 7, 7, c, 70);
                Sparkle(g, p, 3.6f + (float)rnd.NextDouble() * 2.4f, c, 150);
            }
        }

        // ------------------------------------------------------------------ cached bitmap layers

        sealed class Layer { public Bitmap Bmp; public string Key; }
        static readonly Dictionary<string, Layer> layers = new Dictionary<string, Layer>();

        /// <summary>Draws a bitmap cache of <paramref name="draw"/> covering logical rect L (re-rendered when version or device scale changes).</summary>
        public static void Cached(Graphics g, string name, string version, RectangleF L, Action<Graphics> draw)
        {
            float sc;
            using (var m = g.Transform) sc = Math.Abs(m.Elements[0]);
            if (sc < 0.25f) sc = 1;
            int pw = Math.Max(1, (int)Math.Round(L.Width * sc)), ph = Math.Max(1, (int)Math.Round(L.Height * sc));
            string key = version + "|" + pw + "x" + ph;
            if (!layers.TryGetValue(name, out var ly) || ly.Key != key)
            {
                ly?.Bmp?.Dispose();
                var bmp = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);
                using (var bg = Graphics.FromImage(bmp))
                {
                    Gfx.Hq(bg);
                    bg.Clear(Color.Transparent);
                    bg.ScaleTransform(pw / L.Width, ph / L.Height);
                    bg.TranslateTransform(-L.X, -L.Y);
                    try { draw(bg); } catch { }
                }
                ly = new Layer { Bmp = bmp, Key = key };
                layers[name] = ly;
            }
            var im = g.InterpolationMode; var po = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(ly.Bmp, new RectangleF(L.X, L.Y, L.Width, L.Height));
            g.InterpolationMode = im; g.PixelOffsetMode = po;
        }
    }

    struct Place
    {
        public float Lat, Lon;
        public string Name;
        public Place(float lat, float lon, string name) { Lat = lat; Lon = lon; Name = name; }
    }

    /// <summary>Rough server location from its host name (for the route map).</summary>
    static class Geo
    {
        public static readonly Place Iran = new Place(35.7f, 51.4f, "ایران");
        static readonly string[][] keys = {
            new[] { "-oh.", "-oh-", "ohio", "us-east-2" },
            new[] { "-va.", "-va-", "virginia", "us-east-1" },
            new[] { "-or.", "-or-", "oregon", "us-west-2" },
            new[] { "-ca.", "california", "us-west-1" },
            new[] { "-fra", "-fr.", "-de.", "frankfurt", "eu-central-1" },
            new[] { "-ams", "-nl.", "amsterdam" },
            new[] { "-lon", "-uk.", "london", "eu-west-2" },
            new[] { "-par", "paris", "eu-west-3" },
            new[] { "-sto", "-se.", "stockholm", "eu-north-1" },
            new[] { "-sg.", "-sin", "singapore", "ap-southeast-1" },
            new[] { "-tok", "-jp.", "tokyo", "ap-northeast-1" },
        };
        static readonly Place[] places = {
            new Place(40.0f, -83.0f, "اوهایو"), new Place(38.9f, -77.4f, "ویرجینیا"), new Place(45.6f, -121.2f, "اورگان"),
            new Place(37.4f, -122.0f, "کالیفرنیا"), new Place(50.1f, 8.7f, "فرانکفورت"), new Place(52.4f, 4.9f, "آمستردام"),
            new Place(51.5f, -0.1f, "لندن"), new Place(48.9f, 2.4f, "پاریس"), new Place(59.3f, 18.1f, "استکهلم"),
            new Place(1.35f, 103.8f, "سنگاپور"), new Place(35.7f, 139.7f, "توکیو"),
        };

        public static Place Find(string hint)
        {
            var h = (hint ?? "").ToLowerInvariant();
            for (int i = 0; i < keys.Length; i++)
                foreach (var k in keys[i]) if (h.Contains(k)) return places[i];
            return new Place(40.0f, -83.0f, "سرور");
        }
    }

    /// <summary>Natural Earth 110m land mask on a 2° grid (180×71, lat 84 → −58), 1 bit per cell, MSB first.</summary>
    static class WorldMap
    {
        public const int W = 180, H = 71;
        public const float Step = 2f, Lat0 = 84f;
        static byte[] bits;
        const string B64 =
            "AAAAAAAAf4AP/AAAAAAAAAAAAAAAAAAAAAAAAX/z///+AAAAAAAABAAAAAAAAAAAAAYd8P///wAA+AAAAAA8AAAAAAAAAAAwAnw////4AAIAAA" +
            "AAAGAAAAAAAAAAADivwAf//wAAAAADAAf/wAHYAAAAAADoi3sAP//gAAAAAMAD///sAAAAgBgACfwz/AD/+gAAAwAEHf///+/8gBAP/7/nJdjw" +
            "D/+AAAH/AA7f///////f4P//////h8H/gAAAf/6//f////////Mf/////9H4D8AeAA+ev///////////AP/////4A0B4AAAD5/////////////" +
            "Af3////gHgA4AAAH5///////////LwAHgH///gHkAAAAAH4/////////+CIAABAB///4D+AAAAGCx/////////4A8AAIAAf///n/gAAAOCD///" +
            "//////wA4AAAAAf///n/wAAAbP///////////AgAAAAAP/////wAAADf//////////9AAAAAAAF////0YAAAB///////////9AAAAAAAD////8" +
            "EAAAB///////////5AAAAAAAD////2AAAAB/f5fP//////wAAAAAAAD////gAAAAfxnwPP//////jAAAAAAAD////AAAAAPCb3/n/////+CAAA" +
            "AAAAD///8AAAAAfALf/n////+ECAAAAAAAB///8AAAAAGHQP/n/////mMAAAAAAAA///8AAAAAH+Ai///////E8AAAAAAAAf//wAAAAAP/AA//" +
            "/////BgAAAAAAAAP//gAAAAAf/73///////gAAAAAAAAAD/AQAAAAAf////f/////gAAAAAAAAAF+AQAAAAB///+/n/////AAAAAAAAAAC+AAA" +
            "AAAB///+f0H////AAAAAAAAAAAeAwAAAAD////f/B///8gAAAAAAAAAAeGEAAAAH////v+B/z/AAAAAAAAAAAAPMAgAAAD////n+A/B+gAAAAA" +
            "AAAAAAD8AAAAAD////n4AeB/AgAAAAAAAAAAAPAAAAAH////3gAcAfAgAAAAAAAAAAADAAAAAD////6AAcAfggAAAAAAAAAAABDwAAAD////8w" +
            "AMATAIAAAAAAAAAAAAr/AAAB/////gAKASAAAAAAAAAAAAAAH/gAAA/////gACAAAIAAAAAAAAAAAAH/8AAAaH///AAAAsGAAAAAAAAAAAAAH/" +
            "+AAAAB//+AAAAUOAAAAAAAAAAAAAP/+AAAAB//8AAAAYegAAAAAAAAAAAAP//gAAAD//4AAAAMeBgAAAAAAAAAAAP//8AAAB//wAAAAGdiuAAA" +
            "AAAAAAAAf///AAAA//wAAAACAQHgAAAAAAAAAAP///gAAA//wAAAABwAHwgAAAAAAAAAH///AAAA//wAAAAACIDQIAAAAAAAAAH//+AAAAf/wA" +
            "AAAAAAAAAAAAAAAAAAD//+AAAA//wgAAAAABxAAAAAAAAAAAD//+AAAA//wgAAAAAPxgBAAAAAAAAAA//8AAAA//jgAAAAAf5gAAAAAAAAAAAf" +
            "/8AAAA//DgAAAAAf/gAAAAAAAAAAAf/8AAAAf/DAAAAAD//4CAAAAAAAAAAf/wAAAAf/DAAAAAH//4AAAAAAAAAAAf/AAAAAf+CAAAAAH//8AA" +
            "AAAAAAAAAf/AAAAAP8AAAAAAH//+AAAAAAAAAAA/+AAAAAP8AAAAAAH//+AAAAAAAAAAA/+AAAAAH4AAAAAAD//+AAAAAAAAAAA/8AAAAAHwAA" +
            "AAAADwf8AAAAAAAAAAA/gAAAAAAAAAAAAACAH4AIAAAAAAAAB/wAAAAAAAAAAAAAAAD4AEAAAAAAAAB+AAAAAAAAAAAAAAAAAAAGAAAAAAAAB6" +
            "AAAAAAAAAAAAAAAAAwAMAAAAAAAAA8AAAAAAAAAAAAAAAAAQAYAAAAAAAAB4AAAAAAAAAAAAAAAAAAAwAAAAAAAAB4AAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAADwAAAAAAAAAACAAAAAAAAAAAAAAAAADgAAAAAAAAAAAAAAAAAAAAAAAAAAAABwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        public static void ForEachLand(Action<float, float> f)
        {
            if (bits == null) bits = Convert.FromBase64String(B64);
            for (int r = 0; r < H; r++)
                for (int c = 0; c < W; c++)
                {
                    int i = r * W + c;
                    if (((bits[i >> 3] >> (7 - (i & 7))) & 1) != 0)
                        f(Lat0 - (r + 0.5f) * Step, -180 + (c + 0.5f) * Step);
                }
        }
    }
}
