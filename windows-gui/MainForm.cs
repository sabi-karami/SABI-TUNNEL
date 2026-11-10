using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TT
{
    enum VState { NoConfig, Off, Connecting, On, Reconnecting, Stopping }

    sealed class MainForm : GlassForm
    {
        const float WW = 380, HH = 664;
        public const string GuiVersion = "2.1";
        public static readonly string[] Cmds = { "show", "toggle", "connect", "disconnect", "settings", "home", "log", "import", "bypass", "servers", "addlink", "quit" };

        readonly bool startInTray;
        readonly ClientRunner runner = new ClientRunner();
        VState state = VState.Off;
        volatile bool userStop;
        volatile string lastError;
        DateTime connectedAt, connectStart, lastProbe, lastPing;
        string exitIp;
        HashSet<string> serverIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int serverPort = 443;
        int probeBusy, probeCount, pingBusy, tickCount;
        bool loading, reallyExit, balloonShown, reconnectAfterStop;
        Action afterStop;

        // ---- widgets
        readonly Page main = new Page(), settings = new Page();
        PillButton btnMain, btnNewLink;
        Custom orb, tileIp, tilePing, tileUse, footer, chipW;
        Segmented seg;
        Toggle tKill, tAuto, tStart, tNotify;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem miHead, miToggle, miServers;
        readonly Dictionary<string, Icon> trayIcons = new Dictionary<string, Icon>();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 500 };
        readonly Queue<string> pending = new Queue<string>();
        readonly List<string> logLines = new List<string>();
        LogWindow logWin;

        // ---- visuals
        string profile = "TrustTunnel", host = "", user = "";
        int profileCount;
        string orbWord = "", orbHint = "", status = "";
        Color statusColor = Theme.Muted;
        Color c1 = Theme.Idle, c2 = Theme.Idle, f1 = Theme.Idle, f2 = Theme.Idle, t1 = Theme.Idle, t2 = Theme.Idle;
        float colorT = 1;
        bool spinning;
        readonly Stopwatch clock = Stopwatch.StartNew();
        long lastOrbFrame;
        string toast; long toastAt;

        // ---- live stats
        const int N = 60;
        readonly float[] sDown = new float[N], sUp = new float[N];
        int sCount;
        NetworkInterface nic;
        long rx0, tx0, rxLast, txLast;
        DateTime statAt;
        double rateRx, rateTx;
        long sessRx, sessTx;
        bool statsOk;
        int pingMs = -1;

        static readonly RectangleF OrbRect = new RectangleF(WW / 2 - 112, 122, 224, 224);
        static readonly RectangleF StatsRect = new RectangleF(20, 462, WW - 40, 104);
        static readonly RectangleF ToastRect = new RectangleF(WW / 2 - 130, HH - 98, 260, 38);
        static PointF OrbC => new PointF(OrbRect.X + OrbRect.Width / 2, OrbRect.Y + OrbRect.Height / 2);

        public MainForm(bool startHidden) : base(WW, HH)
        {
            startInTray = startHidden;
            Text = "TrustTunnel · Sabi VPN";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Canvas.Background = PaintBackground;
            Canvas.Animate = AnimateFrame;
            Canvas.Key += OnKey;
            Canvas.DragZone = p => p.Y < 56;
            Profiles.Migrate();
            Proto.Register();
            BuildChrome();
            BuildMain();
            BuildSettings();
            Canvas.Show(main, 0);

            menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, Font = FontKit.Pt(10f), Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = false };
            miHead = new ToolStripMenuItem("TrustTunnel") { Tag = "head" };
            miToggle = new ToolStripMenuItem("اتصال", null, (s, e) => ToggleConnect());
            menu.Items.Add(miHead);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miToggle);
            miServers = new ToolStripMenuItem("سرورها");
            var dd = (ToolStripDropDownMenu)miServers.DropDown;
            dd.Renderer = new DarkMenuRenderer(); dd.RightToLeft = RightToLeft.Yes; dd.ShowImageMargin = false; dd.ShowCheckMargin = false; dd.Font = menu.Font;
            menu.Items.Add(miServers);
            menu.Opening += (s, e) => FillServersMenu();
            menu.Items.Add(new ToolStripMenuItem("نمایش برنامه", null, (s, e) => ShowMain()));
            menu.Items.Add(new ToolStripMenuItem("گزارش اتصال", null, (s, e) => ShowLog()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("خروج", null, (s, e) => ExitApp()));
            foreach (ToolStripItem it in menu.Items) if (!(it is ToolStripSeparator)) it.Padding = new Padding(10, 5, 10, 5);
            tray = new NotifyIcon { Icon = TrayIcon("off"), Text = "TrustTunnel", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

            runner.Line += OnLine;
            runner.Exited += () => Post(OnExited);
            tick.Tick += (s, e) => OnTick();
            tick.Start();
            StartCommandListener();

            LoadConfigToUi();
            var once = new System.Windows.Forms.Timer { Interval = 400 };
            once.Tick += (s, e) => { once.Stop(); once.Dispose(); Startup(); };
            once.Start();
        }

        // =============================================================== layout

        void BuildChrome()
        {
            Canvas.Chrome = new Page { Over = PaintToast };
            Canvas.Chrome.Add(new IconButton { B = new RectangleF(12, 14, 32, 32), Danger = true, Inset = 11, Glyph = (g, r, c) => Glyph.Close(g, r, c), Click = () => Close() });
            Canvas.Chrome.Add(new IconButton { B = new RectangleF(48, 14, 32, 32), Inset = 10, Glyph = (g, r, c) => Glyph.Minimize(g, r, c), Click = () => WindowState = FormWindowState.Minimized });
        }

        void BuildMain()
        {
            main.Under = PaintMain;
            main.Add(new IconButton { B = new RectangleF(88, 14, 32, 32), Inset = 7, Glyph = (g, r, c) => Glyph.Gear(g, r, c), Click = GoSettings });
            chipW = main.Add(new Custom { B = new RectangleF(60, 88, WW - 120, 28), Clickable = true, GlowPad = 2, Click = ShowServers });
            orb = main.Add(new Custom { B = OrbRect, Clickable = true, Draw = PaintOrb, Click = ToggleConnect, GlowPad = 2 });
            btnMain = main.Add(new PillButton { B = new RectangleF(WW / 2 - 126, 390, 252, 54), FontPx = 16, Click = ToggleConnect });
            float tw = (WW - 40 - 16) / 3f, ty = 578;
            tileIp = main.Add(new Custom { B = new RectangleF(WW - 20 - tw, ty, tw, 56), Draw = PaintTileIp, Clickable = true, Click = CopyIp });
            tilePing = main.Add(new Custom { B = new RectangleF(20 + tw + 8, ty, tw, 56), Draw = PaintTilePing });
            tileUse = main.Add(new Custom { B = new RectangleF(20, ty, tw, 56), Draw = PaintTileUse });
            footer = main.Add(new Custom { B = new RectangleF(60, 640, WW - 120, 20), Draw = PaintFooter, Clickable = true, Click = GoSettings });
        }

        void BuildSettings()
        {
            settings.Under = PaintSettings;
            settings.Add(new IconButton { B = new RectangleF(WW - 14 - 36, 14, 36, 32), Inset = 9, Glyph = (g, r, c) => Glyph.Back(g, r, c), Click = GoMain });
            seg = settings.Add(new Segmented
            {
                B = new RectangleF(20, 86, WW - 40, 54),
                Items = new[] { "HTTP/2", "HTTP/3" },
                Subs = new[] { "TCP · پایدارتر در ایران", "QUIC · سریع‌تر (UDP)" },
                Changed = i => OnProtocolChanged()
            });
            float y = 156;
            tKill = settings.Add(new Toggle { B = new RectangleF(20, y, WW - 40, 60), Title = "Kill switch", Desc = "اگر VPN قطع شد، اینترنت هم قطع بماند (امن‌تر)", Changed = OnKillChanged });
            tAuto = settings.Add(new Toggle { B = new RectangleF(20, y + 60, WW - 40, 60), Title = "اتصال خودکار", Desc = "با باز شدن برنامه خودش وصل شود", Changed = v => { Ini.Set("autoconnect", v ? "1" : "0"); return true; } });
            tStart = settings.Add(new Toggle { B = new RectangleF(20, y + 120, WW - 40, 60), Title = "اجرا با ویندوز", Desc = "بعد از ورود به ویندوز، کنار ساعت باز شود", Changed = OnStartupChanged });
            tNotify = settings.Add(new Toggle { B = new RectangleF(20, y + 180, WW - 40, 60), Title = "اعلان‌ها", Desc = "پیام کنار ساعت هنگام وصل، قطع و اتصال مجدد", Changed = v => { Ini.Set("notify", v ? "1" : "0"); return true; } });
            btnNewLink = settings.Add(new PillButton
            {
                B = new RectangleF(32, 451, 116, 42), Style = PillStyle.Ghost, Text = "سرورها", FontPx = 13, Weight = 500,
                Icon = (g, r, c) => Glyph.Globe(g, r, c), Click = ShowServers
            });
            float bw = (WW - 40 - 16) / 3f, by = 552;
            settings.Add(new PillButton { B = new RectangleF(WW - 20 - bw, by, bw, 46), Style = PillStyle.Ghost, Radius = 14, Text = "سایت‌های مستقیم", FontPx = 12.5f, Weight = 500, Click = EditBypass });
            settings.Add(new PillButton { B = new RectangleF(20 + bw + 8, by, bw, 46), Style = PillStyle.Ghost, Radius = 14, Text = "گزارش اتصال", FontPx = 12.5f, Weight = 500, Click = ShowLog });
            settings.Add(new PillButton { B = new RectangleF(20, by, bw, 46), Style = PillStyle.Ghost, Radius = 14, Text = "پوشه برنامه", FontPx = 12.5f, Weight = 500, Click = Sys.OpenFolder });
        }

        void GoSettings() { if (Canvas.Active != settings) Canvas.Show(settings, +1); }
        void GoMain() { if (Canvas.Active != main) Canvas.Show(main, -1); }

        // =============================================================== painting

        int AmbientAlpha()
        {
            switch (state)
            {
                case VState.On: return 74;
                case VState.Connecting: case VState.Reconnecting: return 58;
                case VState.Stopping: return 30;
                default: return 40;
            }
        }

        void PaintBackground(Graphics g, RectangleF L)
        {
            Backdrop(g, L);
            Gfx.Glow(g, OrbC, 230, 210, c2, AmbientAlpha());
            Gfx.Glow(g, new PointF(OrbC.X, OrbC.Y + 40), 160, 120, c1, AmbientAlpha() / 3);
            Gfx.Glow(g, new PointF(30, 20), 200, 130, Theme.Off2, 16);
        }

        void PaintMain(Graphics g)
        {
            // brand
            Glyph.Brand(g, new RectangleF(WW - 20 - 28, 16, 28, 28), Theme.Off1, Theme.Off2);
            Gfx.Text(g, "TrustTunnel", FontKit.Px(15.5f, 700), Theme.Text, new RectangleF(WW - 20 - 28 - 10 - 160, 14, 160, 32), Al.Right, false);

            // profile + host chip
            Gfx.Text(g, profile, FontKit.Px(19, 700), Theme.Text, new RectangleF(20, 56, WW - 40, 30), Al.Center);
            var chip = string.IsNullOrEmpty(host) ? "هنوز سروری وارد نشده" : host + (string.IsNullOrEmpty(user) ? "" : "  ·  " + user);
            bool latin = !string.IsNullOrEmpty(host);
            var cf = FontKit.Px(11.5f, 500);
            float tw = Math.Min(WW - 80, Gfx.Measure(g, chip, cf, 10000, !latin).Width);
            var cr = new RectangleF((WW - tw - 34) / 2, 90, tw + 34, 24);
            Gfx.FillRound(g, cr, 12, Color.FromArgb(12, 255, 255, 255));
            Gfx.StrokeRound(g, cr, 12, Theme.Mix(Theme.Stroke, Theme.A(Theme.Off1, 150), chipW.HoverT));
            Gfx.Glow(g, new PointF(cr.X + 15, cr.Y + 12), 9, 9, c1, 120);
            using (var b = new SolidBrush(state == VState.On ? c1 : Theme.Mix(c1, Theme.Muted, 0.4f))) g.FillEllipse(b, cr.X + 12, cr.Y + 9, 6, 6);
            Gfx.Text(g, chip, cf, Theme.Muted, new RectangleF(cr.X + 24, cr.Y, tw + 4, cr.Height), Al.Left, !latin);

            // status line
            Gfx.Text(g, status, FontKit.Px(13, 500), statusColor, new RectangleF(20, 350, WW - 40, 26), Al.Center);

            // live stats card
            var r = StatsRect;
            Gfx.Card(g, r, 18);
            bool live = state == VState.On && statsOk;
            float half = r.Width / 2;
            StatCol(g, new RectangleF(r.X + half, r.Y, half, 60), "دانلود", live ? Gfx.Bytes(rateRx, true) : "—", Theme.On1, true);
            StatCol(g, new RectangleF(r.X, r.Y, half, 60), "آپلود", live ? Gfx.Bytes(rateTx, true) : "—", Theme.Off1, false);
            using (var p = new Pen(Theme.Stroke, 1f)) g.DrawLine(p, r.X + half, r.Y + 16, r.X + half, r.Y + 50);
            Spark(g, new RectangleF(r.X + 14, r.Y + 62, r.Width - 28, 32), live);
        }

        void StatCol(Graphics g, RectangleF col, string label, string value, Color c, bool down)
        {
            var ic = new RectangleF(col.Right - 16 - 32, col.Y + 15, 32, 32);
            using (var b = new SolidBrush(Theme.A(c, 30))) g.FillEllipse(b, ic);
            Glyph.Arrow(g, RectangleF.Inflate(ic, -10, -9), c, down, 2f);
            var tr = new RectangleF(col.X + 12, col.Y, ic.X - 10 - (col.X + 12), 0);
            Gfx.Text(g, label, FontKit.Px(11.5f), Theme.Muted, new RectangleF(tr.X, col.Y + 11, tr.Width, 18));
            Gfx.Text(g, value, FontKit.Px(16, 700), Theme.Text, new RectangleF(tr.X, col.Y + 28, tr.Width, 24), Al.Right, false);
        }

        void Spark(Graphics g, RectangleF a, bool live)
        {
            using (var p = new Pen(Color.FromArgb(18, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                g.DrawLine(p, a.X, a.Bottom - 0.5f, a.Right, a.Bottom - 0.5f);
            if (!live || sCount < 2)
            {
                if (state != VState.On)
                    Gfx.Text(g, "بعد از اتصال، نمودار زنده‌ی سرعت این‌جاست", FontKit.Px(11f), Theme.Dim, new RectangleF(a.X, a.Y + 2, a.Width, a.Height - 6), Al.Center);
                return;
            }
            float max = 24 * 1024;
            for (int i = 0; i < sCount; i++) max = Math.Max(max, Math.Max(sDown[N - sCount + i], sUp[N - sCount + i]));
            max *= 1.15f;
            PointF[] Pts(float[] s)
            {
                var pts = new PointF[sCount];
                for (int i = 0; i < sCount; i++)
                {
                    float x = a.Right - (sCount - 1 - i) * (a.Width / (N - 1));
                    pts[i] = new PointF(x, a.Bottom - 1 - s[N - sCount + i] / max * (a.Height - 3));
                }
                return pts;
            }
            var d = Pts(sDown);
            using (var path = new GraphicsPath())
            {
                path.AddLine(d[0].X, a.Bottom, d[0].X, d[0].Y);
                path.AddCurve(d, 0.4f);
                path.AddLine(d[d.Length - 1].X, d[d.Length - 1].Y, d[d.Length - 1].X, a.Bottom);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new RectangleF(a.X, a.Y - 1, a.Width, a.Height + 2), Theme.A(Theme.On1, 80), Theme.A(Theme.On2, 0), 90f))
                    g.FillPath(b, path);
            }
            using (var p = new Pen(Theme.On1, 1.6f) { LineJoin = LineJoin.Round }) g.DrawCurve(p, d, 0.4f);
            using (var p = new Pen(Theme.A(Theme.Off1, 210), 1.3f) { LineJoin = LineJoin.Round }) g.DrawCurve(p, Pts(sUp), 0.4f);
            var last = d[d.Length - 1];
            Gfx.Glow(g, last, 7, 7, Theme.On1, 140);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, last.X - 2.2f, last.Y - 2.2f, 4.4f, 4.4f);
        }

        void Tile(Graphics g, Custom w, string label, string value, Color vc, bool latin, Action<Graphics, RectangleF, Color> icon = null)
        {
            Gfx.Card(g, w.B, 14, (int)(10 + 8 * w.HoverT));
            var tr = RectangleF.Inflate(w.B, -12, 0);
            Gfx.Text(g, label, FontKit.Px(11f), Theme.Muted, new RectangleF(tr.X, w.B.Y + 8, tr.Width, 18));
            // shrink-to-fit so a full IPv4 address (or "123.4 MB") never gets an ellipsis
            var vr = new RectangleF(w.B.X + 4, w.B.Y + 26, w.B.Width - 16, 24);   // right edge aligned with the label
            float px = 14f;
            var vf = FontKit.Px(px, 700);
            while (px > 10f && Gfx.Measure(g, value, vf, 10000, !latin).Width > vr.Width - 2) { px -= 0.5f; vf = FontKit.Px(px, 700); }
            Gfx.Text(g, value, vf, vc, vr, Al.Right, !latin);
            if (icon != null && w.HoverT > 0.02f) icon(g, new RectangleF(w.B.X + 10, w.B.Y + 10, 12, 12), Theme.A(Theme.Muted, (int)(255 * w.HoverT)));
        }

        void PaintTileIp(Graphics g, Custom w)
        {
            bool via = exitIp != null && serverIps.Contains(exitIp);
            string v = state == VState.On ? (exitIp ?? "…") : "—";
            Tile(g, w, via ? "IP خروجی  ✓" : "IP خروجی", v, via ? Theme.On1 : Theme.Text, true, (gg, r, c) => Glyph.Copy(gg, r, c, 1.2f));
        }

        void PaintTilePing(Graphics g, Custom w)
        {
            bool live = pingMs >= 0 && (state == VState.On || state == VState.Reconnecting);
            string v = live ? pingMs + " ms" : "—";
            var c = !live ? Theme.Text : pingMs < 150 ? Theme.On1 : pingMs < 300 ? Theme.Wait1 : Theme.Danger;
            Tile(g, w, "پینگ سرور", v, c, true);
        }

        void PaintTileUse(Graphics g, Custom w)
        {
            string v = state == VState.On && statsOk ? Gfx.Bytes(sessRx + sessTx) : "—";
            Tile(g, w, "مصرف این اتصال", v, Theme.Text, true);
        }

        void PaintFooter(Graphics g, Custom w)
        {
            string proto = seg.Selected == 1 ? "HTTP/3" : "HTTP/2";
            string ks = tKill.On ? "روشن" : "خاموش";
            var s = "پروتکل " + proto + "\u200F  ·  \u200FKill switch " + ks;
            Gfx.Text(g, s, FontKit.Px(11.5f), Theme.Mix(Theme.Dim, Theme.Text, w.HoverT * 0.8f), w.B, Al.Center);
        }

        void PaintSettings(Graphics g)
        {
            Gfx.Text(g, "تنظیمات", FontKit.Px(18, 700), Theme.Text, new RectangleF(130, 14, WW - 14 - 36 - 6 - 130, 32));
            Section(g, "اتصال", 60);
            var tc = new RectangleF(20, 156, WW - 40, 240);
            Gfx.Card(g, tc, 18);
            using (var p = new Pen(Theme.Stroke, 1f)) for (int i = 1; i < 4; i++) g.DrawLine(p, tc.X + 16, tc.Y + 60 * i, tc.Right - 16, tc.Y + 60 * i);

            int count = profileCount;
            Section(g, count > 1 ? "سرور  ·  " + Gfx.Fa(count.ToString()) + " سرور ذخیره‌شده" : "سرور", 408);
            var sc = new RectangleF(20, 432, WW - 40, 80);
            Gfx.Card(g, sc, 18);
            var tx = new RectangleF(sc.X + 140, sc.Y, sc.Width - 140 - 16, 0);
            Gfx.Text(g, profile, FontKit.Px(14, 700), Theme.Text, new RectangleF(tx.X, sc.Y + 10, tx.Width, 22));
            Gfx.Text(g, string.IsNullOrEmpty(host) ? "—" : host, FontKit.Px(11.5f), Theme.Muted, new RectangleF(tx.X, sc.Y + 33, tx.Width, 18), Al.Right, false);
            Gfx.Text(g, string.IsNullOrEmpty(user) ? "" : "کاربر: " + user, FontKit.Px(11.5f), Theme.Muted, new RectangleF(tx.X, sc.Y + 52, tx.Width, 18));

            Section(g, "ابزارها", 526);
            Gfx.Text(g, "TrustTunnel client 1.1.11  ·  GUI " + GuiVersion + "  ·  Vazirmatn  ·  sabi-karami", FontKit.Px(10.5f), Theme.Dim,
                     new RectangleF(20, HH - 34, WW - 40, 18), Al.Center, false);
        }

        static void Section(Graphics g, string text, float y)
        {
            Gfx.Text(g, text, FontKit.Px(12, 700), Theme.Muted, new RectangleF(24, y, WW - 48, 22));
        }

        void PaintOrb(Graphics g, Custom w)
        {
            var c = OrbC;
            const float R = 94;
            double t = clock.ElapsedMilliseconds / 1000.0;
            float hv = w.HoverT, dn = w.DownT;
            var st = g.Save();
            float s = 1 - dn * 0.025f + hv * 0.008f;
            g.TranslateTransform(c.X, c.Y); g.ScaleTransform(s, s); g.TranslateTransform(-c.X, -c.Y);

            var ring = new RectangleF(c.X - R, c.Y - R, R * 2, R * 2);
            bool on = state == VState.On;
            if (on)
                foreach (var k in new[] { new { w = 26f, a = 10 }, new { w = 16f, a = 16 }, new { w = 10f, a = 26 } })
                    using (var p = new Pen(Theme.A(c2, k.a), k.w)) g.DrawEllipse(p, ring);
            using (var p = new Pen(Theme.Track, 8)) g.DrawEllipse(p, ring);

            if (on)
            {
                using (var b = new LinearGradientBrush(RectangleF.Inflate(ring, 6, 6), c1, c2, 45f))
                using (var p = new Pen(b, 8)) g.DrawEllipse(p, ring);
            }
            else if (spinning)
            {
                float head = (float)(t * 320 % 360);
                const int segs = 36; const float tail = 170;
                for (int j = 0; j < segs; j++)
                {
                    float k = (j + 1) / (float)segs;
                    using (var p = new Pen(Theme.A(Theme.Mix(c2, c1, k), (int)(255 * k * k)), 8))
                        g.DrawArc(p, ring, head - tail + j * (tail / segs), tail / segs + 0.8f);
                }
                using (var b = new SolidBrush(c1))
                {
                    double a = head * Math.PI / 180;
                    g.FillEllipse(b, c.X + (float)Math.Cos(a) * R - 4, c.Y + (float)Math.Sin(a) * R - 4, 8, 8);
                }
                Gfx.Glow(g, new PointF(c.X + (float)Math.Cos(head * Math.PI / 180) * R, c.Y + (float)Math.Sin(head * Math.PI / 180) * R), 16, 16, c1, 120);
            }
            else
            {
                int a = (int)(70 + 140 * hv);
                using (var b = new LinearGradientBrush(RectangleF.Inflate(ring, 6, 6), Theme.A(Theme.Off1, a), Theme.A(Theme.Off2, a), 45f))
                using (var p = new Pen(b, 8)) g.DrawEllipse(p, ring);
            }

            // core disc
            float Rc = R - 15;
            var core = new RectangleF(c.X - Rc, c.Y - Rc, Rc * 2, Rc * 2);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(core);
                using (var b = new PathGradientBrush(path))
                {
                    b.CenterPoint = new PointF(c.X, c.Y - Rc * 0.35f);
                    b.CenterColor = Theme.Mix(Theme.Rgb(0x1C, 0x25, 0x46), c1, 0.07f + hv * 0.05f);
                    b.SurroundColors = new[] { Theme.Rgb(0x0A, 0x0E, 0x1E) };
                    g.FillPath(b, path);
                }
                // tunnel depth rings, flowing inwards while connected / connecting
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                const int n = 7;
                for (int i = 0; i < n; i++)
                {
                    float f;
                    if (on) f = (float)((i / (double)n + t / 3.4) % 1.0);
                    else if (spinning) f = (float)((i / (double)n + t / 1.3) % 1.0);
                    else f = i / (float)n;
                    float rr = (Rc - 4) * (1 - f * 0.88f);
                    int a = on || spinning ? (int)(Math.Sin(f * Math.PI) * (on ? 95 : 70)) : (int)(22 * (1 - f) + 6 * hv);
                    using (var p = new Pen(Theme.A(Theme.Mix(c1, c2, f), a), 1.3f))
                        g.DrawEllipse(p, c.X - rr, c.Y - rr * 0.94f + f * 6, rr * 2, rr * 2 * 0.94f);
                }
                g.Clip = clip;
                using (var p = new Pen(Color.FromArgb(16 + (int)(20 * hv), 255, 255, 255), 1f)) g.DrawEllipse(p, core);
                using (var p = new Pen(Color.FromArgb(34, 255, 255, 255), 1.2f)) g.DrawArc(p, RectangleF.Inflate(core, -3, -3), 205, 130);
            }

            // center content
            var pw = new RectangleF(c.X - 15, c.Y - 50, 30, 30);
            Color ic = on || spinning ? c1 : state == VState.NoConfig ? Theme.Muted : Theme.Mix(Theme.Muted, Color.White, hv);
            if (on || spinning) Gfx.Glow(g, new PointF(pw.X + 15, pw.Y + 15), 30, 30, c1, 70);
            if (state == VState.NoConfig) Glyph.Link(g, pw, ic, 2.4f); else Glyph.Power(g, pw, ic, 2.6f);
            Gfx.Text(g, orbWord, FontKit.Px(24, 900), Theme.Text, new RectangleF(c.X - 84, c.Y - 14, 168, 38), Al.Center);
            Gfx.Text(g, orbHint, FontKit.Px(11.5f), Theme.Muted, new RectangleF(c.X - 74, c.Y + 24, 148, 20), Al.Center);
            g.Restore(st);
        }

        void PaintToast(Graphics g)
        {
            if (toast == null) return;
            long age = clock.ElapsedMilliseconds - toastAt;
            float a = age < 180 ? age / 180f : age > 1900 ? Math.Max(0, 1 - (age - 1900) / 300f) : 1;
            var r = ToastRect; r.Offset(0, (1 - a) * 8);
            Gfx.FillRound(g, r, r.Height / 2, Theme.A(Theme.Rgb(0x1E, 0x26, 0x44), (int)(245 * a)));
            Gfx.StrokeRound(g, r, r.Height / 2, Color.FromArgb((int)(50 * a), 255, 255, 255));
            Gfx.Text(g, toast, FontKit.Px(13, 500), Theme.A(Theme.Text, (int)(255 * a)), r, Al.Center);
        }

        void Toast(string text) { toast = text; toastAt = clock.ElapsedMilliseconds; Canvas.Kick(); }

        bool AnimateFrame()
        {
            if (!Visible || WindowState == FormWindowState.Minimized) return false;
            bool more = false;
            long ms = clock.ElapsedMilliseconds;
            if (colorT < 1)
            {
                colorT = Math.Min(1, colorT + 0.06f);
                float k = Gfx.Ease(colorT);
                c1 = Theme.Mix(f1, t1, k); c2 = Theme.Mix(f2, t2, k);
                Canvas.Invalidate();
                more = true;
            }
            if ((state == VState.On || spinning) && Canvas.Active == main && !Canvas.Navigating)
            {
                more = true;
                if (ms - lastOrbFrame >= 33) { lastOrbFrame = ms; Canvas.InvalidateLogical(RectangleF.Inflate(OrbRect, 4, 4)); }
            }
            if (toast != null)
            {
                more = true;
                Canvas.InvalidateLogical(RectangleF.Inflate(ToastRect, 2, 12));
                if (ms - toastAt > 2250) toast = null;
            }
            return more;
        }

        // =============================================================== lifecycle

        protected override void SetVisibleCore(bool value)
        {
            if (startInTray && !IsHandleCreated) { CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Canvas.Focus(); Canvas.Kick(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) Canvas?.Kick(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (Canvas != null && WindowState != FormWindowState.Minimized) { Canvas.Invalidate(); Canvas.Kick(); } }

        void OnKey(KeyEventArgs e)
        {
            if (Canvas.Active == main && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) { ToggleConnect(); e.Handled = true; }
            else if (Canvas.Active == settings && (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back)) { GoMain(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.L) ShowLog();
            else if (e.Control && e.KeyCode == Keys.Oemcomma) GoSettings();
        }

        void StartCommandListener()
        {
            var handles = new List<EventWaitHandle>();
            foreach (var c in Cmds) handles.Add(new EventWaitHandle(false, EventResetMode.AutoReset, Program.EventName(c)));
            var arr = handles.ToArray();
            new Thread(() => { while (true) { int i = WaitHandle.WaitAny(arr); var c = Cmds[i]; Post(() => RunCommand(c)); } }) { IsBackground = true, Name = "commands" }.Start();
        }

        /// <summary>If a modal dialog is open, bring it forward and report true (nothing else can be used meanwhile).</summary>
        static bool ModalUp()
        {
            var md = DarkDialog.ActiveModal;
            if (md == null || md.IsDisposed || !md.Visible) return false;
            try { md.Activate(); } catch { }
            return true;
        }

        void RunCommand(string c)
        {
            if (c != "toggle" && c != "connect" && c != "disconnect" && c != "quit" && ModalUp()) { if (c == "addlink") Toast("اول پنجره‌ی باز را ببندید و دوباره لینک را باز کنید"); return; }
            switch (c)
            {
                case "show": ShowMain(); break;
                case "toggle": ToggleConnect(); break;
                case "connect": if (state == VState.Off) Connect(); break;
                case "disconnect": if (runner.Running) Disconnect(); break;
                case "settings": ShowMain(); GoSettings(); break;
                case "home": ShowMain(); GoMain(); break;
                case "log": ShowLog(); break;
                case "import": ImportFlow(); break;
                case "bypass": ShowMain(); EditBypass(); break;
                case "servers": ShowServers(); break;
                case "addlink": { var l = Proto.TakePending(); if (l != null) ImportFlow(l); } break;
                case "quit": ExitApp(); break;
            }
        }

        void Startup()
        {
            if (!File.Exists(Cfg.Client) || !File.Exists(Cfg.Wintun) || !File.Exists(Cfg.Wizard))
            {
                ShowMain();
                Ui.Error(this, "فایل‌های trusttunnel_client.exe، setup_wizard.exe و wintun.dll باید کنار همین برنامه باشند.\nکل پوشه‌ی zip را با هم باز کنید.");
                return;
            }
            var others = Process.GetProcessesByName("trusttunnel_client");
            if (others.Length > 0)
            {
                ShowMain();
                if (Ui.Ask(this, "یک اتصال TrustTunnel دیگر از قبل در حال اجراست (مثلاً از Connect.bat).\nمتوقفش کنم تا این برنامه کنترل را بگیرد؟"))
                {
                    Cursor = Cursors.WaitCursor;
                    foreach (var p in others) { try { ClientRunner.StopProcess(p, 6000); } catch { } }
                    Cursor = Cursors.Default;
                }
                else return;
            }
            var link = Proto.TakePending();
            if (link != null) { ImportFlow(link); return; }
            if (!Cfg.Exists) { ShowMain(); ImportFlow(); return; }
            if (Ini.Get("autoconnect") == "1") Connect();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                userStop = true;
                try { runner.Stop(4000); } catch { }
                tray.Visible = false;
                base.OnFormClosing(e);
                return;
            }
            if (!reallyExit)
            {
                e.Cancel = true;
                Hide();
                if (!balloonShown)
                {
                    tray.ShowBalloonTip(4000, "TrustTunnel", "برنامه کنار ساعت ویندوز فعال ماند. برای خروج روی آیکون راست‌کلیک کنید.", ToolTipIcon.Info);
                    balloonShown = true;
                }
                return;
            }
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        void ShowMain()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            Canvas.Kick();
        }

        void ExitApp()
        {
            if (reallyExit) return;
            reallyExit = true;
            Disconnect(() => { tray.Visible = false; Application.Exit(); });
        }

        void Post(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        Icon TrayIcon(string kind)
        {
            if (trayIcons.TryGetValue(kind, out var ic)) return ic;
            switch (kind)
            {
                case "on": ic = Icons.Make(Theme.On1, Theme.On2); break;
                case "wait": ic = Icons.Make(Theme.Wait1, Theme.Wait2); break;
                default: ic = Icons.Make(Theme.Rgb(0x6A, 0x74, 0x98), Theme.Rgb(0x45, 0x4E, 0x6E)); break;
            }
            trayIcons[kind] = ic;
            return ic;
        }

        // =============================================================== config <-> UI

        void LoadConfigToUi()
        {
            loading = true;
            try
            {
                bool has = Cfg.Exists;
                var name = Ini.Get("name", "");
                if (string.IsNullOrWhiteSpace(name) && has) name = Cfg.Get("name");
                user = has ? (Cfg.Get("username") ?? "") : "";
                host = has ? (Cfg.Get("hostname") ?? "") : "";
                profile = string.IsNullOrWhiteSpace(name) ? "TrustTunnel" : name.Trim();
                serverIps = has ? Cfg.ServerIps() : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                serverPort = has ? Cfg.ServerPort() : 443;
                seg.Selected = has && (Cfg.Get("upstream_protocol") ?? "").Contains("3") ? 1 : 0;
                tKill.SetSilently(!has || (Cfg.Get("killswitch_enabled") ?? "true") != "false");
                tAuto.SetSilently(Ini.Get("autoconnect") == "1");
                tStart.SetSilently(AutoStart.Enabled());
                tNotify.SetSilently(Ini.Get("notify", "1") != "0");
                profileCount = Profiles.List().Count;
                seg.Enabled = tKill.Enabled = has;
                if (state == VState.Off || state == VState.NoConfig) SetState(has ? VState.Off : VState.NoConfig);
                Canvas.Invalidate();
            }
            finally { loading = false; }
        }

        void OnProtocolChanged()
        {
            if (loading || !Cfg.Exists) return;
            Cfg.Set("upstream_protocol", seg.Selected == 1 ? "\"http3\"" : "\"http2\"");
            Canvas.Invalidate();
            OfferReconnect();
        }

        bool OnKillChanged(bool v)
        {
            if (!Cfg.Exists) return false;
            Cfg.Set("killswitch_enabled", v ? "true" : "false");
            tKill.On = v;
            OfferReconnect();
            return true;
        }

        void OfferReconnect()
        {
            if (state == VState.On || state == VState.Connecting || state == VState.Reconnecting)
            {
                reconnectAfterStop = true;
                Toast("در حال اعمال تغییرات…");
                Disconnect();
            }
        }

        bool OnStartupChanged(bool v)
        {
            if (AutoStart.Set(v) && AutoStart.Enabled() == v) { Toast(v ? "با ویندوز اجرا می‌شود" : "اجرای خودکار خاموش شد"); return true; }
            Ui.Error(this, "تنظیم اجرای خودکار ممکن نشد.");
            return false;
        }

        bool ImportFlow(string prefill = null)
        {
            if (ModalUp()) return false;
            ShowMain();
            string link, err;
            using (var d = new ImportDialog(prefill))
            {
                if (d.ShowFor(this) != DialogResult.OK) return false;
                link = DeepLink.Normalize(d.Link);
            }
            Cursor = Cursors.WaitCursor;
            try { err = Cfg.ImportLink(link); } finally { Cursor = Cursors.Default; }
            if (err != null) { Ui.Error(this, "وارد کردن لینک ناموفق بود:\n\n" + err); return false; }
            Profile p;
            try { p = Profiles.Add(Cfg.NewToml, DeepLink.Name(link)); }
            catch (Exception ex) { Ui.Error(this, "ذخیره‌ی سرور ممکن نشد:\n" + ex.Message); return false; }
            finally { try { File.Delete(Cfg.NewToml); } catch { } }
            bool replaced = p.Id == Profiles.ActiveId;
            if (state == VState.Off || state == VState.NoConfig)
            {
                try { Profiles.Activate(p, sync: !replaced); Cfg.ApplyBypass(); }
                catch (Exception ex) { Ui.Error(this, ex.Message); return false; }
                LoadConfigToUi();
                GoMain();
                Ui.Success(this, "سرور «" + p.Name + "» " + (replaced ? "به‌روز شد." : "اضافه شد.") + "\nحالا دکمه‌ی «اتصال» را بزنید.");
                return true;
            }
            LoadConfigToUi();
            if (Ui.Ask(this, "سرور «" + p.Name + "» " + (replaced ? "به‌روز شد." : "به فهرست سرورها اضافه شد.") + "\nهمین حالا به آن وصل شوم؟"))
                SwitchTo(p, sync: !replaced, force: replaced);
            return true;
        }

        // =============================================================== saved servers

        void ShowServers()
        {
            if (ModalUp()) return;
            ShowMain();
            while (true)
            {
                var list = Profiles.List();
                string act, id;
                using (var d = new ServersDialog(list, Profiles.ActiveId, state != VState.Stopping))
                {
                    if (d.ShowFor(this) != DialogResult.OK) return;
                    act = d.Action; id = d.Id;
                }
                if (act == "add") { ImportFlow(); return; }
                var p = list.FirstOrDefault(x => x.Id == id);
                if (p == null) return;
                if (act == "select") { SwitchTo(p); return; }
                if (act == "delete" && !DeleteProfile(p)) return;
            }
        }

        /// <summary>Makes p the live server and connects to it (reconnecting if a tunnel is up).</summary>
        void SwitchTo(Profile p, bool sync = true, bool force = false)
        {
            if (state == VState.Stopping || !File.Exists(p.Toml)) return;
            bool same = p.Id == Profiles.ActiveId && !force;
            if (!runner.Running)
            {
                if (!same)
                {
                    try { Profiles.Activate(p, sync); } catch (Exception ex) { Ui.Error(this, ex.Message); return; }
                    LoadConfigToUi();
                }
                GoMain();
                Connect();
                return;
            }
            if (same) { GoMain(); return; }
            GoMain();
            Toast("رفتن به " + p.Name + "…");
            reconnectAfterStop = true;
            Disconnect(() =>
            {
                try { Profiles.Activate(p, sync); } catch (Exception ex) { reconnectAfterStop = false; AddLog("[gui] switch: " + ex.Message); }
                LoadConfigToUi();
            });
        }

        /// <summary>Returns true when finished right away (false if it waits for a disconnect).</summary>
        bool DeleteProfile(Profile p)
        {
            bool active = p.Id == Profiles.ActiveId;
            if (!Ui.Ask(this, "سرور «" + p.Name + "» از فهرست حذف شود؟" + (active && runner.Running ? "\nاتصال فعلی قطع می‌شود." : ""))) return true;
            Action del = () =>
            {
                Profiles.Delete(p);
                if (active)
                {
                    var rest = Profiles.List();
                    try
                    {
                        if (rest.Count > 0) Profiles.Activate(rest[0], sync: false);
                        else { File.Delete(Cfg.Toml); Ini.Set("name", ""); }
                    }
                    catch { }
                }
                LoadConfigToUi();
                Toast("سرور حذف شد");
            };
            if (active && runner.Running) { reconnectAfterStop = false; Disconnect(del); return false; }
            del();
            return true;
        }

        void FillServersMenu()
        {
            var dd = miServers.DropDownItems;
            dd.Clear();
            var active = Profiles.ActiveId;
            foreach (var p in Profiles.List().Take(12))
            {
                var pp = p;
                var it = new ToolStripMenuItem((p.Id == active ? "●  " : "     ") + p.Name, null, (s, e) => { if (!ModalUp()) SwitchTo(pp); })
                { Padding = new Padding(10, 5, 10, 5), Enabled = state != VState.Stopping };
                dd.Add(it);
            }
            if (dd.Count > 0) dd.Add(new ToolStripSeparator());
            dd.Add(new ToolStripMenuItem("مدیریت سرورها…", null, (s, e) => ShowServers()) { Padding = new Padding(10, 5, 10, 5) });
            dd.Add(new ToolStripMenuItem("افزودن با لینک…", null, (s, e) => ImportFlow()) { Padding = new Padding(10, 5, 10, 5) });
        }

        void Notify(string text, ToolTipIcon icon)
        {
            if (Ini.Get("notify", "1") == "0") return;
            try { tray.ShowBalloonTip(3000, "TrustTunnel", text, icon); } catch { }
        }

        void EditBypass()
        {
            if (ModalUp()) return;
            string text;
            try { text = Cfg.ReadBypass(); } catch (Exception ex) { Ui.Error(this, ex.Message); return; }
            using (var d = new BypassDialog(text))
            {
                if (d.ShowFor(this) != DialogResult.OK) return;
                try { Cfg.WriteBypass(d.Content); } catch (Exception ex) { Ui.Error(this, ex.Message); return; }
            }
            if (Cfg.Exists) { try { Cfg.ApplyBypass(); } catch { } }
            Toast("فهرست ذخیره شد");
            if (state == VState.On && Ui.Ask(this, "برای اعمال تغییرات، اتصال یک بار قطع و وصل شود؟")) { reconnectAfterStop = true; Disconnect(); }
        }

        void ShowLog()
        {
            if (ModalUp()) return;
            if (logWin == null || logWin.IsDisposed)
            {
                logWin = new LogWindow(LogPath) { Owner = Visible ? this : null, StartPosition = FormStartPosition.CenterScreen };
                logWin.SetAll(logLines);
            }
            if (!logWin.Visible) { logWin.SetAll(logLines); logWin.Show(); }
            logWin.Activate();
        }

        void CopyIp()
        {
            if (state != VState.On || string.IsNullOrEmpty(exitIp)) return;
            try { Clipboard.SetText(exitIp); Toast("IP کپی شد"); } catch { }
        }

        static string LogPath => Path.Combine(Cfg.Dir, "trusttunnel_last.log");

        // =============================================================== connect / disconnect

        void ToggleConnect()
        {
            switch (state)
            {
                case VState.NoConfig: ImportFlow(); break;
                case VState.Off: Connect(); break;
                case VState.Stopping: break;
                default: Disconnect(); break;
            }
        }

        void Connect()
        {
            if (runner.Running) return;
            if (!Cfg.Exists && !ImportFlow()) return;
            try { Cfg.ApplyBypass(); } catch (Exception ex) { AddLog("[gui] bypass: " + ex.Message); }
            serverIps = Cfg.ServerIps();
            serverPort = Cfg.ServerPort();
            lastError = null; userStop = false; exitIp = null; reconnectAfterStop = false;
            connectStart = DateTime.Now; lastProbe = DateTime.MinValue; lastPing = DateTime.MinValue;
            ResetStats();
            try { File.WriteAllText(LogPath, "TrustTunnel GUI " + GuiVersion + " log " + DateTime.Now.ToString("s") + "\r\n"); } catch { }
            SetState(VState.Connecting);
            AddLog("[gui] starting trusttunnel_client (" + (seg.Selected == 1 ? "http3" : "http2") + ")");
            try { runner.Start(); }
            catch (Exception ex) { SetState(VState.Off, "اجرای کلاینت ممکن نشد: " + ex.Message, Theme.Danger); }
        }

        void ResetStats()
        {
            nic = null; sCount = 0; statsOk = false; rateRx = rateTx = 0; sessRx = sessTx = 0; pingMs = -1;
            Array.Clear(sDown, 0, N); Array.Clear(sUp, 0, N);
        }

        void Disconnect(Action after = null)
        {
            if (!runner.Running) { after?.Invoke(); return; }
            if (after != null) afterStop += after;
            if (state == VState.Stopping) return;
            userStop = true;
            SetState(VState.Stopping);
            Task.Run(() => { try { runner.Stop(); } catch { } })
                .ContinueWith(_ => Post(() => { if (!runner.Running) FireAfterStop(); }));
        }

        void FireAfterStop()
        {
            var a = afterStop; afterStop = null;
            a?.Invoke();
        }

        void OnExited()
        {
            bool unexpected = !userStop;
            var err = lastError;
            AddLog("[gui] client exited");
            exitIp = null;
            SetState(Cfg.Exists ? VState.Off : VState.NoConfig,
                     unexpected ? (err ?? "اتصال به‌طور غیرمنتظره بسته شد؛ گزارش را ببینید.") : null, unexpected ? Theme.Danger : (Color?)null);
            if (unexpected) Notify("اتصال قطع شد. " + (err ?? ""), ToolTipIcon.Warning);
            FireAfterStop();
            if (reconnectAfterStop && !reallyExit) { reconnectAfterStop = false; Connect(); }
        }

        void MarkConnected()
        {
            if (state == VState.On || !runner.Running || state == VState.Stopping) return;
            bool first = state == VState.Connecting;
            connectedAt = first || connectedAt == DateTime.MinValue ? DateTime.Now : connectedAt;
            lastError = null;
            SetState(VState.On);
            exitIp = null; probeCount = 0;
            lastProbe = DateTime.Now.AddSeconds(-7);   // first probe ~3 s later, once routes are installed
            lastPing = DateTime.Now.AddSeconds(-18);
            if (!Visible) Notify(first ? "وصل شد ✓  " + profile : "اتصال دوباره برقرار شد ✓", ToolTipIcon.Info);
        }

        // =============================================================== client output

        static string Translate(string why)
        {
            var w = (why ?? "").ToLowerInvariant();
            if (w.Contains("auth") || w.Contains("407")) return "نام کاربری یا رمز پذیرفته نشد (کاربر غیرفعال یا حذف شده؟)";
            if (w.Contains("timed out") || w.Contains("timeout")) return "سرور پاسخ نمی‌دهد؛ اینترنت یا مسدودی را بررسی کنید";
            if (w.Contains("refused")) return "اتصال توسط سرور رد شد";
            if (w.Contains("certificate") || w.Contains("verify")) return "خطای گواهی سرور";
            if (w.Contains("bad response")) return "پاسخ نامعتبر از سرور";
            if (w.Contains("network") && w.Contains("unreach")) return "شبکه در دسترس نیست";
            if (w.Length == 0) return null;
            return why.Length > 90 ? why.Substring(0, 90) + "…" : why;
        }

        void OnLine(string line)
        {
            lock (pending) { pending.Enqueue(line); while (pending.Count > 3000) pending.Dequeue(); }
            if (line.IndexOf("Successfully connected to endpoint", StringComparison.Ordinal) >= 0) { Post(MarkConnected); return; }
            var pm = Regex.Match(line, @"pinger_handler.*ping=(\d+)ms");
            if (pm.Success) { int v = int.Parse(pm.Groups[1].Value); Post(() => { pingMs = v; Canvas.InvalidateLogical(tilePing.B); }); return; }
            if (line.IndexOf("Waiting recovery", StringComparison.Ordinal) >= 0)
            {
                var m = Regex.Match(line, @"error=-?\d+\s+(.*)$");
                var why = Translate(m.Success ? m.Groups[1].Value.Trim() : "") ?? "در حال تلاش دوباره…";
                lastError = why;
                Post(() => { if (state == VState.On || state == VState.Reconnecting || state == VState.Connecting) SetState(state == VState.Connecting ? VState.Connecting : VState.Reconnecting, why); });
                return;
            }
            if (line.IndexOf("VPN_SS_WAITING_FOR_NETWORK", StringComparison.Ordinal) >= 0)
            {
                Post(() => { if (state == VState.On || state == VState.Reconnecting) SetState(VState.Reconnecting, "در انتظار اینترنت…"); });
                return;
            }
            if (line.IndexOf("VPN_SS_RECOVERING", StringComparison.Ordinal) >= 0)
            {
                Post(() => { if (state == VState.On) SetState(VState.Reconnecting, "در حال بازیابی اتصال…"); });
                return;
            }
            if (line.IndexOf("Proxy authentication required", StringComparison.OrdinalIgnoreCase) >= 0) { lastError = Translate("auth"); return; }
            int k = line.IndexOf("Error: ", StringComparison.Ordinal);
            if (k >= 0) lastError = Translate(line.Substring(k + 7).Trim()) ?? lastError;
        }

        void AddLog(string line) { lock (pending) pending.Enqueue(line); }

        void OnTick()
        {
            tickCount++;
            List<string> batch = null;
            lock (pending) { if (pending.Count > 0) { batch = pending.ToList(); pending.Clear(); } }
            if (batch != null)
            {
                logLines.AddRange(batch);
                if (logLines.Count > 1500) logLines.RemoveRange(0, logLines.Count - 1500);
                try
                {
                    if (tickCount % 120 == 0) { var fi = new FileInfo(LogPath); if (fi.Exists && fi.Length > 4_000_000) { File.Copy(LogPath, LogPath + ".old", true); File.WriteAllText(LogPath, ""); } }
                    File.AppendAllText(LogPath, string.Join("\r\n", batch) + "\r\n");
                }
                catch { }
                if (logWin != null && !logWin.IsDisposed && logWin.Visible) logWin.Append(batch, logLines);
            }
            var now = DateTime.Now;
            if (state == VState.On)
            {
                var t = now - connectedAt;
                status = "مدت اتصال  " + Gfx.Fa(((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00"));
                statusColor = Theme.Muted;
                if (Visible) Canvas.InvalidateLogical(new RectangleF(20, 350, WW - 40, 26));
                if (tickCount % 2 == 0) SampleStats();
                bool viaServer = exitIp != null && serverIps.Contains(exitIp);
                if (!viaServer && probeCount < 6 && (now - lastProbe).TotalSeconds > 10) { probeCount++; ProbeIp(false); }
                if ((now - lastPing).TotalSeconds > 20) MeasurePing();
                if (tickCount % 4 == 0) UpdateTrayText();
            }
            else if (state == VState.Connecting && (now - connectStart).TotalSeconds > 5 && (now - lastProbe).TotalSeconds > 4)
            {
                ProbeIp(true);   // fallback if the client's log line is buffered
            }
        }

        void UpdateTrayText()
        {
            var tip = "TrustTunnel · وصل" + (statsOk ? "  ↓" + Gfx.Bytes(rateRx, true) + "  ↑" + Gfx.Bytes(rateTx, true) : "");
            try { tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip; } catch { }
        }

        // =============================================================== live statistics

        NetworkInterface FindNic()
        {
            NetworkInterface best = null;
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                string nm = n.Name ?? "", ds = n.Description ?? "";
                if (nm.StartsWith("TrustTunnel", StringComparison.OrdinalIgnoreCase)) return n;
                if (best == null && ds.IndexOf("wintun", StringComparison.OrdinalIgnoreCase) >= 0 && n.OperationalStatus == OperationalStatus.Up) best = n;
            }
            return best;
        }

        void SampleStats()
        {
            try
            {
                if (nic == null)
                {
                    nic = FindNic();
                    if (nic == null) return;
                    var s0 = nic.GetIPStatistics();
                    rx0 = rxLast = s0.BytesReceived; tx0 = txLast = s0.BytesSent; statAt = DateTime.UtcNow;
                    AddLog("[gui] stats adapter: " + nic.Name + " / " + nic.Description);
                    return;
                }
                var st = nic.GetIPStatistics();
                var now = DateTime.UtcNow;
                double dt = (now - statAt).TotalSeconds;
                if (dt < 0.5) return;
                long rx = st.BytesReceived, tx = st.BytesSent;
                if (rx < rxLast || tx < txLast) { rx0 = rxLast = rx; tx0 = txLast = tx; statAt = now; return; }
                rateRx = (rx - rxLast) / dt; rateTx = (tx - txLast) / dt;
                rxLast = rx; txLast = tx; statAt = now;
                sessRx = rx - rx0; sessTx = tx - tx0;
                statsOk = true;
                Array.Copy(sDown, 1, sDown, 0, N - 1); Array.Copy(sUp, 1, sUp, 0, N - 1);
                sDown[N - 1] = (float)rateRx; sUp[N - 1] = (float)rateTx;
                if (sCount < N) sCount++;
                if (Visible && Canvas.Active == main)
                {
                    Canvas.InvalidateLogical(StatsRect);
                    Canvas.InvalidateLogical(tileUse.B);
                }
            }
            catch { nic = null; }
        }

        void MeasurePing()
        {
            if (Interlocked.Exchange(ref pingBusy, 1) == 1) return;
            lastPing = DateTime.Now;
            var addr = serverIps.FirstOrDefault(i => !i.Contains(":")) ?? serverIps.FirstOrDefault();
            int port = serverPort;
            if (addr == null) { Interlocked.Exchange(ref pingBusy, 0); return; }
            Task.Run(() =>
            {
                int ms = -1;
                try
                {
                    var ip = IPAddress.Parse(addr);
                    using (var s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
                    {
                        var sw = Stopwatch.StartNew();
                        var ar = s.BeginConnect(ip, port, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(3000)) { s.EndConnect(ar); ms = (int)sw.ElapsedMilliseconds; }
                    }
                }
                catch { }
                finally { Interlocked.Exchange(ref pingBusy, 0); }
                Post(() => { if (ms >= 0) { pingMs = ms; Canvas.InvalidateLogical(tilePing.B); } });
            });
        }

        void ProbeIp(bool markIfServer)
        {
            if (Interlocked.Exchange(ref probeBusy, 1) == 1) return;
            lastProbe = DateTime.Now;
            Task.Run(() =>
            {
                string ip = null;
                try
                {
                    var rq = (HttpWebRequest)WebRequest.Create("https://api.ipify.org");
                    rq.Timeout = 6000; rq.ReadWriteTimeout = 6000; rq.Proxy = null; rq.UserAgent = "TrustTunnelGUI";
                    rq.KeepAlive = false; rq.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                    using (var rs = rq.GetResponse())
                    using (var sr = new StreamReader(rs.GetResponseStream())) ip = sr.ReadToEnd().Trim();
                }
                catch { }
                finally { Interlocked.Exchange(ref probeBusy, 0); }
                Post(() =>
                {
                    if (ip == null || ip.Length > 45 || !IPAddress.TryParse(ip, out _)) return;
                    bool viaServer = serverIps.Contains(ip);
                    if (state == VState.Connecting && markIfServer && viaServer) MarkConnected();
                    if (state == VState.On) { exitIp = ip; Canvas.InvalidateLogical(tileIp.B); }
                });
            });
        }

        // =============================================================== visual state

        void SetState(VState s, string sub = null, Color? subColor = null)
        {
            var prev = state;
            state = s;
            if (prev == VState.On && s == VState.Reconnecting && !Visible) Notify("اتصال موقتاً قطع شد؛ در حال اتصال مجدد…", ToolTipIcon.Warning);
            Color n1, n2;
            bool spin = false;
            switch (s)
            {
                case VState.NoConfig:
                    orbWord = "بدون سرور"; orbHint = "لینک را وارد کنید"; n1 = Theme.Idle; n2 = Theme.Idle;
                    Btn("وارد کردن لینک", PillStyle.Primary, true, (g, r, c) => Glyph.Link(g, r, c, 2f));
                    sub = sub ?? "لینک اتصال را از پنل کپی کنید"; break;
                case VState.Connecting:
                    orbWord = "در حال اتصال"; orbHint = "لطفاً صبر کنید"; n1 = Theme.Wait1; n2 = Theme.Wait2; spin = true;
                    Btn("لغو", PillStyle.Wait, true, null);
                    sub = sub ?? (seg.Selected == 1 ? "HTTP/3 · QUIC" : "HTTP/2 · TCP"); break;
                case VState.Reconnecting:
                    orbWord = "اتصال مجدد"; orbHint = "اتصال موقتاً قطع شد"; n1 = Theme.Wait1; n2 = Theme.Wait2; spin = true;
                    Btn("قطع اتصال", PillStyle.Danger, true, null); break;
                case VState.On:
                    orbWord = "وصل"; orbHint = "ترافیک شما امن است"; n1 = Theme.On1; n2 = Theme.On2;
                    Btn("قطع اتصال", PillStyle.Danger, true, null); break;
                case VState.Stopping:
                    orbWord = "در حال قطع"; orbHint = "چند لحظه…"; n1 = Theme.Idle; n2 = Theme.Rgb(0x6A, 0x74, 0x98); spin = true;
                    Btn("صبر کنید…", PillStyle.Ghost, false, null); break;
                default:
                    orbWord = "قطع"; orbHint = "برای اتصال بزنید"; n1 = Theme.Mix(Theme.Idle, Theme.Off1, 0.45f); n2 = Theme.Mix(Theme.Idle, Theme.Off2, 0.45f);
                    Btn("اتصال", PillStyle.Primary, true, (g, r, c) => Glyph.Power(g, r, c, 2.2f));
                    sub = sub ?? ""; break;
            }
            spinning = spin;
            if (n1 != t1 || n2 != t2) { f1 = c1; f2 = c2; t1 = n1; t2 = n2; colorT = 0; }
            if (s != VState.On) { status = sub ?? ""; statusColor = subColor ?? (s == VState.Reconnecting ? Theme.Wait1 : Theme.Muted); }
            else if (sub != null) { status = sub; statusColor = subColor ?? Theme.Muted; }
            orb.Clickable = s != VState.Stopping;
            btnNewLink.Enabled = s != VState.Stopping;

            tray.Icon = TrayIcon(s == VState.On ? "on" : (s == VState.Off || s == VState.NoConfig ? "off" : "wait"));
            var tip = "TrustTunnel · " + orbWord;
            try { tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip; } catch { }
            miHead.Text = profile + "  ·  " + orbWord;
            miToggle.Text = (s == VState.Off || s == VState.NoConfig) ? "اتصال" : "قطع اتصال";
            miToggle.Enabled = s != VState.Stopping;
            Canvas.Invalidate();
            Canvas.Kick();
        }

        void Btn(string text, PillStyle style, bool enabled, Action<Graphics, RectangleF, Color> icon)
        {
            btnMain.Text = text; btnMain.Style = style; btnMain.Enabled = enabled; btnMain.Icon = icon;
            btnMain.C1 = Theme.Off1; btnMain.C2 = Theme.Off2;
        }
    }
}
