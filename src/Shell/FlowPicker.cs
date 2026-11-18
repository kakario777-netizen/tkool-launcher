using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 태그·엔진 고르기 창: 동그란 단추를 줄줄이 늘어놓고(넘치면 위아래로 밀기) 하나 누르면 그걸로 거름.
    /// 맨 앞 "모두 보기"는 거르기 풀기.
    /// </summary>
    class FlowPicker : Screen
    {
        Screen under;
        string title;
        List<string> names; List<int> counts;
        int sel, cur;
        Action<string> pick;     // null = 풀기
        Rectangle box, list;
        Rectangle[] rects = new Rectangle[0];
        int scroll, contentH, dragStart;
        static readonly Color Orange = Color.FromArgb(255, 214, 120, 30);

        public static void Show(ShellForm sh, string title, List<KeyValuePair<string, int>> items, string current, Action<string> pick)
        {
            var p = new FlowPicker { under = sh.Cur, title = title, pick = pick };
            p.names = new List<string> { "모두 보기" };
            p.counts = new List<int> { -1 };
            foreach (var kv in items) { p.names.Add(kv.Key); p.counts.Add(kv.Value); }
            p.cur = current == null ? 0 : Math.Max(0, p.names.IndexOf(current));
            p.sel = p.cur;
            sh.Push(p);
        }

        public override GameInfo FocusedGame => under?.FocusedGame;

        string Label(int i) => counts[i] < 0 ? names[i] : names[i] + "  " + counts[i];

        public override void Paint(Graphics g)
        {
            if (under != null) { under.Area = Area; under.Paint(g); }
            using (var dim = new SolidBrush(Color.FromArgb(180, 0, 0, 0))) g.FillRectangle(dim, Area);
            int w = Math.Min(Area.Width - S(80), S(900));
            int lw = w - S(40);
            var font = Theme.Px(15, true);
            int ph = S(44), gap = S(10);
            if (rects.Length != names.Count) rects = new Rectangle[names.Count];
            int x = 0, y = 0;
            for (int i = 0; i < names.Count; i++)
            {
                int pw = Math.Min(lw, Draw.Measure(g, Label(i), font) + S(36));
                if (x > 0 && x + pw > lw) { x = 0; y += ph + gap; }
                rects[i] = new Rectangle(x, y, pw, ph);   // list 기준 위치 (스크롤 전)
                x += pw + gap;
            }
            contentH = y + ph;
            // 창 높이는 내용만큼 (넘치면 화면 안에서 밀기)
            int h = Math.Min(Area.Height - S(60), S(76) + contentH + S(20));
            box = new Rectangle(Area.X + (Area.Width - w) / 2, Area.Y + (Area.Height - h) / 2, w, h);
            Draw.Fill(g, box, S(12), Color.FromArgb(255, 30, 36, 46));
            Draw.Text(g, title, Draw.Menu, new Rectangle(box.X + S(24), box.Y + S(12), w - S(48), S(40)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            list = new Rectangle(box.X + S(20), box.Y + S(60), lw, h - S(76));
            // 고른 것이 보이게
            if (sel >= 0 && sel < rects.Length)
            {
                if (rects[sel].Y < scroll) scroll = rects[sel].Y;
                if (rects[sel].Bottom > scroll + list.Height) scroll = rects[sel].Bottom - list.Height;
            }
            scroll = Math.Max(0, Math.Min(Math.Max(0, contentH - list.Height), scroll));

            var st = g.Save();
            g.SetClip(list, System.Drawing.Drawing2D.CombineMode.Intersect);
            for (int i = 0; i < names.Count; i++)
            {
                var r = Screenify(rects[i]);
                if (r.Bottom < list.Y || r.Y > list.Bottom) continue;
                bool on = i == cur;
                if (on) Draw.Fill(g, r, ph / 2, Orange);
                else Draw.Fill(g, r, ph / 2, Color.FromArgb(255, 48, 56, 68));
                Draw.Text(g, Label(i), font, r, on ? Color.White : Theme.Text, Draw.Center);
                if (i == sel && Shell.ShowFocus) Draw.FocusGlow(g, r, ph / 2);
            }
            g.Restore(st);
            if (contentH > list.Height)
            {
                int bh = Math.Max(S(30), list.Height * list.Height / contentH);
                int by = list.Y + (list.Height - bh) * scroll / Math.Max(1, contentH - list.Height);
                Draw.Fill(g, new Rectangle(box.Right - S(10), by, S(4), bh), S(2), Color.FromArgb(120, 255, 255, 255));
            }
        }

        Rectangle Screenify(Rectangle r) => new Rectangle(list.X + r.X, list.Y + r.Y - scroll, r.Width, r.Height);

        void Choose(int i)
        {
            Shell.Pop();
            try { pick?.Invoke(i <= 0 ? null : names[i]); } catch (Exception ex) { Log.Error("태그 고르기", ex); }
            Shell.Invalidate();
        }

        /// <summary>위·아래 줄에서 가로 위치가 가장 가까운 단추</summary>
        int Vertical(int dir)
        {
            if (sel < 0 || sel >= rects.Length) return 0;
            var me = rects[sel];
            int cx = me.X + me.Width / 2, best = sel, bestD = int.MaxValue, rowY = -1;
            for (int i = 0; i < rects.Length; i++)
            {
                int dy = rects[i].Y - me.Y;
                if (dir > 0 ? dy <= 0 : dy >= 0) continue;
                if (rowY < 0 || (dir > 0 ? rects[i].Y < rowY : rects[i].Y > rowY)) { rowY = rects[i].Y; bestD = int.MaxValue; }
                if (rects[i].Y != rowY) continue;
                int d = Math.Abs(rects[i].X + rects[i].Width / 2 - cx);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public override void OnPad(Pad b)
        {
            switch (b)
            {
                case Pad.Left: sel = Math.Max(0, sel - 1); break;
                case Pad.Right: sel = Math.Min(names.Count - 1, sel + 1); break;
                case Pad.Up: sel = Vertical(-1); break;
                case Pad.Down: sel = Vertical(1); break;
                case Pad.A: Choose(sel); return;
                case Pad.X: Choose(0); return;
                case Pad.B: Shell.Pop(); return;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            if (!box.Contains(p)) { Shell.Pop(); return; }
            if (!list.Contains(p)) return;
            for (int i = 0; i < rects.Length; i++) if (Screenify(rects[i]).Contains(p)) { Choose(i); return; }
        }

        public override void OnDrag(int dx, int dy, bool start)
        {
            if (dx == int.MinValue) return;
            if (start) dragStart = scroll;
            scroll = Math.Max(0, Math.Min(Math.Max(0, contentH - list.Height), dragStart - dy));
            sel = -1;
            Shell.Invalidate();
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "고르기", () => OnPad(Pad.A)),
            new Hint(Pad.X, "모두 보기", () => Choose(0)),
            new Hint(Pad.B, "닫기", Shell.Pop),
        };
    }
}
