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
        Rectangle[] openR = new Rectangle[0], rowR = new Rectangle[0], delR = new Rectangle[0];
        Rectangle notDupR, prevR, nextR;
        static readonly Dictionary<string, DupFinder.Detail> details = new Dictionary<string, DupFinder.Detail>();
        int top;   // 묶음이 길면 위아래로 밀기

        readonly bool onlyNew;
        public DupScreen(bool onlyNew = false) { this.onlyNew = onlyNew; }
        List<DupFinder.Group> G => onlyNew ? LibraryScreen.NewDupGroups() : DupFinder.Groups;
        Rectangle[] oldR = new Rectangle[0];

        DupFinder.Detail DetailOf(GameInfo g)
        {
            string id = GameData.IdOf(g);
            lock (details)
            {
                DupFinder.Detail d;
                if (details.TryGetValue(id, out d)) return d;
                details[id] = null;
            }
            Task.Run(() => { var v = DupFinder.GetDetail(g); lock (details) details[id] = v; try { Shell.BeginInvoke(new Action(Shell.Invalidate)); } catch { } });
            return null;
        }

        static string Size(long v) => v >= 1L << 30 ? (v / (double)(1L << 30)).ToString("0.0") + "GB" : (v >> 20) + "MB";
        static string Day(DateTime t) => t > DateTime.MinValue ? t.ToString("yyyy.MM.dd") : "-";
        const TextFormatFlags One = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        const TextFormatFlags PathF = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix;

        public override void Paint(Graphics g)
        {
            int pad = S(40);
            if (G.Count == 0) { Draw.Text(g, "중복으로 보이는 게임이 없어요.", Draw.Menu, Area, Theme.SubText, Draw.Center); return; }
            gi = Math.Max(0, Math.Min(G.Count - 1, gi));
            var grp = G[gi];
            row = Math.Max(0, Math.Min(grp.Games.Count - 1, row));
            Draw.Text(g, onlyNew ? "새 게임 중복·버전" : "중복 확인", Theme.Px(20, true), new Rectangle(Area.X + pad, Area.Y + S(10), S(300), S(44)), Theme.Text, Draw.Left);
            Draw.Text(g, (gi + 1) + " / " + G.Count + " 묶음  ·  " + string.Join(" · ", grp.Why), Draw.Body,
                new Rectangle(Area.X + pad + S(200), Area.Y + S(10), Area.Width - pad * 2 - S(200), S(44)), Theme.SubText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            int rh = S(176); lastRh = rh; int listTop = Area.Y + S(64), listBottom = Area.Bottom - S(80);
            int n = grp.Games.Count, fit = Math.Max(1, (listBottom - listTop) / rh);
            if (row < top) top = row; if (row >= top + fit) top = row - fit + 1;
            top = Math.Max(0, Math.Min(top, Math.Max(0, n - fit)));
            if (openR.Length != n) { openR = new Rectangle[n]; rowR = new Rectangle[n]; delR = new Rectangle[n]; oldR = new Rectangle[n]; }
            for (int i = 0; i < n; i++) { openR[i] = rowR[i] = delR[i] = oldR[i] = Rectangle.Empty; }
            // 묶음 안에서 가장 큰/세이브 최근 표시용
            var ds = grp.Games.Select(DetailOf).ToList();
            long maxSize = ds.Where(x => x != null).Select(x => x.Size).DefaultIfEmpty(0).Max();
            DateTime maxSave = ds.Where(x => x != null).Select(x => x.LastSave).DefaultIfEmpty(DateTime.MinValue).Max();
            int y = listTop;
            for (int i = top; i < n && i < top + fit; i++)
            {
                var gm = grp.Games[i];
                var st = GameData.Get(gm);
                var dt = ds[i];
                var r = new Rectangle(Area.X + pad, y, Area.Width - pad * 2, rh - S(10));
                rowR[i] = r;
                Draw.Fill(g, r, S(8), Theme.Tile);
                // 작은 그림 (4:3)
                int thH = S(108);
                var tr = new Rectangle(r.X + S(12), r.Y + S(12), thH * 4 / 3, thH);
                Draw.Fill(g, tr, S(4), Color.FromArgb(255, 14, 17, 22));
                var th = CoverArt.Thumb(gm);
                if (th != null)
                {
                    float k = Math.Min(tr.Width / (float)th.Width, tr.Height / (float)th.Height);
                    int w = (int)(th.Width * k), h = (int)(th.Height * k);
                    Draw.Image(g, th, new Rectangle(tr.X + (tr.Width - w) / 2, tr.Y + (tr.Height - h) / 2, w, h), S(3));
                }
                // 단추 (오른쪽 세로로)
                openR[i] = new Rectangle(r.Right - S(170), r.Y + S(12), S(156), S(44));
                oldR[i] = new Rectangle(openR[i].X, openR[i].Bottom + S(6), S(156), S(44));
                delR[i] = new Rectangle(openR[i].X, oldR[i].Bottom + S(6), S(156), S(44));
                Draw.Fill(g, openR[i], S(6), Color.FromArgb(255, 52, 62, 78));
                Draw.Text(g, "폴더 열기", Draw.BodyBold, openR[i], Theme.Text, Draw.Center);
                Draw.Fill(g, oldR[i], S(6), Color.FromArgb(255, 150, 112, 20));
                Draw.Text(g, "_old로 옮기기", Draw.BodyBold, oldR[i], Color.White, Draw.Center);
                Draw.Fill(g, delR[i], S(6), Color.FromArgb(255, 120, 45, 45));
                Draw.Text(g, "삭제", Draw.BodyBold, delR[i], Color.White, Draw.Center);

                int tx = tr.Right + S(16), tw = openR[i].X - S(14) - tx, ly = r.Y + S(8), lh = S(26);
                bool isNew = NewInbox.IsNew(gm);
                Draw.Text(g, (isNew ? "[새로 들어옴] " : "[원래 있던 것] ") + gm.Title + (i == 0 ? "   ← 가장 새 버전" : ""), Draw.BodyBold, new Rectangle(tx, ly, tw, S(30)),
                    i == 0 ? Color.FromArgb(130, 220, 140) : isNew ? Color.FromArgb(238, 196, 60) : Theme.Text, One);
                ly += S(32);
                // 경로 전체
                Draw.Text(g, gm.Folder ?? "", Draw.Small, new Rectangle(tx, ly, tw, lh), Color.FromArgb(150, 190, 235), PathF);
                ly += lh;
                // 엔진·버전·파일 날짜·크기·파일 수
                string verName = DupFinder.VersionText(gm);
                string l3 = (gm.IsTkool ? LibraryScreen.EngineShort(gm) : (string.IsNullOrEmpty(gm.Engine) ? "기타" : gm.Engine)) +
                            (verName.StartsWith("v") ? " · " + verName : "") +
                            " · 데이터 " + Day(DupFinder.DataDate(gm)) +
                            (dt == null ? " · 크기 계산 중…" : " · " + Size(dt.Size) + (dt.Size == maxSize && n > 1 ? "(가장 큼)" : "") + " · 파일 " + dt.Files.ToString("#,0") + "개" +
                                (dt.Newest > DateTime.MinValue ? " · 마지막 수정 " + Day(dt.Newest) : ""));
                Draw.Text(g, l3, Draw.Small, new Rectangle(tx, ly, tw, lh), Theme.SubText, One);
                ly += lh;
                // 세이브·플레이 기록
                string l4 = dt == null ? "세이브 확인 중…" : dt.Saves == 0 ? "세이브 없음" :
                            "세이브 " + dt.Saves + "개 (마지막 " + Day(dt.LastSave) + (dt.LastSave == maxSave && n > 1 ? ", 가장 최근" : "") + ")";
                l4 += " · 플레이 " + GameData.PlayTimeText(st.TotalSeconds) + (st.LastPlayed > 0 ? " · 마지막 플레이 " + Day(new DateTime(st.LastPlayed)) : " · 안 해봄") +
                      (st.Cleared ? " · 다 깸" : "") + (st.Favorite ? " · ★" : "");
                Draw.Text(g, l4, Draw.Small, new Rectangle(tx, ly, tw, lh), dt != null && dt.Saves > 0 ? Color.FromArgb(235, 205, 120) : Theme.SubText, One);
                ly += lh;
                // 언어·꼬리표·패치 파일·원래 이름
                var bits = new List<string> { LibraryScreen.IsKorean(gm) ? "한글" : "일본어" };
                bits.AddRange(gm.Tags.Where(t => t != "한글"));
                if (dt != null && dt.Extras.Count > 0) bits.Add("패치 파일: " + string.Join(", ", dt.Extras));
                if (!string.IsNullOrEmpty(gm.RawTitle) && gm.RawTitle != gm.Title) bits.Add("원래 이름: " + gm.RawTitle);
                bits.Add("폴더 이름: " + GameScanner.FolderName(gm.Folder));
                Draw.Text(g, string.Join(" · ", bits), Draw.Small, new Rectangle(tx, ly, tw, lh), Theme.SubText, One);

                if (i == row && Shell.ShowFocus) Draw.FocusGlow(g, r, S(8));
                y += rh;
            }
            if (n > fit)
                Draw.Text(g, (top + 1) + "~" + Math.Min(n, top + fit) + " / " + n + "개 (위아래로 넘기기)", Draw.Small,
                    new Rectangle(Area.X + pad, listBottom - S(4), Area.Width - pad * 2, S(22)), Theme.SubText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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

        void Delete(GameInfo gm)
        {
            PickerScreen.Confirm(Shell, "'" + gm.Title + "' 폴더를 삭제할까요?", "삭제 (휴지통으로)", () =>
            {
                string err;
                if (DupFinder.DeleteToRecycle(gm, out err))
                {
                    Shell.ShowToast("폴더를 휴지통으로 옮겼어요 (휴지통에서 되살릴 수 있어요)", 3500);
                    row = 0;
                    Shell.RefreshLists();
                    if (G.Count == 0) Shell.Pop();
                }
                else Shell.ShowToast("지우지 못했어요: " + err, 4000);
            }, "폴더를 삭제합니다: " + gm.Folder);
        }

        void MoveOld(GameInfo gm)
        {
            var grp = G[gi];
            var newer = grp.Games.FirstOrDefault(x => x != gm);
            if (newer == null) return;
            var dt = DetailOf(gm);
            string warn = grp.Games.IndexOf(gm) == 0 ? "이 게임이 가장 새 버전으로 보여요. " : "";
            PickerScreen.Confirm(Shell, "'" + gm.Title + "'을(를) _old 폴더로 옮길까요?", "_old로 옮기기", () => Shell.BeginInvoke(new Action(() =>
            {
                bool hasSave = dt == null || dt.Saves > 0;
                Action<bool> go = copy => Task.Run(() =>
                {
                    string msg;
                    bool ok = NewInbox.MoveToOld(gm, newer, copy, Shell.Games, out msg);
                    try
                    {
                        Shell.BeginInvoke(new Action(() =>
                        {
                            Shell.ShowToast(msg, 4500);
                            if (ok) { row = 0; Shell.Rescan(); if (G.Count == 0) Shell.Pop(); }
                            Shell.Invalidate();
                        }));
                    }
                    catch { }
                });
                if (!hasSave) { go(false); return; }
                PickerScreen.Show(Shell, "세이브를 '" + newer.Title + "'(으)로 복사할까요?", new[] { "세이브 복사하기", "복사 안 함" }, 0, k => go(k == 0),
                    "같은 이름 세이브가 이미 있으면 새 버전 폴더 _save_backup에 남겨 둬요");
            })), warn + "지우지 않고 게임 폴더\\_old 안으로 옮겨요 · " + gm.Folder);
        }

        void Menu(GameInfo gm)
        {
            PickerScreen.Show(Shell, gm.Title, new[] { "폴더 열기", "옛 버전 → _old로 옮기기", "삭제 (휴지통)", "상세 보기", "닫기" }, 0, k =>
            {
                if (k == 0) DupFinder.OpenFolder(gm);
                else if (k == 1) Shell.BeginInvoke(new Action(() => MoveOld(gm)));
                else if (k == 2) Shell.BeginInvoke(new Action(() => Delete(gm)));
                else if (k == 3) Shell.BeginInvoke(new Action(() => Shell.Push(new DetailScreen(gm))));
            });
        }

        void Go(int d) { if (G.Count == 0) return; gi = (gi + d + G.Count) % G.Count; row = 0; top = 0; }

        public override void OnPad(Pad b)
        {
            int n = G.Count > 0 ? G[Math.Min(gi, G.Count - 1)].Games.Count : 0;
            switch (b)
            {
                case Pad.Up: row = Math.Max(0, row - 1); break;
                case Pad.Down: row = Math.Min(Math.Max(0, n - 1), row + 1); break;
                case Pad.LB: case Pad.Left: Go(-1); break;
                case Pad.RB: case Pad.Right: Go(1); break;
                case Pad.A: if (n > 0) Menu(G[gi].Games[row]); return;
                case Pad.X: if (n > 0) Delete(G[gi].Games[row]); return;
                case Pad.Y: NotDup(); return;
                case Pad.B: Shell.Pop(); return;
            }
            Shell.Invalidate();
        }

        int dragTop, lastRh = 1;
        public override void OnDrag(int dx, int dy, bool start)
        {
            if (dx == int.MinValue) return;
            if (start) dragTop = top;
            int n = G.Count > 0 ? G[Math.Min(gi, G.Count - 1)].Games.Count : 0;
            top = Math.Max(0, Math.Min(Math.Max(0, n - 1), dragTop - dy / Math.Max(1, lastRh)));
            row = top;   // 보이는 첫 줄로 (그리기에서 포커스가 화면 안에 있게 맞춤)
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
                if (delR[i].Contains(p)) { row = i; Delete(G[gi].Games[i]); return; }
                if (oldR[i].Contains(p)) { row = i; MoveOld(G[gi].Games[i]); return; }
                if (rowR[i].Contains(p)) { row = i; Shell.Push(new DetailScreen(G[gi].Games[i])); return; }
            }
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "할 일", () => OnPad(Pad.A)),
            new Hint(Pad.X, "삭제", () => OnPad(Pad.X)),
            new Hint(Pad.Y, "중복 아님", () => OnPad(Pad.Y)),
            new Hint(Pad.RB, "다음 묶음", () => OnPad(Pad.RB)),
            new Hint(Pad.B, "뒤로", Shell.Pop),
        };
    }
}
