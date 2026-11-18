using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>중복으로 보이는 게임 묶음을 하나씩 보여 줌: 버전·날짜·크기·플레이 시간 비교, 폴더 열기, 중복 아님</summary>
    class DupScreen : Screen
    {
        int gi, row;
        Rectangle[] openR = new Rectangle[0], rowR = new Rectangle[0];
        Rectangle notDupR, prevR, nextR;
        static readonly Dictionary<string, long> sizes = new Dictionary<string, long>();

        List<DupFinder.Group> G => DupFinder.Groups;

        string SizeText(GameInfo g)
        {
            string id = GameData.IdOf(g);
            lock (sizes)
            {
                if (sizes.TryGetValue(id, out long v)) return v < 0 ? "…" : v >= 1L << 30 ? (v / (double)(1L << 30)).ToString("0.0") + "GB" : (v >> 20) + "MB";
                sizes[id] = -1;
            }
            Task.Run(() => { long v = DupFinder.FolderSize(g); lock (sizes) sizes[id] = v; try { Shell.BeginInvoke(new Action(Shell.Invalidate)); } catch { } });
            return "…";
        }

        public override void Paint(Graphics g)
        {
            int pad = S(40);
            if (G.Count == 0) { Draw.Text(g, "중복으로 보이는 게임이 없어요.", Draw.Menu, Area, Theme.SubText, Draw.Center); return; }
            gi = Math.Max(0, Math.Min(G.Count - 1, gi));
            var grp = G[gi];
            row = Math.Max(0, Math.Min(grp.Games.Count - 1, row));
            Draw.Text(g, "중복 확인", Theme.Px(20, true), new Rectangle(Area.X + pad, Area.Y + S(10), S(300), S(44)), Theme.Text, Draw.Left);
            Draw.Text(g, (gi + 1) + " / " + G.Count + " 묶음  ·  " + string.Join(" · ", grp.Why), Draw.Body,
                new Rectangle(Area.X + pad + S(200), Area.Y + S(10), Area.Width - pad * 2 - S(200), S(44)), Theme.SubText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            int y = Area.Y + S(64), rh = S(112);
            int n = grp.Games.Count;
            if (openR.Length != n) { openR = new Rectangle[n]; rowR = new Rectangle[n]; }
            for (int i = 0; i < n; i++)
            {
                var gm = grp.Games[i];
                var st = GameData.Get(gm);
                var r = new Rectangle(Area.X + pad, y, Area.Width - pad * 2, rh - S(10));
                rowR[i] = r;
                Draw.Fill(g, r, S(8), Theme.Tile);
                // 작은 그림
                var tr = new Rectangle(r.X + S(10), r.Y + S(10), (r.Height - S(20)) * 4 / 3, r.Height - S(20));
                Draw.Fill(g, tr, S(4), Color.FromArgb(255, 14, 17, 22));
                var th = CoverArt.Thumb(gm);
                if (th != null)
                {
                    float k = Math.Min(tr.Width / (float)th.Width, tr.Height / (float)th.Height);
                    int w = (int)(th.Width * k), h = (int)(th.Height * k);
                    Draw.Image(g, th, new Rectangle(tr.X + (tr.Width - w) / 2, tr.Y + (tr.Height - h) / 2, w, h), S(3));
                }
                int tx = tr.Right + S(16), tw = r.Right - S(190) - tx;
                Draw.Text(g, gm.Title + (i == 0 ? "   ← 가장 새 버전" : ""), Draw.BodyBold, new Rectangle(tx, r.Y + S(8), tw, S(28)), i == 0 ? Color.FromArgb(130, 220, 140) : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                Draw.Text(g, "폴더: " + System.IO.Path.GetFileName(gm.Folder), Draw.Small, new Rectangle(tx, r.Y + S(38), tw, S(22)), Theme.SubText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                string info = (gm.IsTkool ? LibraryScreen.EngineShort(gm) : "기타") + " · " + DupFinder.VersionText(gm) + " · " + SizeText(gm) +
                              " · 플레이 " + GameData.PlayTimeText(st.TotalSeconds) + (st.Favorite ? " · ★" : "") + (st.Cleared ? " · 다 깸" : "");
                Draw.Text(g, info, Draw.Small, new Rectangle(tx, r.Y + S(62), tw, S(22)), Theme.SubText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                openR[i] = new Rectangle(r.Right - S(170), r.Y + (r.Height - S(50)) / 2, S(156), S(50));
                Draw.Fill(g, openR[i], S(6), Color.FromArgb(255, 52, 62, 78));
                Draw.Text(g, "폴더 열기", Draw.BodyBold, openR[i], Theme.Text, Draw.Center);
                if (i == row && Shell.ShowFocus) Draw.FocusGlow(g, r, S(8));
                y += rh;
                if (y > Area.Bottom - S(80)) break;
            }
            // 아래 단추
            int by = Area.Bottom - S(66);
            prevR = new Rectangle(Area.X + pad, by, S(150), S(52));
            nextR = new Rectangle(prevR.Right + S(12), by, S(150), S(52));
            notDupR = new Rectangle(Area.Right - pad - S(240), by, S(240), S(52));
            Draw.Fill(g, prevR, S(6), Theme.Tile); Draw.Text(g, "‹ 이전 묶음", Draw.BodyBold, prevR, Theme.Text, Draw.Center);
            Draw.Fill(g, nextR, S(6), Theme.Tile); Draw.Text(g, "다음 묶음 ›", Draw.BodyBold, nextR, Theme.Text, Draw.Center);
            Draw.Fill(g, notDupR, S(6), Theme.Tile); Draw.Text(g, "이 묶음은 중복 아님", Draw.BodyBold, notDupR, Theme.Text, Draw.Center);
        }

        void NotDup()
        {
            if (G.Count == 0) return;
            PickerScreen.Confirm(Shell, "이 묶음을 중복 아님으로 할까요?", "중복 아님", () =>
            {
                DupFinder.IgnoreGroup(G[gi]);
                row = 0;
                if (G.Count == 0) Shell.Pop();
            }, "다음부터 이 게임들은 같이 묶지 않아요");
        }

        void Go(int d) { if (G.Count == 0) return; gi = (gi + d + G.Count) % G.Count; row = 0; }

        public override void OnPad(Pad b)
        {
            int n = G.Count > 0 ? G[Math.Min(gi, G.Count - 1)].Games.Count : 0;
            switch (b)
            {
                case Pad.Up: row = Math.Max(0, row - 1); break;
                case Pad.Down: row = Math.Min(Math.Max(0, n - 1), row + 1); break;
                case Pad.LB: case Pad.Left: Go(-1); break;
                case Pad.RB: case Pad.Right: Go(1); break;
                case Pad.A: if (n > 0) DupFinder.OpenFolder(G[gi].Games[row]); break;
                case Pad.Y: if (n > 0) Shell.Push(new DetailScreen(G[gi].Games[row])); break;
                case Pad.X: NotDup(); return;
                case Pad.B: Shell.Pop(); return;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            if (prevR.Contains(p)) { Go(-1); Shell.Invalidate(); return; }
            if (nextR.Contains(p)) { Go(1); Shell.Invalidate(); return; }
            if (notDupR.Contains(p)) { NotDup(); return; }
            if (G.Count == 0) return;
            for (int i = 0; i < openR.Length && i < G[gi].Games.Count; i++)
            {
                if (openR[i].Contains(p)) { row = i; DupFinder.OpenFolder(G[gi].Games[i]); return; }
                if (rowR[i].Contains(p)) { row = i; Shell.Push(new DetailScreen(G[gi].Games[i])); return; }
            }
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "폴더 열기", () => OnPad(Pad.A)),
            new Hint(Pad.Y, "게임 정보", () => OnPad(Pad.Y)),
            new Hint(Pad.X, "중복 아님", () => OnPad(Pad.X)),
            new Hint(Pad.RB, "다음 묶음", () => OnPad(Pad.RB)),
            new Hint(Pad.B, "뒤로", Shell.Pop),
        };
    }
}
