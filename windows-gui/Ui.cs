using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security;
using System.Text;
using System.Windows.Forms;

namespace TT
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(15, 20, 32);
        public static readonly Color Card = Color.FromArgb(27, 35, 52);
        public static readonly Color Border = Color.FromArgb(48, 60, 84);
        public static readonly Color Text = Color.FromArgb(230, 233, 240);
        public static readonly Color Muted = Color.FromArgb(150, 160, 180);
        public static readonly Color Green = Color.FromArgb(34, 197, 94);
        public static readonly Color Orange = Color.FromArgb(245, 158, 11);
        public static readonly Color Red = Color.FromArgb(225, 70, 70);
        public static readonly Color Gray = Color.FromArgb(105, 115, 135);
        public static readonly Color Accent = Color.FromArgb(56, 132, 246);
        public static Font F(float size, FontStyle st = FontStyle.Regular) => new Font("Tahoma", size, st);
    }

    static class Ui
    {
        const MessageBoxOptions Rtl = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;
        public static void Error(IWin32Window owner, string text) =>
            MessageBox.Show(owner, text, "TrustTunnel", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, Rtl);
        public static void Info(IWin32Window owner, string text) =>
            MessageBox.Show(owner, text, "TrustTunnel", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Rtl);
        public static bool Ask(IWin32Window owner, string text) =>
            MessageBox.Show(owner, text, "TrustTunnel", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, Rtl) == DialogResult.Yes;

        public static Button Button(string text, int x, int y, int w, int h, Color back, float size = 9.5f, bool border = false)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), BackColor = back, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = Theme.F(size, size >= 11 ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand, RightToLeft = RightToLeft.Yes, UseVisualStyleBackColor = false, TabStop = true
            };
            b.FlatAppearance.BorderSize = border ? 1 : 0;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.05f);
            return b;
        }

        public static Label Label(string text, int x, int y, int w, int h, Color color, float size = 9.5f,
                                  FontStyle st = FontStyle.Regular, ContentAlignment align = ContentAlignment.MiddleRight)
            => new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), ForeColor = color, BackColor = Color.Transparent,
                           Font = Theme.F(size, st), TextAlign = align, RightToLeft = RightToLeft.Yes, AutoEllipsis = true };
    }

    static class Icons
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        /// <summary>Round badge with a white "tunnel entrance" glyph.</summary>
        public static Icon Make(Color c)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, 1, 1, 30, 30);
                    using (var pen = new Pen(Color.White, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawArc(pen, 9, 8, 14, 14, 180, 180);
                        g.DrawLine(pen, 9, 15, 9, 23);
                        g.DrawLine(pen, 23, 15, 23, 23);
                    }
                }
                IntPtr h = bmp.GetHicon();
                var ico = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ico;
            }
        }
    }

    /// <summary>"Run at Windows sign-in" via a Task Scheduler task (elevated, no UAC prompt, works on battery).</summary>
    static class AutoStart
    {
        const string TaskName = "TrustTunnelGUI";

        public static bool Enabled() => Run("/Query /TN " + TaskName) == 0;

        public static bool Set(bool on)
        {
            if (!on) return Run("/Delete /F /TN " + TaskName) == 0;
            var user = Environment.UserDomainName + "\\" + Environment.UserName;
            var xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                "  <RegistrationInfo><Description>TrustTunnel VPN (Sabi)</Description></RegistrationInfo>\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + "</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers>\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\n" +
                "  <Actions Context=\"Author\"><Exec><Command>\"" + SecurityElement.Escape(Application.ExecutablePath) + "\"</Command><Arguments>--tray</Arguments></Exec></Actions>\n" +
                "</Task>\n";
            var tmp = Path.Combine(Path.GetTempPath(), "trusttunnel_task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try { return Run("/Create /F /TN " + TaskName + " /XML \"" + tmp + "\"") == 0; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        static int Run(string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }

    /// <summary>Big status ring with an animated arc while connecting.</summary>
    sealed class StatusRing : Control
    {
        Color ring = Theme.Gray; string title = ""; string sub = ""; bool spin; float angle;
        readonly Timer timer = new Timer { Interval = 30 };
        readonly Font fTitle = Theme.F(15f, FontStyle.Bold), fSub = Theme.F(8.5f);

        public StatusRing()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            timer.Tick += (s, e) => { angle = (angle + 7) % 360; Invalidate(); };
        }

        public void Set(Color c, string t, string s, bool spinning)
        {
            ring = c; title = t; sub = s ?? ""; spin = spinning;
            timer.Enabled = spinning;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int thick = Math.Max(8, Width / 18);
            int s = Math.Min(Width, Height) - thick * 2;
            var r = new Rectangle((Width - s) / 2, (Height - s) / 2, s, s);
            using (var glow = new SolidBrush(Color.FromArgb(28, ring))) g.FillEllipse(glow, Rectangle.Inflate(r, -thick, -thick));
            using (var track = new Pen(Theme.Border, thick)) g.DrawEllipse(track, r);
            using (var pen = new Pen(ring, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (spin) g.DrawArc(pen, r, angle, 110); else g.DrawEllipse(pen, r);
            }
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.WordBreak;
            int h1 = fTitle.Height + 6;
            TextRenderer.DrawText(g, title, fTitle, new Rectangle(r.X + thick, r.Y + r.Height / 2 - h1, r.Width - 2 * thick, h1), Theme.Text, flags);
            TextRenderer.DrawText(g, sub, fSub, new Rectangle(r.X + thick * 2, r.Y + r.Height / 2 + 2, r.Width - 4 * thick, fSub.Height * 2 + 4), Theme.Muted, flags);
        }

        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); fTitle.Dispose(); fSub.Dispose(); } base.Dispose(disposing); }
    }

    abstract class DarkDialog : Form
    {
        protected DarkDialog(string title, int w, int h)
        {
            Text = title; RightToLeft = RightToLeft.Yes; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(w, h);
        }

        protected TextBox Box(int x, int y, int w, int h, string text) => new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(x, y), Size = new Size(w, h), Text = text,
            BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, RightToLeft = RightToLeft.No,
            Font = new Font("Consolas", 9.5f), AcceptsReturn = true
        };
    }

    sealed class ImportDialog : DarkDialog
    {
        readonly TextBox tb;
        public string Link => tb.Text.Trim();

        public ImportDialog() : base("وارد کردن لینک", 460, 250)
        {
            Controls.Add(Ui.Label("لینک tt:// کاربر را از پنل مدیریت کپی کنید و اینجا paste کنید (Ctrl+V):", 16, 12, 428, 40, Theme.Text));
            tb = Box(16, 56, 428, 120, "");
            tb.AcceptsReturn = false;
            Controls.Add(tb);
            try { var c = Clipboard.GetText().Trim(); if (c.StartsWith("tt://")) tb.Text = c; } catch { }
            var ok = Ui.Button("ذخیره", 460 - 16 - 130, 192, 130, 40, Theme.Accent, 10f);
            var cancel = Ui.Button("انصراف", 460 - 16 - 130 - 10 - 100, 192, 100, 40, Theme.Card, 9.5f, true);
            ok.Click += (s, e) =>
            {
                if (!Link.StartsWith("tt://")) { Ui.Error(this, "لینک باید با tt:// شروع شود."); return; }
                DialogResult = DialogResult.OK;
            };
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    sealed class BypassDialog : DarkDialog
    {
        readonly TextBox tb;
        public string Content => tb.Text;

        public BypassDialog(string current) : base("سایت‌های مستقیم (بدون VPN)", 460, 470)
        {
            Controls.Add(Ui.Label("هر خط یک سایت. این سایت‌ها مستقیم و بدون VPN باز می‌شوند.\n‎*.ir یعنی همه‌ی سایت‌های ایرانی؛ برای .com هم site.com و هم ‎*.site.com را بنویسید.",
                                  16, 10, 428, 52, Theme.Text, 9f, FontStyle.Regular, ContentAlignment.TopRight));
            tb = Box(16, 66, 428, 336, current);
            Controls.Add(tb);
            var ok = Ui.Button("ذخیره", 460 - 16 - 130, 414, 130, 40, Theme.Accent, 10f);
            var cancel = Ui.Button("انصراف", 460 - 16 - 130 - 10 - 100, 414, 100, 40, Theme.Card, 9.5f, true);
            var reset = Ui.Button("پیش‌فرض", 16, 414, 100, 40, Theme.Card, 9.5f, true);
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            reset.Click += (s, e) => tb.Text = Cfg.DefaultBypass;
            Controls.Add(ok); Controls.Add(cancel); Controls.Add(reset);
            CancelButton = cancel;
        }
    }
}
