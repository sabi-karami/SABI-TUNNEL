using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TT
{
    /// <summary>List of saved servers. Result: Action = "select" | "delete" | "add", Id = profile id.</summary>
    sealed class ServersDialog : DarkDialog
    {
        public string Action, Id;
        const float RowH = 60, ListTop = 74;
        public const int MaxRows = 8;

        public ServersDialog(List<Profile> list, string activeId, bool canSwitch)
            : base("سرورها", 440, DialogHeight(list.Count))
        {
            Text = "TrustTunnel · سرورها";
            int n = Math.Min(list.Count, MaxRows);
            string hint = list.Count == 0 ? "هنوز سروری ذخیره نشده است."
                        : canSwitch ? "روی هر سرور بزنید تا به آن وصل شوید." : "در حال قطع اتصال… چند لحظه صبر کنید.";
            Page.Over = g => Gfx.Text(g, hint, FontKit.Px(12f), Theme.Muted, new RectangleF(24, 44, LW - 48, 20));
            for (int i = 0; i < n; i++)
            {
                var p = list[i];
                bool active = p.Id == activeId;
                var row = new RectangleF(24, ListTop + i * RowH, LW - 48, RowH - 8);
                Page.Add(new Custom
                {
                    B = row, Clickable = canSwitch, GlowPad = 2,
                    Draw = (g, w) => PaintRow(g, w, p, active),
                    Click = () => Done("select", p.Id)
                });
                Page.Add(new IconButton
                {
                    B = new RectangleF(row.X + 10, row.Y + (row.Height - 32) / 2, 32, 32), Danger = true, Inset = 8,
                    Glyph = (g, r, c) => Glyph.Trash(g, r, c), Click = () => Done("delete", p.Id)
                });
            }
            float by = LH - 22 - 46;
            var add = Page.Add(new PillButton
            {
                Text = "افزودن سرور", B = new RectangleF(LW - 24 - 156, by, 156, 46), FontPx = 14,
                Icon = (g, r, c) => Glyph.Link(g, r, c, 1.8f), Click = () => Done("add", null)
            });
            Primary = add.Click;
            Page.Add(new PillButton { Text = "بستن", Style = PillStyle.Ghost, B = new RectangleF(24, by, 100, 46), FontPx = 14, Click = Cancel });
        }

        static float DialogHeight(int count) => ListTop + Math.Max(1, Math.Min(count, MaxRows)) * RowH + 14 + 46 + 22;

        void Done(string action, string id) { Action = action; Id = id; DialogResult = DialogResult.OK; Close(); }

        static void PaintRow(Graphics g, Custom w, Profile p, bool active)
        {
            var r = w.B;
            Gfx.FillRound(g, r, 14, Color.FromArgb(10 + (int)(14 * w.HoverT), 255, 255, 255));
            Gfx.StrokeRound(g, r, 14, active ? Theme.A(Theme.On1, 120) : Theme.Mix(Theme.Stroke, Theme.StrokeHi, w.HoverT), active ? 1.3f : 1f);
            var dot = new RectangleF(r.Right - 22, r.Y + 17, 8, 8);
            if (active) Gfx.Glow(g, new PointF(dot.X + 4, dot.Y + 4), 10, 10, Theme.On1, 120);
            using (var b = new SolidBrush(active ? Theme.On1 : Theme.Dim)) g.FillEllipse(b, dot);
            var tx = new RectangleF(r.X + 52, r.Y, r.Width - 52 - 34, 0);
            Gfx.Text(g, p.Name, FontKit.Px(14, 700), Theme.Text, new RectangleF(tx.X, r.Y + 6, tx.Width, 22));
            var sub = (string.IsNullOrEmpty(p.Host) ? "—" : p.Host) + (string.IsNullOrEmpty(p.User) ? "" : "  ·  " + p.User);
            Gfx.Text(g, sub, FontKit.Px(11.5f), Theme.Muted, new RectangleF(tx.X, r.Y + 28, tx.Width, 18), Al.Right, false);
            if (active)
                Gfx.Text(g, "فعلی", FontKit.Px(11f, 700), Theme.On1, new RectangleF(tx.X, r.Y + 6, 60, 22), Al.Left);
        }
    }
}
