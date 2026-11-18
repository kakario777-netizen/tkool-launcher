using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 화면 위에 뜨는 고르기 창 (정렬 방식·해상도·언어·예/아니오 등). 패드 위아래+A, 터치로 고름. B나 바깥 터치는 닫기.
    /// </summary>
    class PickerScreen : Screen
    {
        Screen under;
        string title, sub;
        string[] opts;
        int sel, cur, top;
        Action<int> pick;
        Rectangle box;
        Rectangle[] rects = new Rectangle[0];
        const int MaxRows = 8;

        public static void Show(ShellForm sh, string title, string[] opts, int current, Action<int> pick, string sub = null)
        {
            if (opts == null || opts.Length == 0) return;
            var p = new PickerScreen { under = sh.Cur, title = title, sub = sub, opts = opts, cur = current, sel = Math.Max(0, Math.Min(opts.Length - 1, current)), pick = pick };
            sh.Push(p);
        }

        /// <summary>예/아니오 묻기</summary>
        public static void Confirm(ShellForm sh, string question, string yes, Action onYes, string sub = null) =>
            Show(sh, question, new[] { yes, "취소" }, 1, i => { if (i == 0) onYes(); }, sub);

        public override GameInfo FocusedGame => under?.FocusedGame;

        public override void Paint(Graphics g)
        {
            if (under != null) { under.Area = Area; under.Paint(g); }
            using (var dim = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillRectangle(dim, Area);

            int rowH = S(56), n = Math.Min(MaxRows, opts.Length);
            if (sel < top) top = sel;
            if (sel >= top + n) top = sel - n + 1;
            int headH = S(sub != null ? 84 : 60);
            int w = Math.Min(Area.Width - S(80), S(560));
            int h = headH + n * rowH + S(20);
            box = new Rectangle(Area.X + (Area.Width - w) / 2, Area.Y + (Area.Height - h) / 2, w, h);
            Draw.Fill(g, box, S(10), Color.FromArgb(255, 34, 40, 50));
            Draw.Text(g, title, Draw.Menu, new Rectangle(box.X + S(22), box.Y + S(12), w - S(44), S(36)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (sub != null)
                Draw.Text(g, sub, Draw.Small, new Rectangle(box.X + S(22), box.Y + S(48), w - S(44), S(26)), Theme.SubText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (rects.Length != n) rects = new Rectangle[n];
            for (int k = 0; k < n; k++)
            {
                int i = top + k;
                var r = new Rectangle(box.X + S(12), box.Y + headH + k * rowH, w - S(24), rowH - S(6));
                rects[k] = r;
                if (i == sel) Draw.Fill(g, r, S(6), Color.FromArgb(255, 52, 62, 78));
                Draw.Text(g, opts[i], Draw.BodyBold, new Rectangle(r.X + S(16), r.Y, r.Width - S(60), r.Height), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (i == cur)
                    using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, r.Right - S(30), r.Y + r.Height / 2 - S(6), S(12), S(12));
                if (i == sel && Shell.ShowFocus) Draw.FocusGlow(g, r, S(6));
            }
            if (opts.Length > n)
                Draw.Text(g, (top > 0 ? "▲ " : "") + (top + n < opts.Length ? "▼" : ""), Draw.Small,
                    new Rectangle(box.Right - S(60), box.Y + S(12), S(40), S(36)), Theme.SubText, Draw.Right);
        }

        void Choose(int i)
        {
            Shell.Pop();
            try { pick?.Invoke(i); } catch (Exception ex) { Log.Error("고르기", ex); }
            Shell.Invalidate();
        }

        public override void OnPad(Pad b)
        {
            switch (b)
            {
                case Pad.Up: sel = (sel - 1 + opts.Length) % opts.Length; break;
                case Pad.Down: sel = (sel + 1) % opts.Length; break;
                case Pad.A: Choose(sel); return;
                case Pad.B: Shell.Pop(); return;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            for (int k = 0; k < rects.Length; k++) if (rects[k].Contains(p)) { Choose(top + k); return; }
            if (!box.Contains(p)) Shell.Pop();
        }

        public override void OnDrag(int dx, int dy, bool start)
        {
            if (dx == int.MinValue) return;
            if (start) dragTop = top;
            int rows = dy / Math.Max(1, S(56));
            top = Math.Max(0, Math.Min(opts.Length - Math.Min(MaxRows, opts.Length), dragTop - rows));
            sel = Math.Max(top, Math.Min(top + MaxRows - 1, sel));
        }
        int dragTop;

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "고르기", () => OnPad(Pad.A)),
            new Hint(Pad.B, "닫기", Shell.Pop),
        };
    }
}
