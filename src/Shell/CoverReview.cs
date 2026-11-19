using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 한 번에 찾기로 찍은 표지를 쭉 늘어놓고 확인: 아닌 것만 눌러 취소 → [적용]
    /// 한 쪽(12개)씩 적용, 남은 건 그대로 두었다가 나중에 마저 확인
    /// </summary>
    class CoverReviewScreen : Screen
    {
        const int Cols = 4, RowsPerPage = 3, Per = Cols * RowsPerPage;
        List<GameInfo> items = new List<GameInfo>();
        readonly HashSet<GameInfo> rejected = new HashSet<GameInfo>();
        readonly Dictionary<GameInfo, Bitmap> thumbs = new Dictionary<GameInfo, Bitmap>();
        int focus;          // 0..Per-1 = 그림, Per = 적용 단추
        Rectangle[] cells = new Rectangle[0], retakes = new Rectangle[0];
        Rectangle applyR, laterR;

        public CoverReviewScreen() { }

        public override void OnGamesChanged()
        {
            if (Shell == null) return;
            items = GameCapture.Pending(Shell.Games);
        }

        List<GameInfo> Page => items.Take(Per).ToList();

        /// <summary>한 쪽 12개뿐이라 처음 그릴 때 바로 작게 줄여 둠</summary>
        Bitmap ThumbOf(GameInfo g)
        {
            Bitmap small;
            if (thumbs.TryGetValue(g, out small)) return small;
            small = null;
            try
            {
                using (var fs = File.OpenRead(GameCapture.PendingPath(g)))
                using (var src = new Bitmap(fs))
                {
                    float k = Math.Min(480f / src.Width, 360f / src.Height);
                    if (k > 1) k = 1;
                    small = new Bitmap(Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k)));
                    using (var gr = Graphics.FromImage(small)) { gr.InterpolationMode = InterpolationMode.HighQualityBicubic; gr.DrawImage(src, 0, 0, small.Width, small.Height); }
                }
            }
            catch (Exception ex) { Log.Error("찍은 표지 읽기", ex); }
            thumbs[g] = small;
            return small;
        }

        public override void Paint(Graphics g)
        {
            int pad = S(40);
            var page = Page;
            Draw.Text(g, "찍은 표지 확인", Theme.Px(20, true), new Rectangle(Area.X + pad, Area.Y + S(10), S(400), S(44)), Theme.Text, Draw.Left);
            Draw.Text(g, page.Count == 0 ? "" : "아닌 건 눌러서 취소 · 맘에 안 들면 ↻ 다시 찍기 · 다 보고 [적용]  ·  남은 " + items.Count + "개", Draw.Body,
                new Rectangle(Area.X + pad + S(300), Area.Y + S(10), Area.Width - pad * 2 - S(300), S(44)), Theme.SubText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (page.Count == 0)
            {
                Draw.Text(g, "확인할 표지가 없어요.", Draw.Menu, Area, Theme.SubText, Draw.Center);
                cells = new Rectangle[0]; retakes = new Rectangle[0]; applyR = laterR = Rectangle.Empty;
                return;
            }

            int top = Area.Y + S(64), bottom = Area.Bottom - S(84), gap = S(14);
            int cw = (Area.Width - pad * 2 - gap * (Cols - 1)) / Cols;
            int ch = (bottom - top - gap * (RowsPerPage - 1)) / RowsPerPage;
            cells = new Rectangle[page.Count]; retakes = new Rectangle[page.Count];
            for (int i = 0; i < page.Count; i++)
            {
                var gm = page[i];
                var r = new Rectangle(Area.X + pad + (i % Cols) * (cw + gap), top + (i / Cols) * (ch + gap), cw, ch);
                cells[i] = r;
                bool no = rejected.Contains(gm);
                Draw.Fill(g, r, S(8), Theme.Tile);
                // 그림 (비율 그대로, 남는 곳은 검게)
                var ir = new Rectangle(r.X + S(6), r.Y + S(6), r.Width - S(12), r.Height - S(40));
                Draw.Fill(g, ir, S(5), Color.FromArgb(255, 10, 12, 16));
                var th = ThumbOf(gm);
                if (th != null)
                {
                    float k = Math.Min(ir.Width / (float)th.Width, ir.Height / (float)th.Height);
                    int w = (int)(th.Width * k), h = (int)(th.Height * k);
                    Draw.Image(g, th, new Rectangle(ir.X + (ir.Width - w) / 2, ir.Y + (ir.Height - h) / 2, w, h), S(4));
                }
                else Draw.Text(g, "…", Draw.Body, ir, Theme.SubText, Draw.Center);
                if (no)
                {
                    using (var b = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillRectangle(b, ir);
                    var badge = new Rectangle(ir.X + (ir.Width - S(96)) / 2, ir.Y + (ir.Height - S(40)) / 2, S(96), S(40));
                    Draw.Fill(g, badge, S(20), Color.FromArgb(255, 150, 50, 50));
                    Draw.Text(g, "취소", Draw.BodyBold, badge, Color.White, Draw.Center);
                }
                Draw.Text(g, gm.Title, Draw.Small, new Rectangle(r.X + S(8), r.Bottom - S(32), r.Width - S(16), S(28)), no ? Theme.SubText : Theme.Text, Draw.Center);
                // 다시 찍기 단추 (그림 오른쪽 위)
                var rr = new Rectangle(ir.Right - S(104), ir.Y + S(6), S(98), S(34));
                retakes[i] = rr;
                Draw.Fill(g, rr, S(17), Color.FromArgb(220, 30, 34, 44));
                Draw.Text(g, "↻ 다시 찍기", Draw.Small, rr, Color.White, Draw.Center);
                if (i == focus && Shell.ShowFocus) Draw.FocusGlow(g, r, S(8));
            }

            int by = Area.Bottom - S(66);
            int keep = page.Count - page.Count(x => rejected.Contains(x));
            applyR = new Rectangle(Area.Right - pad - S(300), by, S(300), S(52));
            laterR = new Rectangle(Area.X + pad, by, S(220), S(52));
            Draw.Fill(g, applyR, S(6), Theme.Accent);
            Draw.Text(g, "적용 (" + keep + "개 표지로)", Draw.BodyBold, applyR, Color.White, Draw.Center);
            if (focus == Per && Shell.ShowFocus) Draw.FocusGlow(g, applyR, S(6));
            Draw.Fill(g, laterR, S(6), Theme.Tile);
            Draw.Text(g, "나중에 마저 확인", Draw.BodyBold, laterR, Theme.Text, Draw.Center);
        }

        void Toggle(GameInfo gm) { if (!rejected.Remove(gm)) rejected.Add(gm); }

        /// <summary>이 게임만 다시 찍기 — 켜서 Y 두 번, 찍은 그림이 이 칸에 바뀌어 들어옴 (적용은 마지막에 한꺼번에)</summary>
        void Retake(GameInfo gm)
        {
            if (GameWatch.Running) { Shell.ShowToast("게임을 끈 다음에 해 주세요", 2500); return; }
            Shell.RetakeCover(gm, () =>
            {
                try
                {
                    Shell.BeginInvoke(new Action(() =>
                    {
                        Bitmap ob; if (thumbs.TryGetValue(gm, out ob)) ob?.Dispose();
                        thumbs.Remove(gm); rejected.Remove(gm);
                        Shell.Bring(); Shell.Activate();
                        Shell.Invalidate();
                    }));
                }
                catch { }
            });
        }

        void Apply()
        {
            var page = Page;
            if (page.Count == 0) return;
            int ok = 0;
            foreach (var gm in page)
            {
                if (rejected.Contains(gm)) GameCapture.Reject(gm);
                else { GameCapture.Accept(gm); ok++; }
                Bitmap ob; if (thumbs.TryGetValue(gm, out ob)) ob?.Dispose(); thumbs.Remove(gm);
                rejected.Remove(gm);
            }
            Log.Info("찍은 표지 확인: " + ok + "개 적용, " + (page.Count - ok) + "개 취소");
            items = GameCapture.Pending(Shell.Games);
            focus = 0;
            Shell.RefreshLists();
            Shell.ShowToast(ok + "개를 표지로 넣었어요" + (items.Count > 0 ? " · 남은 " + items.Count + "개" : ""), 3000);
            if (items.Count == 0) Shell.Pop();
        }

        public override void OnPad(Pad b)
        {
            int n = Page.Count;
            switch (b)
            {
                case Pad.Left: if (focus < Per && focus % Cols > 0) focus--; break;
                case Pad.Right: if (focus < Per && focus % Cols < Cols - 1 && focus + 1 < n) focus++; break;
                case Pad.Up: if (focus == Per) focus = Math.Max(0, n - 1); else if (focus >= Cols) focus -= Cols; break;
                case Pad.Down: if (focus < Per) { if (focus + Cols < n) focus += Cols; else focus = Per; } break;
                case Pad.A:
                    if (focus == Per) { Apply(); return; }
                    if (focus < n) Toggle(Page[focus]);
                    break;
                case Pad.X: Apply(); return;
                case Pad.Y: if (focus < n) Retake(Page[focus]); return;
                case Pad.B: Shell.Pop(); return;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            if (applyR.Contains(p)) { Apply(); return; }
            if (laterR.Contains(p)) { Shell.Pop(); return; }
            var page = Page;
            for (int i = 0; i < retakes.Length && i < page.Count; i++)
                if (retakes[i].Contains(p)) { focus = i; Retake(page[i]); return; }
            for (int i = 0; i < cells.Length && i < page.Count; i++)
                if (cells[i].Contains(p)) { focus = i; Toggle(page[i]); Shell.Invalidate(); return; }
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "취소/되돌리기", () => OnPad(Pad.A)),
            new Hint(Pad.Y, "다시 찍기", () => OnPad(Pad.Y)),
            new Hint(Pad.X, "적용", Apply),
            new Hint(Pad.B, "나중에", Shell.Pop),
        };
    }
}
