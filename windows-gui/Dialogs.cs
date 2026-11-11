using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TT
{
    /// <summary>Base for the app's dialogs: borderless glass window, title, close button, text fields.</summary>
    class DarkDialog : GlassForm
    {
        protected readonly Page Page = new Page();
        protected string Title, Subtitle;
        readonly List<KeyValuePair<RectangleF, TextBox>> fields = new List<KeyValuePair<RectangleF, TextBox>>();
        protected Action Primary;

        protected DarkDialog(string title, float w, float h) : base(w, h)
        {
            Title = title;
            ShowInTaskbar = false;
            MinimizeBox = false; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            Text = title;
            Canvas.Background = (g, L) =>
            {
                Backdrop(g, L, 11);
                Gfx.Glow(g, new PointF(L.Width - 40, -10), 220, 140, Theme.Off2, 26);
            };
            Page.Under = g =>
            {
                if (!string.IsNullOrEmpty(Title))
                    Gfx.Text(g, Title, FontKit.Px(17, 700), Theme.Text, new RectangleF(60, 12, LW - 84, 32));
                if (!string.IsNullOrEmpty(Subtitle))
                    Gfx.Text(g, Subtitle, FontKit.Px(11.5f), Theme.Muted, new RectangleF(24, 42, LW - 48, 20));
                foreach (var f in fields)
                {
                    var r = f.Key;
                    Gfx.FillRound(g, r, 14, Theme.Field);
                    Gfx.StrokeRound(g, r, 14, f.Value.Focused ? Theme.A(Theme.Off1, 170) : Theme.StrokeHi, f.Value.Focused ? 1.4f : 1f);
                }
            };
            Canvas.Chrome = new Page();
            Canvas.Chrome.Add(new IconButton { B = new RectangleF(12, 12, 32, 32), Danger = true, Inset = 11, Glyph = (g, r, c) => Glyph.Close(g, r, c), Click = Cancel });
            Canvas.Show(Page, 0);
        }

        protected void Cancel() { DialogResult = DialogResult.Cancel; Close(); }

        protected TextBox Field(RectangleF r, bool multiline, bool readOnly = false)
        {
            var tb = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text, Font = FontKit.MonoPt(10f),
                Multiline = multiline, ReadOnly = readOnly, RightToLeft = RightToLeft.No, HideSelection = false,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, WordWrap = true, AcceptsReturn = multiline
            };
            var inner = RectangleF.Inflate(r, -12, -10);
            tb.Bounds = PxRect(inner);
            Canvas.Controls.Add(tb);
            tb.HandleCreated += (s, e) => Native.DarkScrollbars(tb);
            tb.GotFocus += (s, e) => Canvas.InvalidateLogical(r);
            tb.LostFocus += (s, e) => Canvas.InvalidateLogical(r);
            fields.Add(new KeyValuePair<RectangleF, TextBox>(r, tb));
            return tb;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Cancel(); }
        }

        /// <summary>The modal dialog currently on screen (null if none).</summary>
        public static Form ActiveModal { get; private set; }

        public DialogResult ShowFor(IWin32Window owner)
        {
            var prev = ActiveModal;
            ActiveModal = this;
            try
            {
                var f = owner as Form;
                if (f == null || !f.Visible || f.WindowState == FormWindowState.Minimized)
                {
                    StartPosition = FormStartPosition.CenterScreen;
                    TopMost = true;
                    return ShowDialog();
                }
                return ShowDialog(owner);
            }
            finally { ActiveModal = prev; }
        }
    }

    /// <summary>Dark, Persian message box.</summary>
    sealed class MsgBox : DarkDialog
    {
        readonly string message;
        readonly char kind;
        readonly float textH;

        MsgBox(string text, char kind, bool ask, float textH, float h) : base("", 420, h)
        {
            message = text; this.kind = kind; this.textH = textH;
            Text = "TrustTunnel";
            Color c = kind == 'e' ? Theme.Danger : kind == '?' ? Theme.Wait1 : kind == 'v' ? Theme.On1 : Theme.Off1;
            Page.Over = g =>
            {
                var ic = new RectangleF(LW - 24 - 46, 34, 46, 46);
                Gfx.Glow(g, new PointF(ic.X + 23, ic.Y + 23), 46, 46, c, 60);
                using (var b = new SolidBrush(Theme.A(c, 34))) g.FillEllipse(b, ic);
                using (var p = new Pen(Theme.A(c, 150), 1.3f)) g.DrawEllipse(p, ic);
                Glyph.Info(g, RectangleF.Inflate(ic, -13, -12), c, kind == 'e' ? '!' : kind);
                Gfx.Text(g, message, FontKit.Px(13.5f), Theme.Text, new RectangleF(24, 38, LW - 24 - 46 - 18 - 24, textH + 6), Al.Right, true, true, StringAlignment.Near);
            };
            float by = h - 22 - 44;
            var ok = Page.Add(new PillButton { Text = ask ? "بله" : "باشه", B = new RectangleF(LW - 24 - 120, by, 120, 44), FontPx = 14,
                                               C1 = kind == 'e' ? Theme.Danger : Theme.Off1, C2 = kind == 'e' ? Theme.Wait2 : Theme.Off2 });
            ok.Click = () => { DialogResult = ask ? DialogResult.Yes : DialogResult.OK; Close(); };
            Primary = ok.Click;
            if (ask)
            {
                var no = Page.Add(new PillButton { Text = "خیر", Style = PillStyle.Ghost, B = new RectangleF(LW - 24 - 120 - 10 - 100, by, 100, 44), FontPx = 14 });
                no.Click = () => { DialogResult = DialogResult.No; Close(); };
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; Primary?.Invoke(); return; }
            base.OnKeyDown(e);
        }

        public static DialogResult Run(IWin32Window owner, string text, char kind, bool ask)
        {
            float th;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                Gfx.Hq(g);
                th = Gfx.Measure(g, text, FontKit.Px(13.5f), 420 - 24 - 46 - 18 - 24).Height;
            }
            float h = Math.Max(46f, th) + 38 + 28 + 44 + 22;
            using (var m = new MsgBox(text, kind, ask, th, h)) return m.ShowFor(owner);
        }
    }

    static class Ui
    {
        public static void Error(IWin32Window o, string t) => MsgBox.Run(o, t, 'e', false);
        public static void Info(IWin32Window o, string t) => MsgBox.Run(o, t, 'i', false);
        public static void Success(IWin32Window o, string t) => MsgBox.Run(o, t, 'v', false);
        public static bool Ask(IWin32Window o, string t) => MsgBox.Run(o, t, '?', true) == DialogResult.Yes;
    }

    sealed class ImportDialog : DarkDialog
    {
        readonly TextBox tb;
        string error;
        public string Link => (tb.Text ?? "").Trim().Trim('"', '\'');

        public ImportDialog(string prefill = null) : base("وارد کردن لینک", 460, 340)
        {
            Subtitle = "";
            var desc = "لینک اتصال کاربر (که با tt شروع می‌شود) را از پنل مدیریت کپی کنید. بعد دکمه‌ی «چسباندن» را بزنید یا Ctrl+V.";
            Page.Over = g =>
            {
                Gfx.Text(g, desc, FontKit.Px(13f), Theme.Muted, new RectangleF(24, 52, LW - 48, 44), Al.Right, true, true, StringAlignment.Near);
                if (!string.IsNullOrEmpty(error))
                    Gfx.Text(g, error, FontKit.Px(12.5f, 500), Theme.Danger, new RectangleF(24, 228, LW - 48, 22));
            };
            tb = Field(new RectangleF(24, 104, LW - 48, 118), true);
            tb.AcceptsReturn = false;
            tb.TextChanged += (s, e) => { if (error != null) { error = null; Canvas.Invalidate(); } };
            if (!string.IsNullOrEmpty(prefill)) tb.Text = prefill.Trim();

            float by = 266;
            var ok = Page.Add(new PillButton { Text = "ذخیره", B = new RectangleF(LW - 24 - 132, by, 132, 46), FontPx = 14.5f });
            ok.Click = Save;
            Primary = Save;
            Page.Add(new PillButton { Text = "انصراف", Style = PillStyle.Ghost, B = new RectangleF(LW - 24 - 132 - 10 - 100, by, 100, 46), FontPx = 14, Click = Cancel });
            Page.Add(new PillButton { Text = "چسباندن", Style = PillStyle.Ghost, B = new RectangleF(24, by, 112, 46), FontPx = 13.5f, Weight = 500,
                                      Icon = (g, r, c) => Glyph.Copy(g, r, c), Click = () => TryPaste(true) });
        }

        void TryPaste(bool explicitly)
        {
            // Only on an explicit click, through the text box itself (no background clipboard reads).
            if (!explicitly) return;
            try
            {
                tb.Focus(); tb.SelectAll(); tb.Paste();
                if (!Link.StartsWith("tt:", StringComparison.OrdinalIgnoreCase)) { error = "لینک اتصال چسبانده نشد؛ لینک را کپی و دوباره امتحان کنید."; Canvas.Invalidate(); }
            }
            catch { }
        }

        void Save()
        {
            if (!Link.StartsWith("tt://", StringComparison.OrdinalIgnoreCase)) { error = "لینک باید با tt شروع شود."; Canvas.Invalidate(); tb.Focus(); return; }
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; Save(); return; }
            base.OnKeyDown(e);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); tb.Focus(); tb.SelectAll(); }
    }

    sealed class BypassDialog : DarkDialog
    {
        readonly TextBox tb;
        public string Content => tb.Text;

        public BypassDialog(string current) : base("سایت‌های مستقیم", 460, 580)
        {
            var desc = "این سایت‌ها بدون VPN و مستقیم باز می‌شوند؛ هر خط یک مورد.\n‎*.ir یعنی همه‌ی دامنه‌های ایرانی. برای ‎.com هم site.com و هم ‎*.site.com را بنویسید.";
            Page.Over = g => Gfx.Text(g, desc, FontKit.Px(12.5f), Theme.Muted, new RectangleF(24, 52, LW - 48, 62), Al.Right, true, true, StringAlignment.Near);
            tb = Field(new RectangleF(24, 120, LW - 48, 366), true);
            tb.Text = current;
            float by = 508;
            var ok = Page.Add(new PillButton { Text = "ذخیره", B = new RectangleF(LW - 24 - 132, by, 132, 46), FontPx = 14.5f });
            ok.Click = () => { DialogResult = DialogResult.OK; Close(); };
            Page.Add(new PillButton { Text = "انصراف", Style = PillStyle.Ghost, B = new RectangleF(LW - 24 - 132 - 10 - 100, by, 100, 46), FontPx = 14, Click = Cancel });
            Page.Add(new PillButton { Text = "پیش‌فرض", Style = PillStyle.Ghost, B = new RectangleF(24, by, 104, 46), FontPx = 13.5f, Weight = 500, Click = () => tb.Text = Cfg.DefaultBypass });
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); tb.Focus(); tb.SelectionStart = tb.TextLength; tb.SelectionLength = 0; }
    }

    /// <summary>Live connection log (non-modal).</summary>
    sealed class LogWindow : DarkDialog
    {
        readonly TextBox tb;
        public bool WantsLines => Visible;

        public LogWindow(string path) : base("گزارش اتصال", 680, 500)
        {
            Subtitle = "";
            Page.Over = g => Gfx.Text(g, path, FontKit.Px(11f), Theme.Dim, new RectangleF(24, 46, LW - 48, 18), Al.Right, false);
            tb = Field(new RectangleF(20, 70, LW - 40, 352), true, true);
            tb.Font = FontKit.MonoPt(8.5f);
            tb.WordWrap = true;
            tb.ScrollBars = ScrollBars.Vertical;
            tb.ForeColor = Theme.Rgb(0xB4, 0xBC, 0xD6);
            float by = 438;
            Page.Add(new PillButton { Text = "بستن", Style = PillStyle.Ghost, B = new RectangleF(LW - 24 - 110, by, 110, 44), FontPx = 14, Click = () => Hide() });
            Page.Add(new PillButton { Text = "کپی همه", Style = PillStyle.Ghost, B = new RectangleF(LW - 24 - 110 - 10 - 120, by, 120, 44), FontPx = 13.5f, Weight = 500,
                                      Icon = (g, r, c) => Glyph.Copy(g, r, c), Click = () => { try { if (tb.TextLength > 0) { tb.SelectAll(); tb.Copy(); } } catch { } } });
            Page.Add(new PillButton { Text = "باز کردن پوشه", Style = PillStyle.Ghost, B = new RectangleF(24, by, 150, 44), FontPx = 13.5f, Weight = 500,
                                      Icon = (g, r, c) => Glyph.Folder(g, r, c), Click = () => Sys.OpenFolder() });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        // "10.11.2026 20:36:56.239281 INFO  [6792] X" -> "20:36:56  INFO  X" (the file keeps the full line)
        static readonly System.Text.RegularExpressions.Regex Stamp =
            new System.Text.RegularExpressions.Regex(@"^\d{1,2}\.\d{1,2}\.\d{4} (\d\d:\d\d:\d\d)\.\d+\s+(\w+)\s+\[\d+\]\s*");
        static string Short(string l) => Stamp.Replace(l, m => m.Groups[1].Value + "  " + m.Groups[2].Value.PadRight(5) + " ", 1);
        static string Join(IEnumerable<string> lines) => string.Join("\r\n", lines.Select(Short));

        public void SetAll(IEnumerable<string> lines)
        {
            tb.Text = Join(lines);
            tb.SelectionStart = tb.TextLength; tb.SelectionLength = 0; tb.ScrollToCaret();
        }

        public void Append(IList<string> batch, IEnumerable<string> all)
        {
            if (tb.TextLength > 200000) { SetAll(all); return; }
            tb.AppendText((tb.TextLength > 0 ? "\r\n" : "") + Join(batch));
        }
    }

    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Tag as string == "head" ? Theme.Muted : e.Item.Enabled ? Theme.Text : Theme.Dim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Muted; base.OnRenderArrow(e); }

        sealed class DarkColors : ProfessionalColorTable
        {
            static readonly Color Bg = Theme.Rgb(0x12, 0x17, 0x2B), Sel = Theme.Rgb(0x24, 0x2C, 0x4C), Line = Theme.Rgb(0x2A, 0x33, 0x55);
            public override Color ToolStripDropDownBackground => Bg;
            public override Color ImageMarginGradientBegin => Bg;
            public override Color ImageMarginGradientMiddle => Bg;
            public override Color ImageMarginGradientEnd => Bg;
            public override Color MenuBorder => Line;
            public override Color MenuItemBorder => Sel;
            public override Color MenuItemSelected => Sel;
            public override Color MenuItemSelectedGradientBegin => Sel;
            public override Color MenuItemSelectedGradientEnd => Sel;
            public override Color MenuItemPressedGradientBegin => Sel;
            public override Color MenuItemPressedGradientEnd => Sel;
            public override Color SeparatorDark => Line;
            public override Color SeparatorLight => Bg;
        }
    }
}
