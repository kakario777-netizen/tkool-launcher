using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>긴 글 보기 창 (런처가 한 일 자세히 등): 줄바꿈해서 보여 주고 위아래로 밀기·패드 위아래, B나 바깥 터치로 닫기</summary>
    class TextScreen : Screen
    {
        Screen under;
        string title, text;
        List<string> lines;
        int top, wrapW = -1, rowsVisible = 1;
        Rectangle box;

        public static void Show(ShellForm sh, string title, string text)
        {
            sh.Push(new TextScreen { under = sh.Cur, title = title, text = text ?? "" });
        }

        public override GameInfo FocusedGame => under?.FocusedGame;

        void Wrap(Graphics g, int w)
        {
            if (wrapW == w && lines != null) return;
            wrapW = w; lines = new List<string>();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Length == 0) { lines.Add(""); continue; }
                string rest = raw;
                while (rest.Length > 0)
                {
                    // 들어가는 만큼 자르되 가능하면 띄어쓰기에서
                    int lo = 1, hi = rest.Length;
                    while (lo < hi) { int mid = (lo + hi + 1) / 2; if (Draw.Measure(g, rest.Substring(0, mid), Draw.Body) <= w) lo = mid; else hi = mid - 1; }
                    int n = lo;
                    if (n < rest.Length) { int sp = rest.LastIndexOf(' ', n - 1, n); if (sp > n / 2) n = sp + 1; }
                    lines.Add(rest.Substring(0, n).TrimEnd());
                    rest = rest.Substring(n).TrimStart();
                }
            }
        }

        public override void Paint(Graphics g)
        {
            if (under != null) { under.Area = Area; under.Paint(g); }
            using (var dim = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillRectangle(dim, Area);
            int w = Math.Min(Area.Width - S(80), S(900)), h = Area.Height - S(60);
            box = new Rectangle(Area.X + (Area.Width - w) / 2, Area.Y + (Area.Height - h) / 2, w, h);
            Draw.Fill(g, box, S(10), Color.FromArgb(255, 34, 40, 50));
            Draw.Text(g, title, Draw.Menu, new Rectangle(box.X + S(22), box.Y + S(12), w - S(44), S(36)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Wrap(g, w - S(56));
            int lh = (int)Math.Ceiling(Draw.Body.GetHeight(g)) + S(4);
            int y0 = box.Y + S(60);
            rowsVisible = Math.Max(1, (box.Bottom - S(16) - y0) / lh);
            top = Math.Max(0, Math.Min(top, Math.Max(0, lines.Count - rowsVisible)));
            for (int k = 0; k < rowsVisible && top + k < lines.Count; k++)
                Draw.Text(g, lines[top + k], Draw.Body, new Rectangle(box.X + S(28), y0 + k * lh, w - S(56), lh), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (lines.Count > rowsVisible)
                Draw.Text(g, (top + 1) + "–" + Math.Min(lines.Count, top + rowsVisible) + " / " + lines.Count + " 줄", Draw.Small,
                    new Rectangle(box.Right - S(220), box.Y + S(12), S(200), S(36)), Theme.SubText, Draw.Right);
        }

        public override void OnPad(Pad b)
        {
            switch (b)
            {
                case Pad.Up: top = Math.Max(0, top - 1); break;
                case Pad.Down: top++; break;
                case Pad.LB: top = Math.Max(0, top - rowsVisible); break;
                case Pad.RB: top += rowsVisible; break;
                case Pad.A: case Pad.B: Shell.Pop(); return;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p) { if (!box.Contains(p)) Shell.Pop(); }

        int dragTop;
        public override void OnDrag(int dx, int dy, bool start)
        {
            if (dx == int.MinValue) return;
            if (start) dragTop = top;
            top = Math.Max(0, dragTop - dy / Math.Max(1, S(28)));
            Shell.Invalidate();
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.LB, "앞 쪽", () => OnPad(Pad.LB)),
            new Hint(Pad.RB, "다음 쪽", () => OnPad(Pad.RB)),
            new Hint(Pad.B, "닫기", Shell.Pop),
        };
    }
}
