using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TT
{
    // =====================================================================================
    //  Widgets: tiny retained-mode UI drawn with GDI+ in logical (96-dpi) pixels.
    // =====================================================================================

    abstract class Widget
    {
        public RectangleF B;
        public bool Visible = true, Enabled = true;
        public bool Hover, Down;
        public float HoverT, DownT;
        public Action Click;
        public Cursor Cursor = Cursors.Hand;
        public float GlowPad = 6;            // extra invalidation margin for glows

        public virtual bool Hit(PointF p) => Visible && Enabled && B.Contains(p);
        public virtual void OnClick(PointF p) => Click?.Invoke();
        public abstract void Paint(Graphics g);

        /// <summary>Advances animations; returns true while still moving.</summary>
        public virtual bool Step()
        {
            bool a = Approach(ref HoverT, Hover && Enabled ? 1 : 0, 0.22f);
            bool b = Approach(ref DownT, Down ? 1 : 0, 0.45f);
            return a | b;
        }

        protected static bool Approach(ref float v, float target, float k)
        {
            if (Math.Abs(v - target) < 0.004f) { v = target; return false; }
            v += (target - v) * k;
            return true;
        }

        protected Color Fade(Color c) => Enabled ? c : Theme.A(c, c.A * 45 / 100);
    }

    enum PillStyle { Primary, Danger, Wait, Ghost }

    sealed class PillButton : Widget
    {
        public string Text;
        public PillStyle Style = PillStyle.Primary;
        public Color C1 = Theme.Off1, C2 = Theme.Off2;
        public float FontPx = 15, Radius = -1;
        public int Weight = 700;
        public Action<Graphics, RectangleF, Color> Icon;

        public PillButton() { GlowPad = 26; }

        public override void Paint(Graphics g)
        {
            var r = B;
            float k = DownT * 1.6f;
            r.Inflate(-k, -k * 0.6f);
            float rad = Radius < 0 ? r.Height / 2 : Radius;
            Color txt;
            using (var path = Gfx.Round(r, rad))
            {
                switch (Style)
                {
                    case PillStyle.Primary:
                        if (Enabled) Gfx.Glow(g, new PointF(r.X + r.Width / 2, r.Y + r.Height * 0.75f), r.Width * 0.62f, r.Height * 0.95f, C2, (int)(55 + 45 * HoverT));
                        bool brand = C1 == Theme.Off1 && C2 == Theme.Off2;
                        using (var b = brand
                            ? Art.Grad(r, 0f, Fade(Theme.Mix(Theme.Pink, Color.White, HoverT * 0.10f)), Fade(Theme.Mix(Theme.Violet, Color.White, HoverT * 0.12f)), Fade(Theme.Mix(Theme.Turq, Color.White, HoverT * 0.10f)))
                            : new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Fade(Theme.Mix(C1, Color.White, HoverT * 0.12f)), Fade(Theme.Mix(C2, Color.White, HoverT * 0.08f)), 0f))
                            g.FillPath(b, path);
                        if (brand && Enabled)
                            using (var b = Art.Grad(r, 0f, Theme.A(Theme.Gold, 0), Theme.A(Theme.Gold, (int)(150 + 80 * HoverT)), Theme.A(Theme.Gold, 0)))
                            using (var p = new Pen(b, 1.2f))
                                g.DrawPath(p, path);
                        using (var hl = Gfx.Round(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height * 0.55f), rad))
                        using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height * 0.55f + 1), Color.FromArgb(Enabled ? 46 : 20, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                            g.FillPath(b, hl);
                        txt = Fade(Color.White);
                        break;
                    case PillStyle.Danger:
                        using (var b = new SolidBrush(Fade(Theme.A(Theme.Danger, (int)(26 + 26 * HoverT))))) g.FillPath(b, path);
                        using (var p = new Pen(Fade(Theme.A(Theme.Danger, (int)(120 + 80 * HoverT))), 1.2f)) g.DrawPath(p, path);
                        txt = Fade(Theme.Mix(Theme.DangerSoft, Color.White, HoverT * 0.4f));
                        break;
                    case PillStyle.Wait:
                        using (var b = new SolidBrush(Fade(Theme.A(Theme.Wait1, (int)(22 + 24 * HoverT))))) g.FillPath(b, path);
                        using (var p = new Pen(Fade(Theme.A(Theme.Wait1, (int)(110 + 80 * HoverT))), 1.2f)) g.DrawPath(p, path);
                        txt = Fade(Theme.Mix(Theme.Wait1, Color.White, 0.25f + HoverT * 0.3f));
                        break;
                    default:
                        using (var b = new SolidBrush(Fade(Color.FromArgb((int)(12 + 14 * HoverT), 255, 255, 255)))) g.FillPath(b, path);
                        using (var p = new Pen(Fade(Color.FromArgb((int)(26 + 30 * HoverT), 255, 255, 255)), 1f)) g.DrawPath(p, path);
                        txt = Fade(Theme.Mix(Theme.Text, Color.White, HoverT));
                        break;
                }
            }
            var f = FontKit.Px(FontPx, Weight);
            var ts = Gfx.Measure(g, Text, f);
            float iw = Icon != null ? FontPx * 1.05f : 0, gap = Icon != null ? 8 : 0;
            float total = ts.Width + iw + gap;
            float x0 = r.X + (r.Width - total) / 2;
            if (Icon != null)
                Icon(g, new RectangleF(x0 + ts.Width + gap, r.Y + (r.Height - iw) / 2, iw, iw), txt);   // icon on the right of the text (RTL)
            Gfx.Text(g, Text, f, txt, new RectangleF(x0 - 2, r.Y, ts.Width + 4, r.Height + 1), Al.Center);
        }
    }

    sealed class IconButton : Widget
    {
        public Action<Graphics, RectangleF, Color> Glyph;
        public bool Danger;
        public float Inset = 9;

        public override void Paint(Graphics g)
        {
            var r = B;
            if (HoverT > 0.01f)
                Gfx.FillRound(g, r, r.Height * 0.32f, Danger ? Theme.A(Theme.Danger, (int)(200 * HoverT)) : Color.FromArgb((int)(22 * HoverT), 255, 255, 255));
            var c = Danger ? Theme.Mix(Theme.Muted, Color.White, HoverT) : Theme.Mix(Theme.Muted, Theme.Text, HoverT);
            var ir = RectangleF.Inflate(r, -Inset, -Inset);
            Glyph?.Invoke(g, ir, Fade(c));
        }
    }

    sealed class Toggle : Widget
    {
        public string Title, Desc;
        public bool On;
        public Func<bool, bool> Changed;     // return false to refuse the change
        float t;

        public Toggle() { Click = () => { bool nv = !On; if (Changed == null || Changed(nv)) On = nv; }; }
        public void SetSilently(bool v) { On = v; t = v ? 1 : 0; }

        public override bool Step() { bool a = base.Step(); bool b = Approach(ref t, On ? 1 : 0, 0.28f); return a | b; }

        public override void Paint(Graphics g)
        {
            if (HoverT > 0.01f) Gfx.FillRound(g, RectangleF.Inflate(B, -4, -3), 12, Color.FromArgb((int)(10 * HoverT), 255, 255, 255));
            var sw = new RectangleF(B.X + 16, B.Y + (B.Height - 24) / 2, 44, 24);
            using (var path = Gfx.Round(sw, 12))
            {
                using (var b = new SolidBrush(Fade(Theme.Track))) g.FillPath(b, path);
                if (t > 0.01f)
                    using (var b = new LinearGradientBrush(new RectangleF(sw.X - 1, sw.Y, sw.Width + 2, sw.Height), Fade(Theme.A(Theme.On2, (int)(255 * t))), Fade(Theme.A(Theme.On1, (int)(255 * t))), 0f))
                        g.FillPath(b, path);
                using (var p = new Pen(Fade(Color.FromArgb((int)(26 * (1 - t)), 255, 255, 255)), 1f)) g.DrawPath(p, path);
            }
            float kx = sw.X + 3 + (sw.Width - 24) * t;
            using (var b = new SolidBrush(Color.FromArgb(Enabled ? 70 : 30, 0, 0, 0))) g.FillEllipse(b, kx, sw.Y + 4, 18, 18);
            using (var b = new SolidBrush(Fade(Theme.Mix(Theme.Rgb(0xC9, 0xCF, 0xE4), Color.White, t)))) g.FillEllipse(b, kx, sw.Y + 3, 18, 18);

            var tr = new RectangleF(sw.Right + 12, B.Y, B.Right - sw.Right - 28, B.Height);
            if (string.IsNullOrEmpty(Desc))
                Gfx.Text(g, Title, FontKit.Px(14, 500), Fade(Theme.Text), tr);
            else
            {
                Gfx.Text(g, Title, FontKit.Px(14, 500), Fade(Theme.Text), new RectangleF(tr.X, B.Y + B.Height / 2 - 22, tr.Width, 24));
                Gfx.Text(g, Desc, FontKit.Px(11.5f), Fade(Theme.Muted), new RectangleF(tr.X, B.Y + B.Height / 2 + 1, tr.Width, 20));
            }
        }
    }

    sealed class Segmented : Widget
    {
        public string[] Items = new string[0];
        public string[] Subs;
        public int Selected;
        public Action<int> Changed;
        float pos = -1;

        RectangleF Seg(int i)
        {
            float w = B.Width / Items.Length;
            return new RectangleF(B.Right - (i + 1) * w, B.Y, w, B.Height);   // first item on the right (RTL)
        }

        public override void OnClick(PointF p)
        {
            for (int i = 0; i < Items.Length; i++)
                if (Seg(i).Contains(p) && i != Selected) { Selected = i; Changed?.Invoke(i); return; }
        }

        public override bool Step()
        {
            bool a = base.Step();
            if (pos < 0) { pos = Selected; return a; }
            return a | Approach(ref pos, Selected, 0.25f);
        }

        public override void Paint(Graphics g)
        {
            if (pos < 0) pos = Selected;
            using (var path = Gfx.Round(B, 14))
            {
                using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillPath(b, path);
                using (var p = new Pen(Theme.Stroke, 1f)) g.DrawPath(p, path);
            }
            float w = B.Width / Items.Length;
            var hi = new RectangleF(B.Right - (pos + 1) * w + 4, B.Y + 4, w - 8, B.Height - 8);
            using (var path = Gfx.Round(hi, 11))
            {
                using (var b = new LinearGradientBrush(new RectangleF(hi.X - 1, hi.Y, hi.Width + 2, hi.Height), Fade(Theme.A(Theme.Violet, 235)), Fade(Theme.A(Theme.Turq, 235)), 0f))
                    g.FillPath(b, path);
                using (var hl = Gfx.Round(new RectangleF(hi.X, hi.Y, hi.Width, hi.Height * 0.5f), 11))
                using (var b = new LinearGradientBrush(new RectangleF(hi.X, hi.Y - 1, hi.Width, hi.Height * 0.5f + 2), Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillPath(b, hl);
            }
            for (int i = 0; i < Items.Length; i++)
            {
                var r = Seg(i);
                float sel = 1 - Math.Min(1, Math.Abs(pos - i));
                var c = Fade(Theme.Mix(Theme.Muted, Color.White, sel));
                bool hasSub = Subs != null && i < Subs.Length && !string.IsNullOrEmpty(Subs[i]);
                if (!hasSub) Gfx.Text(g, Items[i], FontKit.Px(13.5f, 700), c, r, Al.Center, false);
                else
                {
                    Gfx.Text(g, Items[i], FontKit.Px(13.5f, 700), c, new RectangleF(r.X, r.Y + r.Height / 2 - 18, r.Width, 20), Al.Center, false);
                    Gfx.Text(g, Subs[i], FontKit.Px(10.5f), Fade(Theme.A(c, 200)), new RectangleF(r.X, r.Y + r.Height / 2 + 1, r.Width, 16), Al.Center, true);
                }
            }
        }
    }

    /// <summary>Anything else: a paint delegate plus optional click.</summary>
    sealed class Custom : Widget
    {
        public Action<Graphics, Custom> Draw;
        public bool Clickable;
        public override bool Hit(PointF p) => Clickable && base.Hit(p);
        public override void Paint(Graphics g) => Draw?.Invoke(g, this);
    }

    sealed class Page
    {
        public readonly List<Widget> W = new List<Widget>();
        public Action<Graphics> Under, Over;
        public T Add<T>(T w) where T : Widget { W.Add(w); return w; }
    }

    // =====================================================================================
    //  Canvas: one double-buffered control that paints pages and routes mouse input.
    // =====================================================================================

    sealed class Canvas : Control
    {
        public float S = 1f;
        public Page Chrome;
        public Action<Graphics, RectangleF> Background;
        public Func<bool> Animate;                   // per-frame hook; return true to keep the timer running
        public Func<PointF, bool> DragZone;
        public event Action<KeyEventArgs> Key;

        Page active, from;
        float navT = 1; int navDir;
        Widget hover, down;
        readonly Timer anim = new Timer { Interval = 15 };

        public Canvas()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            anim.Tick += (s, e) => Frame();
        }

        public Page Active => active;
        public bool Navigating => navT < 1;
        public RectangleF Logical => new RectangleF(0, 0, Width / S, Height / S);

        public void Show(Page p, int dir)
        {
            if (p == active) return;
            ClearHover();
            from = dir == 0 ? null : active;
            active = p;
            navDir = dir;
            navT = from == null ? 1 : 0;
            Invalidate();
            Kick();
        }

        void ClearHover()
        {
            if (hover != null) hover.Hover = false;
            if (down != null) down.Down = false;
            hover = down = null;
            Cursor = Cursors.Default;
        }

        public void Kick() { if (!anim.Enabled && IsHandleCreated) anim.Start(); }

        public void InvalidateLogical(RectangleF r)
        {
            r.Inflate(2, 2);
            Invalidate(Rectangle.FromLTRB((int)Math.Floor(r.Left * S), (int)Math.Floor(r.Top * S), (int)Math.Ceiling(r.Right * S), (int)Math.Ceiling(r.Bottom * S)));
        }

        IEnumerable<Widget> All()
        {
            if (Chrome != null) foreach (var w in Chrome.W) yield return w;
            if (active != null) foreach (var w in active.W) yield return w;
            if (from != null) foreach (var w in from.W) yield return w;
        }

        void Frame()
        {
            bool more = false;
            foreach (var w in All())
                if (w.Step()) { more = true; if (navT >= 1) InvalidateLogical(RectangleF.Inflate(w.B, w.GlowPad, w.GlowPad)); }
            if (navT < 1)
            {
                navT = Math.Min(1, navT + 0.07f);
                if (navT >= 1) from = null;
                Invalidate();
                more = true;
            }
            if (Animate != null && Animate()) more = true;
            if (!more) anim.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Hq(g);
            g.ScaleTransform(S, S);
            var L = Logical;
            Background?.Invoke(g, L);
            if (navT < 1 && from != null)
            {
                float k = Gfx.Ease(navT);
                PaintPage(g, from, navDir * k * L.Width);
                PaintPage(g, active, -navDir * (1 - k) * L.Width);
            }
            else PaintPage(g, active, 0);
            if (Chrome != null) PaintPage(g, Chrome, 0);
        }

        /// <summary>Offscreen render of the active page (used by the CI preview mode).</summary>
        public void RenderTo(Graphics g, float scale, RectangleF L)
        {
            Gfx.Hq(g);
            g.ScaleTransform(scale, scale);
            Background?.Invoke(g, L);
            PaintPage(g, active, 0);
            if (Chrome != null) PaintPage(g, Chrome, 0);
        }

        void PaintPage(Graphics g, Page p, float dx)
        {
            if (p == null) return;
            var st = g.Save();
            g.TranslateTransform(dx, 0);
            p.Under?.Invoke(g);
            foreach (var w in p.W) if (w.Visible) w.Paint(g);
            p.Over?.Invoke(g);
            g.Restore(st);
        }

        PointF L(MouseEventArgs e) => new PointF(e.X / S, e.Y / S);

        Widget HitTest(PointF p)
        {
            if (Chrome != null) for (int i = Chrome.W.Count - 1; i >= 0; i--) if (Chrome.W[i].Hit(p)) return Chrome.W[i];
            if (navT < 1 || active == null) return null;
            for (int i = active.W.Count - 1; i >= 0; i--) if (active.W[i].Hit(p)) return active.W[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitTest(L(e));
            if (h == hover) return;
            if (hover != null) hover.Hover = false;
            hover = h;
            if (h != null) h.Hover = true;
            Cursor = h?.Cursor ?? Cursors.Default;
            Kick();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover.Hover = false; hover = null; Kick(); }
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (!Focused) Focus();
            var p = L(e);
            var h = HitTest(p);
            if (h != null) { down = h; h.Down = true; Kick(); return; }
            if (DragZone != null && DragZone(p)) Native.Drag(FindForm());
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || down == null) return;
            var d = down; d.Down = false; down = null; Kick();
            var p = L(e);
            if (d.Hit(p)) d.OnClick(p);
        }

        protected override bool IsInputKey(Keys k) => k == Keys.Space || k == Keys.Enter || k == Keys.Escape || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); Key?.Invoke(e); }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
    }

    // =====================================================================================
    //  Borderless, shadowed, rounded window (Windows 11 corners; Windows 10 square + shadow)
    // =====================================================================================

    static class Native
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
        [DllImport("dwmapi.dll")] static extern int DwmIsCompositionEnabled(out bool enabled);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int L, R, T, B; }

        public static void Decorate(IntPtr h)
        {
            try
            {
                int v = 1;
                DwmSetWindowAttribute(h, 20, ref v, 4);                 // DWMWA_USE_IMMERSIVE_DARK_MODE
                v = 2; DwmSetWindowAttribute(h, 33, ref v, 4);          // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND (Win11)
                v = 0x003A2A24; DwmSetWindowAttribute(h, 34, ref v, 4); // DWMWA_BORDER_COLOR (BGR) - subtle indigo hairline
                if (DwmIsCompositionEnabled(out bool on) == 0 && on)
                {
                    v = 2; DwmSetWindowAttribute(h, 2, ref v, 4);       // DWMWA_NCRENDERING_POLICY = ENABLED -> drop shadow
                    var m = new Margins { L = 1, R = 1, T = 1, B = 1 };
                    DwmExtendFrameIntoClientArea(h, ref m);
                }
            }
            catch { }
        }

        public static void DarkScrollbars(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        public static void Drag(Form f)
        {
            if (f == null) return;
            ReleaseCapture();
            SendMessage(f.Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero);
        }

        public static float SystemScale()
        {
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); } catch { return 1f; }
        }
    }

    class GlassForm : Form
    {
        public readonly Canvas Canvas;
        public readonly float S;
        public readonly float LW, LH;

        protected GlassForm(float w, float h)
        {
            S = Native.SystemScale();
            LW = w; LH = h;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg1;
            ForeColor = Theme.Text;
            Font = FontKit.Pt(9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            Canvas = new Canvas { Dock = DockStyle.Fill, S = S };   // must exist before ClientSize raises OnResize
            Controls.Add(Canvas);
            ClientSize = new Size(Px(w), Px(h));
            Canvas.DragZone = p => p.Y < 54;
        }

        public int Px(float v) => (int)Math.Round(v * S);
        public Rectangle PxRect(RectangleF r) => new Rectangle(Px(r.X), Px(r.Y), Px(r.Width), Px(r.Height));

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000 /*WS_MINIMIZEBOX*/ | 0x00080000 /*WS_SYSMENU*/;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.Decorate(Handle);
        }

        /// <summary>Shared window backdrop: aurora sky with girih veil and stars (cached per size and DPI).</summary>
        public static void Backdrop(Graphics g, RectangleF L, int seed = 7)
        {
            Art.Cached(g, "sky" + seed + "_" + L.Width + "x" + L.Height, "v3", L, gg => Art.Sky(gg, L, seed));
        }
    }
}
