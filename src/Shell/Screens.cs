using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TkoolLauncher
{
    class Hint
    {
        public Pad Button;
        public string Label;
        public Action Act;
        public Rectangle Bounds;
        public Hint(Pad b, string label, Action act) { Button = b; Label = label; Act = act; }
    }

    abstract class Screen
    {
        public ShellForm Shell;
        public Rectangle Area;          // 위·아래 줄 사이
        public virtual GameInfo FocusedGame => null;
        public virtual void Paint(Graphics g) { }
        public virtual bool Tick(float dt) => false;    // 아직 움직이는 중이면 true
        public virtual void OnPad(Pad b) { }
        public virtual void OnTap(Point p) { }
        public virtual void OnDrag(int dx, int dy, bool start) { }
        public virtual void OnGamesChanged() { }
        public virtual List<Hint> Hints() => new List<Hint>();
        protected int S(float v) => Theme.S(v);
    }

    // ═════════════════════ 홈: 최근 게임 선반 ═════════════════════
    class HomeScreen : Screen
    {
        List<GameInfo> items = new List<GameInfo>();
        int focus;
        float scroll, target, dragStart;
        bool dragging;
        Rectangle emptyBtn;

        public override GameInfo FocusedGame => focus < items.Count ? items[focus] : null;

        public override void OnGamesChanged()
        {
            var cur = FocusedGame;
            items = Shell.Recent(15);
            int i = cur != null ? items.FindIndex(x => x.Exe == cur.Exe) : -1;
            focus = i >= 0 ? i : 0;
            Follow();
        }

        int Count => items.Count == 0 ? 0 : items.Count + 1; // 끝에 '라이브러리' 칸
        int CapH => (int)(Area.Height * 0.5f);
        int CapW => CapH * 2 / 3;
        int Gap => S(16);
        int Pad0 => S(40);

        void Follow()
        {
            target = Math.Max(0, focus * (CapW + Gap) - (CapW + Gap) * 0.0f);
            float max = Math.Max(0, Count * (CapW + Gap) - (Area.Width - Pad0 * 2));
            if (target > max) target = max;
            Shell.Animate();
        }

        Rectangle CapRect(int i)
        {
            int y = Area.Top + S(64);
            int x = (int)(Area.Left + Pad0 + i * (CapW + Gap) - scroll);
            return new Rectangle(x, y, CapW, CapH);
        }

        public override bool Tick(float dt)
        {
            if (dragging) return false;
            float before = scroll;
            scroll = Draw.Lerp(scroll, target, dt * 14f);
            if (Math.Abs(scroll - target) < 0.5f) scroll = target;
            return Math.Abs(scroll - before) > 0.01f;
        }

        public override void Paint(Graphics g)
        {
            if (items.Count == 0)
            {
                var r = new Rectangle(Area.X, Area.Y + Area.Height / 2 - S(80), Area.Width, S(40));
                string msg = Shell.Scanning ? "게임을 찾고 있어요..." :
                    Settings.GameFolders.Count == 0 ? "게임 폴더를 먼저 골라 주세요" : "등록한 폴더에서 게임을 못 찾았어요";
                Draw.Text(g, msg, Draw.H2, r, Theme.Text, Draw.Center);
                if (!Shell.Scanning)
                {
                    emptyBtn = new Rectangle(Area.X + Area.Width / 2 - S(120), r.Bottom + S(24), S(240), S(52));
                    Draw.Fill(g, emptyBtn, S(6), Theme.Accent);
                    Draw.Text(g, "게임 폴더 설정", Draw.BodyBold, emptyBtn, Color.White, Draw.Center);
                }
                return;
            }

            Draw.Text(g, "최근 게임", Draw.H2, new Rectangle(Area.Left + Pad0, Area.Top + S(18), S(400), S(32)), Theme.Text);

            var oldClip = g.Clip;
            for (int i = 0; i < Count; i++)
            {
                var r = CapRect(i);
                if (r.Right < -S(40) || r.Left > Area.Right + S(40)) continue;
                bool f = i == focus;
                if (f) r = Rectangle.Inflate(r, S(6), (int)(S(6) * 1.5f));
                if (i < items.Count)
                {
                    var art = CoverArt.Get(items[i]);
                    if (art?.Capsule != null) Draw.Image(g, art.Capsule, r, S(4));
                    else
                    {
                        Draw.Fill(g, r, S(4), Theme.Tile);
                        Draw.Text(g, items[i].Title, Draw.BodyBold, Rectangle.Inflate(r, -S(12), -S(12)), Theme.SubText, Draw.Wrap);
                    }
                }
                else
                {
                    Draw.Fill(g, r, S(4), Color.FromArgb(160, 35, 41, 51));
                    // 격자 아이콘
                    int cs = S(14), gx = r.X + r.Width / 2 - cs * 3 / 2 - S(4), gy = r.Y + r.Height / 2 - S(46);
                    using (var b = new SolidBrush(Theme.SubText))
                        for (int yy = 0; yy < 3; yy++) for (int xx = 0; xx < 3; xx++)
                            g.FillRectangle(b, gx + xx * (cs + S(4)), gy + yy * (cs + S(4)), cs, cs);
                    Draw.Text(g, "라이브러리", Draw.BodyBold, new Rectangle(r.X, gy + S(66), r.Width, S(24)), Theme.Text, Draw.Center);
                    Draw.Text(g, "게임 " + Shell.Games.Count + "개", Draw.Small, new Rectangle(r.X, gy + S(92), r.Width, S(20)), Theme.SubText, Draw.Center);
                }
                if (f && Shell.ShowFocus) Draw.FocusGlow(g, r, S(4));
            }

            // 선반 아래: 고른 게임 정보
            var gm = FocusedGame;
            int infoY = CapRect(0).Bottom + S(30);
            if (gm != null)
            {
                var st = GameData.Get(gm);
                Draw.Text(g, gm.Title, Draw.H1, new Rectangle(Area.Left + Pad0, infoY, Area.Width - Pad0 * 2, S(36)), Theme.Text);
                int x = Area.Left + Pad0, y = infoY + S(44);
                Draw.Badge(g, ref x, y, gm.VersionLabel, Theme.GameColor(gm));
                if (gm.Japanese) Draw.Badge(g, ref x, y, "日本語", Color.FromArgb(186, 62, 62));
                if (st.Favorite) Draw.Badge(g, ref x, y, "★ 즐겨찾기", Color.FromArgb(160, 120, 20));
                string info = "마지막 플레이  " + GameData.LastPlayedText(st.LastPlayed) + "      플레이 시간  " + GameData.PlayTimeText(st.TotalSeconds);
                Draw.Text(g, info, Draw.Body, new Rectangle(x + S(10), y, Area.Right - x - Pad0, S(22)), Theme.SubText);
            }
            else if (focus == items.Count)
            {
                Draw.Text(g, "모든 게임 보기", Draw.H1, new Rectangle(Area.Left + Pad0, infoY, Area.Width, S(36)), Theme.Text);
            }
        }

        public override void OnPad(Pad b)
        {
            if (items.Count == 0) { if (b == Pad.A) Shell.OpenFolders(); return; }
            switch (b)
            {
                case Pad.Left: if (focus > 0) { focus--; Follow(); Shell.FocusChanged(); } break;
                case Pad.Right: if (focus < Count - 1) { focus++; Follow(); Shell.FocusChanged(); } break;
                case Pad.A: Open(focus); break;
                case Pad.Y: Shell.Push(new LibraryScreen()); break;
                case Pad.X: Shell.Push(new SearchScreen()); break;
            }
        }

        void Open(int i)
        {
            if (i < items.Count) Shell.Push(new DetailScreen(items[i]));
            else if (i == items.Count) Shell.Push(new LibraryScreen());
        }

        public override void OnTap(Point p)
        {
            if (items.Count == 0) { if (emptyBtn.Contains(p)) Shell.OpenFolders(); return; }
            for (int i = 0; i < Count; i++)
                if (CapRect(i).Contains(p))
                {
                    focus = i;
                    Shell.FocusChanged();
                    Open(i);
                    return;
                }
        }

        public override void OnDrag(int dx, int dy, bool start)
        {
            if (start) { dragStart = scroll; dragging = true; }
            if (dx == int.MinValue) { dragging = false; target = scroll; Clamp(); return; }
            scroll = dragStart - dx;
            float max = Math.Max(0, Count * (CapW + Gap) - (Area.Width - Pad0 * 2));
            if (scroll < -S(60)) scroll = -S(60);
            if (scroll > max + S(60)) scroll = max + S(60);
        }

        void Clamp()
        {
            float max = Math.Max(0, Count * (CapW + Gap) - (Area.Width - Pad0 * 2));
            target = Math.Max(0, Math.Min(max, scroll));
            Shell.Animate();
        }

        public override List<Hint> Hints()
        {
            var h = new List<Hint>();
            if (items.Count == 0) { h.Add(new Hint(Pad.A, "폴더 설정", Shell.OpenFolders)); return h; }
            h.Add(new Hint(Pad.A, "열기", () => Open(focus)));
            h.Add(new Hint(Pad.X, "검색", () => Shell.Push(new SearchScreen())));
            h.Add(new Hint(Pad.Y, "라이브러리", () => Shell.Push(new LibraryScreen())));
            h.Add(new Hint(Pad.Back, "메뉴", Shell.OpenMenu));
            return h;
        }
    }

    // ═════════════════════ 라이브러리: 탭 + 격자 ═════════════════════
    class LibraryScreen : Screen
    {
        class Tab { public string Name; public Func<GameInfo, bool> Filter; }
        static string lastTab = "전체";
        List<Tab> tabs = new List<Tab>();
        int tab;
        List<GameInfo> items = new List<GameInfo>();
        int focus;
        float scroll, target, dragStart;
        bool dragging;
        Rectangle[] tabRects = new Rectangle[0];

        // ── 쯔꾸르 탭: 가로 카드 + 분류 동그라미 줄(칩) ──
        class Chip { public string Name; public Func<GameInfo, bool> Filter; }
        List<Chip> chips = new List<Chip>();
        static string chipSel = "전체";
        static int sortMode;                 // 0 이름순 1 최근 추가 2 플레이 시간
        static readonly string[] SortNames = { "이름순", "최근 추가", "플레이 시간" };
        bool chipFocus;
        int chipIdx, extraSel;   // extraSel: 0 칩, 1 정리 모드, 2 정렬
        Rectangle[] chipRects = new Rectangle[0];
        Rectangle sortRect, tidyRect;
        bool Wide => tab < tabs.Count && tabs[tab].Name == "쯔꾸르";
        int ChipH => Wide ? S(58) : 0;
        int BandH => S(58);

        public static string EngineShort(GameInfo g)
        {
            switch (g.Version)
            {
                case TkoolVersion.RM2000: return "2000";
                case TkoolVersion.XP: return "XP";
                case TkoolVersion.VX: return "VX";
                case TkoolVersion.VXAce: return "Ace";
                case TkoolVersion.MV: return "MV";
                case TkoolVersion.MZ: return "MZ";
            }
            return "";
        }

        public static Color EngineColor(GameInfo g)
        {
            if (!g.IsTkool) return Color.FromArgb(110, 120, 135);
            switch (g.Version)
            {
                case TkoolVersion.RM2000: return Color.FromArgb(228, 140, 60);
                case TkoolVersion.XP: return Color.FromArgb(96, 176, 92);
                case TkoolVersion.VX: return Color.FromArgb(70, 145, 225);
                case TkoolVersion.VXAce: return Color.FromArgb(80, 100, 205);
                case TkoolVersion.MV: return Color.FromArgb(160, 105, 225);
                default: return Color.FromArgb(225, 95, 155);
            }
        }

        public static bool IsKorean(GameInfo g) => g.Tags.Contains("한글") || !g.Japanese;

        /// <summary>0 안 해봄 1 하는 중 2 다 깸</summary>
        public static int Status(GameInfo g) { var st = GameData.Get(g); return st.Cleared ? 2 : st.LastPlayed > 0 ? 1 : 0; }

        void BuildChips(List<GameInfo> pool)
        {
            chips = new List<Chip>
            {
                new Chip { Name = "전체", Filter = x => true },
                new Chip { Name = "하는 중", Filter = x => Status(x) == 1 },
                new Chip { Name = "안 해봄", Filter = x => Status(x) == 0 },
                new Chip { Name = "다 깸", Filter = x => Status(x) == 2 },
            };
            if (pool.Any(IsKorean) && pool.Any(x => !IsKorean(x)))
            {
                chips.Add(new Chip { Name = "한글", Filter = IsKorean });
                chips.Add(new Chip { Name = "일본어", Filter = x => !IsKorean(x) });
            }
            foreach (var v in new[] { TkoolVersion.RM2000, TkoolVersion.XP, TkoolVersion.VX, TkoolVersion.VXAce, TkoolVersion.MV, TkoolVersion.MZ })
            {
                var vv = v;
                if (pool.Any(x => x.IsTkool && x.Version == vv))
                    chips.Add(new Chip { Name = EngineShort(new GameInfo { Kind = GameKind.Tkool, Version = vv }), Filter = x => x.IsTkool && x.Version == vv });
            }
            if (pool.Any(x => x.Tags.Contains("체험판"))) chips.Add(new Chip { Name = "체험판", Filter = x => x.Tags.Contains("체험판") });
            chipIdx = Math.Max(0, chips.FindIndex(c => c.Name == chipSel));
        }

        static readonly Dictionary<string, DateTime> addedCache = new Dictionary<string, DateTime>();
        static DateTime Added(GameInfo g)
        {
            string k = g.Folder ?? g.Exe;
            DateTime d;
            if (k == null) return DateTime.MinValue;
            if (!addedCache.TryGetValue(k, out d)) { try { d = System.IO.Directory.GetCreationTime(k); } catch { d = DateTime.MinValue; } addedCache[k] = d; }
            return d;
        }

        List<GameInfo> SortWide(IEnumerable<GameInfo> src)
        {
            if (sortMode == 1) return src.OrderByDescending(Added).ToList();
            if (sortMode == 2) return src.OrderByDescending(x => GameData.Get(x).TotalSeconds).ThenBy(x => x.Title, StringComparer.CurrentCulture).ToList();
            return src.OrderBy(x => x.Title, StringComparer.CurrentCulture).ToList();
        }

        void PickChip(int i)
        {
            if (chips.Count == 0) return;
            chipIdx = Math.Max(0, Math.Min(chips.Count - 1, i));
            chipSel = chips[chipIdx].Name;
            focus = 0; scroll = target = 0;
            OnGamesChanged();
            Shell.FocusChanged();
        }

        void NextSort()
        {
            sortMode = (sortMode + 1) % SortNames.Length;
            focus = 0; scroll = target = 0;
            OnGamesChanged();
            Shell.ShowToast("정렬: " + SortNames[sortMode], 1500);
        }

        public override GameInfo FocusedGame => chipFocus ? null : focus < items.Count ? items[focus] : null;

        void BuildTabs()
        {
            var all = Shell.VisibleGames.ToList();
            // 첫 탭은 '전체' (라이브러리를 열면 여기서 시작)
            tabs = new List<Tab> { new Tab { Name = "전체", Filter = x => true } };
            // 쯔꾸르 폴더에서 찾은 건 '쯔꾸르'에만, 스팀·배틀넷·에픽·GOG·설치된 게임은 'PC 게임'에
            Func<GameInfo, bool> isT = x => x.FromFolder;   // 쯔꾸르 탭 = 설정에서 고른 게임 폴더의 게임만
            if (all.Any(isT)) tabs.Add(new Tab { Name = "쯔꾸르", Filter = isT });
            if (all.Any(x => !isT(x))) tabs.Add(new Tab { Name = "PC 게임", Filter = x => !isT(x) });
            tabs.Add(new Tab { Name = "즐겨찾기", Filter = x => GameData.Get(x).Favorite });
            tabs.Add(new Tab { Name = "최근 플레이", Filter = x => GameData.Get(x).LastPlayed > 0 });
            tab = Math.Max(0, tabs.FindIndex(t => t.Name == lastTab));
        }

        public override void OnGamesChanged()
        {
            var cur = FocusedGame;
            // 라이브러리를 새로 열면 설정한 첫 탭부터 (그 탭에 게임이 없으면 전체)
            string keep = tabs.Count > 0 ? tabs[tab].Name : StartTab();
            BuildTabs();
            int ti = tabs.FindIndex(t => t.Name == keep);
            if (ti >= 0) tab = ti;
            var f = tabs[tab].Filter;
            items = tabs[tab].Name == "최근 플레이" ? Shell.Recent(int.MaxValue).Where(f).ToList() : Shell.VisibleGames.Where(f).ToList();
            if (Wide)
            {
                BuildChips(items);
                var cf = chips[chipIdx].Filter;
                items = SortWide(items.Where(cf));
            }
            else chipFocus = false;
            int i = cur != null ? items.FindIndex(x => x.Exe == cur.Exe) : -1;
            focus = i >= 0 ? i : 0;
            Follow();
        }

        string StartTab()
        {
            string want = Settings.LibStartTab ?? "전체";
            var all = Shell.VisibleGames.ToList();
            bool any = want == "즐겨찾기" ? all.Any(x => GameData.Get(x).Favorite)
                     : want == "최근 플레이" ? all.Any(x => GameData.Get(x).LastPlayed > 0) : true;
            return any ? want : "전체";
        }

        void SetTab(int t)
        {
            int n = tabs.Count;
            t = (t + n) % n;
            if (t == tab) return;
            tab = t;
            lastTab = tabs[t].Name;
            focus = 0; scroll = target = 0;
            OnGamesChanged();
            Shell.FocusChanged();
        }

        int TabH => S(60);
        int Pad0 => S(40);
        int Gap => S(18);
        int Cols => Wide ? Math.Max(2, (Area.Width - Pad0 * 2 + Gap) / (S(250) + Gap))
                         : Math.Max(3, (Area.Width - Pad0 * 2 + Gap) / (S(150) + Gap));
        int CapW => (Area.Width - Pad0 * 2 - Gap * (Cols - 1)) / Cols;
        int CapH => Wide ? CapW * 3 / 4 + BandH : CapW * 3 / 2;   // 쯔꾸르: 4:3 타이틀 그림 + 아래 이름 띠
        Rectangle GridArea => new Rectangle(Area.X, Area.Y + TabH + ChipH, Area.Width, Area.Height - TabH - ChipH);

        Rectangle CapRect(int i)
        {
            int c = i % Cols, row = i / Cols;
            return new Rectangle(Area.X + Pad0 + c * (CapW + Gap), (int)(GridArea.Y + S(20) + row * (CapH + Gap) - scroll), CapW, CapH);
        }

        float MaxScroll
        {
            get
            {
                int rows = (items.Count + Cols - 1) / Cols;
                return Math.Max(0, S(20) * 2 + rows * (CapH + Gap) - GridArea.Height);
            }
        }

        void Follow()
        {
            if (items.Count == 0) { target = 0; return; }
            int row = focus / Cols;
            float top = S(20) + row * (CapH + Gap);
            float bottom = top + CapH;
            if (top - S(24) < target) target = top - S(24);
            if (bottom + S(24) > target + GridArea.Height) target = bottom + S(24) - GridArea.Height;
            target = Math.Max(0, Math.Min(MaxScroll, target));
            Shell.Animate();
        }

        public override bool Tick(float dt)
        {
            if (dragging) return false;
            float before = scroll;
            scroll = Draw.Lerp(scroll, target, dt * 14f);
            if (Math.Abs(scroll - target) < 0.5f) scroll = target;
            return Math.Abs(scroll - before) > 0.01f;
        }

        public override void Paint(Graphics g)
        {
            // 탭 줄
            int cy = Area.Y + TabH / 2;
            int totalW = 0;
            int nt = tabs.Count;
            if (tabRects.Length != nt) tabRects = new Rectangle[nt];
            var widths = new int[nt];
            for (int i = 0; i < nt; i++) { widths[i] = Draw.Measure(g, tabs[i].Name, Draw.Menu) + S(36); totalW += widths[i]; }
            int x = Area.X + (Area.Width - totalW) / 2;
            Draw.PadGlyph(g, Pad.LB, x - S(50), cy);
            for (int i = 0; i < nt; i++)
            {
                var r = new Rectangle(x, Area.Y, widths[i], TabH);
                tabRects[i] = r;
                bool on = i == tab;
                Draw.Text(g, tabs[i].Name, on ? Theme.Px(17, true) : Draw.Menu, r, on ? Theme.Text : Theme.SubText, Draw.Center);
                if (on) Draw.Fill(g, new Rectangle(r.X + S(16), r.Bottom - S(6), r.Width - S(32), S(3)), S(1), Theme.Accent);
                x += widths[i];
            }
            Draw.PadGlyph(g, Pad.RB, x + S(20), cy);
            Draw.Text(g, items.Count + "개", Draw.Body, new Rectangle(Area.Right - Pad0 - S(120), Area.Y, S(120), TabH), Theme.SubText, Draw.Right);
            using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawLine(p, Area.X + Pad0, Area.Y + TabH, Area.Right - Pad0, Area.Y + TabH);
            if (Wide) PaintChips(g);

            var clip = g.Clip;
            g.SetClip(GridArea);
            if (items.Count == 0)
            {
                string tn = tab < tabs.Count ? tabs[tab].Name : "";
                string msg = tn == "즐겨찾기" ? "즐겨찾기한 게임이 없어요.\n게임 화면에서 ★를 누르면 여기에 모여요." :
                             tn == "최근 플레이" ? "아직 플레이한 게임이 없어요." : "게임이 없어요.";
                if (tabs.Count == 0) msg = "";
                Draw.Text(g, msg, Draw.Menu, GridArea, Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
            for (int i = 0; i < items.Count; i++)
            {
                var r = CapRect(i);
                if (r.Bottom < GridArea.Top - S(20) || r.Top > GridArea.Bottom + S(20)) continue;
                bool f = i == focus && Shell.ShowFocus;
                if (f && !Wide) r = Rectangle.Inflate(r, S(5), (int)(S(5) * 1.5f));
                if (Wide)
                {
                    PaintWideCard(g, items[i], r);
                    if (f) Draw.FocusGlow(g, r, S(6));
                    continue;
                }
                var art = CoverArt.Get(items[i]);
                if (art?.Capsule != null) Draw.Image(g, art.Capsule, r, S(4));
                else
                {
                    Draw.Fill(g, r, S(4), Theme.Tile);
                    Draw.Text(g, items[i].Title, Draw.BodyBold, Rectangle.Inflate(r, -S(10), -S(10)), Theme.SubText, Draw.Wrap);
                }
                if (GameData.Get(items[i]).Favorite)
                    Draw.Star(g, new Rectangle(r.Right - S(26), r.Y + S(8), S(18), S(18)), true, Color.FromArgb(255, 205, 60));
                if (f) Draw.FocusGlow(g, r, S(4));
            }
            g.Clip = clip;

            // 스크롤 위치
            float max = MaxScroll;
            if (max > 0)
            {
                var ga = GridArea;
                int barH = Math.Max(S(40), (int)(ga.Height * ga.Height / (ga.Height + max)));
                int barY = ga.Y + (int)((ga.Height - barH) * (scroll / max));
                Draw.Fill(g, new Rectangle(Area.Right - S(10), barY, S(4), barH), S(2), Color.FromArgb(80, 255, 255, 255));
            }
        }

        void PaintChips(Graphics g)
        {
            int y = Area.Y + TabH + S(10), h = S(38);
            if (chipRects.Length != chips.Count) chipRects = new Rectangle[chips.Count];
            int x = Area.X + Pad0;
            var font = Theme.Px(14, true);
            for (int i = 0; i < chips.Count; i++)
            {
                int w = Draw.Measure(g, chips[i].Name, font) + S(32);
                var r = new Rectangle(x, y, w, h);
                chipRects[i] = r;
                bool on = i == chipIdx;
                Draw.Fill(g, r, h / 2, on ? Theme.Accent : Color.FromArgb(46, 255, 255, 255));
                Draw.Text(g, chips[i].Name, font, r, on ? Color.White : Theme.SubText, Draw.Center);
                if (chipFocus && on && extraSel == 0 && Shell.ShowFocus) Draw.FocusGlow(g, r, h / 2);
                x += w + S(10);
            }
            // 오른쪽: 정리 모드 · 정렬 바꾸기
            string tdTxt = "정리 모드";
            string st = "정렬 · " + SortNames[sortMode];
            int sw = Draw.Measure(g, st, font) + S(32);
            sortRect = new Rectangle(Area.Right - Pad0 - sw, y, sw, h);
            int tw = Draw.Measure(g, tdTxt, font) + S(32);
            tidyRect = new Rectangle(sortRect.X - S(10) - tw, y, tw, h);
            if (tidyRect.X > x)
            {
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1, S(1))))
                {
                    g.DrawPath(pen, Theme.Round(sortRect, h / 2));
                    g.DrawPath(pen, Theme.Round(tidyRect, h / 2));
                }
                Draw.Text(g, st, font, sortRect, Theme.SubText, Draw.Center);
                Draw.Text(g, tdTxt, font, tidyRect, Theme.SubText, Draw.Center);
                if (chipFocus && extraSel == 1 && Shell.ShowFocus) Draw.FocusGlow(g, tidyRect, h / 2);
                if (chipFocus && extraSel == 2 && Shell.ShowFocus) Draw.FocusGlow(g, sortRect, h / 2);
            }
            else sortRect = tidyRect = Rectangle.Empty;
        }

        /// <summary>쯔꾸르 카드: 위 4:3 타이틀 그림(늘리지 않고 맞춤) + 아래 띠(이름 한 줄 · 엔진·한/일 · 상태) + 맨 아래 상태 선</summary>
        void PaintWideCard(Graphics g, GameInfo gm, Rectangle r)
        {
            int rad = S(6);
            Draw.Fill(g, r, rad, Color.FromArgb(255, 28, 33, 41));
            var imgR = new Rectangle(r.X, r.Y, r.Width, r.Height - BandH);
            var art = CoverArt.Get(gm);
            var ec = EngineColor(gm);
            if (art?.Hero != null)
            {
                // 그림 비율 그대로 맞춰 넣기 (남는 곳은 어두운 바탕)
                var src = art.Hero;
                float k = Math.Min(imgR.Width / (float)src.Width, imgR.Height / (float)src.Height);
                int w = (int)(src.Width * k), h = (int)(src.Height * k);
                // 거의 꽉 차면(4:3 근처) 그냥 칸에 맞춰 살짝 잘라 꽉 채움
                float fill = Math.Max(imgR.Width / (float)src.Width, imgR.Height / (float)src.Height);
                if (fill / k < 1.08f) { k = fill; w = (int)(src.Width * k); h = (int)(src.Height * k); }
                var dst = new Rectangle(imgR.X + (imgR.Width - w) / 2, imgR.Y + (imgR.Height - h) / 2, w, h);
                using (var b = new SolidBrush(Color.FromArgb(255, 14, 17, 22))) using (var path = TopRound(imgR, rad)) g.FillPath(b, path);
                var oc = g.Clip;
                using (var path = TopRound(imgR, rad)) g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect);
                Draw.Image(g, src, dst, 0);
                g.Clip = oc;
            }
            else
            {
                // 그림 없음: 엔진 색 바탕 + 이름 첫 글자 (모든 카드 같은 모양)
                using (var path = TopRound(imgR, rad))
                using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(imgR, Color.FromArgb(255, ec.R * 2 / 3, ec.G * 2 / 3, ec.B * 2 / 3), Color.FromArgb(255, ec.R / 4, ec.G / 4, ec.B / 4), 60f))
                    g.FillPath(lg, path);
                string ch = string.IsNullOrEmpty(gm.Title) ? "?" : gm.Title.Substring(0, char.IsSurrogate(gm.Title[0]) && gm.Title.Length > 1 ? 2 : 1).ToUpperInvariant();
                Draw.Text(g, ch, Theme.Px(Math.Max(20, imgR.Height / 3f / Theme.Scale), true), imgR, Color.FromArgb(200, 255, 255, 255), Draw.Center);
            }
            // 즐겨찾기 별
            if (GameData.Get(gm).Favorite)
                Draw.Star(g, new Rectangle(r.Right - S(28), r.Y + S(8), S(20), S(20)), true, Color.FromArgb(255, 205, 60));

            // 아래 띠
            int bx = r.X + S(12), bw = r.Width - S(24);
            Draw.Text(g, gm.Title, Draw.BodyBold, new Rectangle(bx, imgR.Bottom + S(6), bw, S(26)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int ly = imgR.Bottom + S(33), lh = S(18);
            int dx = bx;
            // 엔진 색 점 + 이름
            using (var b = new SolidBrush(ec)) g.FillEllipse(b, dx, ly + lh / 2 - S(4), S(8), S(8));
            dx += S(13);
            string eng = gm.IsTkool ? EngineShort(gm) : "기타";
            var small = Theme.Px(12, true);
            Draw.Text(g, eng, small, new Rectangle(dx, ly, S(60), lh), Theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            dx += Draw.Measure(g, eng, small) + S(10);
            string lang = IsKorean(gm) ? "한" : "日";
            var lr = new Rectangle(dx, ly, S(22), lh);
            using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), Math.Max(1, S(1)))) g.DrawPath(pen, Theme.Round(lr, S(4)));
            Draw.Text(g, lang, small, lr, Theme.SubText, Draw.Center);
            dx += lr.Width + S(8);
            if (gm.Tags.Contains("체험판") && dx + S(50) < r.Right - S(70))
                Draw.Text(g, "체험판", small, new Rectangle(dx, ly, S(56), lh), Color.FromArgb(230, 190, 110), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            // 상태 (오른쪽)
            int stt = Status(gm);
            string stTxt = stt == 2 ? "다 깸" : stt == 1 ? "하는 중" : "안 해봄";
            var stCol = stt == 2 ? Color.FromArgb(110, 210, 120) : stt == 1 ? Theme.Accent : Color.FromArgb(120, 255, 255, 255);
            Draw.Text(g, stTxt, small, new Rectangle(r.Right - S(80), ly, S(68), lh), stCol, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            // 맨 아래 상태 선 (만화 뷰어 읽음 표시처럼)
            if (stt > 0)
                using (var b = new SolidBrush(stCol))
                    g.FillRectangle(b, r.X + rad, r.Bottom - S(3), r.Width - rad * 2, S(3));
        }

        static System.Drawing.Drawing2D.GraphicsPath TopRound(Rectangle r, int rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }

        void Move(int d)
        {
            if (items.Count == 0) return;
            int n = Math.Max(0, Math.Min(items.Count - 1, focus + d));
            if (n == focus) return;
            focus = n; Follow(); Shell.FocusChanged();
        }

        public override void OnPad(Pad b)
        {
            switch (b)
            {
                case Pad.Left: if (chipFocus) { if (extraSel > 0) extraSel--; else PickChip(chipIdx - 1); } else Move(-1); break;
                case Pad.Right:
                    if (chipFocus) { if (chipIdx < chips.Count - 1 && extraSel == 0) PickChip(chipIdx + 1); else extraSel = Math.Min(2, extraSel + 1); }   // 칸 끝에서 오른쪽 → 정리 모드 · 정렬
                    else Move(1);
                    break;
                case Pad.Up:
                    if (chipFocus) break;
                    if (focus - Cols >= 0) Move(-Cols);
                    else if (Wide) { chipFocus = true; target = 0; Shell.Animate(); Shell.FocusChanged(); }   // 맨 윗줄에서 위 → 분류 줄
                    break;
                case Pad.Down:
                    if (chipFocus) { chipFocus = false; extraSel = 0; Shell.FocusChanged(); break; }
                    if (focus + Cols < items.Count) Move(Cols); else if (focus / Cols < (items.Count - 1) / Cols) Move(items.Count - 1 - focus); break;
                case Pad.X: if (Wide) NextSort(); break;
                case Pad.LB: SetTab(tab - 1); break;
                case Pad.RB: SetTab(tab + 1); break;
                case Pad.A:
                    if (chipFocus && extraSel == 1) { Shell.Push(new TidyScreen()); break; }
                    if (chipFocus && extraSel == 2) { NextSort(); break; }
                    if (chipFocus) { chipFocus = false; Shell.FocusChanged(); } else if (FocusedGame != null) Shell.Push(new DetailScreen(FocusedGame)); break;
                case Pad.B: Shell.Pop(); break;
                case Pad.Y: Shell.Push(new SearchScreen()); break;
            }
        }

        public override void OnTap(Point p)
        {
            for (int i = 0; i < tabRects.Length; i++) if (tabRects[i].Contains(p)) { SetTab(i); return; }
            if (Wide)
            {
                for (int i = 0; i < chipRects.Length; i++) if (chipRects[i].Contains(p)) { chipFocus = false; PickChip(i); return; }
                if (sortRect.Contains(p)) { NextSort(); return; }
                if (tidyRect.Contains(p)) { Shell.Push(new TidyScreen()); return; }
            }
            if (!GridArea.Contains(p)) return;
            for (int i = 0; i < items.Count; i++)
                if (CapRect(i).Contains(p)) { focus = i; Shell.FocusChanged(); Shell.Push(new DetailScreen(items[i])); return; }
        }

        public override void OnDrag(int dx, int dy, bool start)
        {
            if (start) { dragStart = scroll; dragging = true; }
            if (dx == int.MinValue) { dragging = false; target = Math.Max(0, Math.Min(MaxScroll, scroll)); Shell.Animate(); return; }
            scroll = Math.Max(-S(60), Math.Min(MaxScroll + S(60), dragStart - dy));
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "열기", () => OnPad(Pad.A)),
            new Hint(Pad.Y, "검색", () => Shell.Push(new SearchScreen())),
        }.Concat(Wide ? new List<Hint> { new Hint(Pad.X, "정렬", NextSort) } : new List<Hint>()).Concat(new List<Hint>
        {
            new Hint(Pad.B, "뒤로", Shell.Pop),
            new Hint(Pad.RB, "탭 이동", () => SetTab(tab + 1)),
        }).ToList();
    }

    // ═════════════════════ 정리 모드 (쯔꾸르) ═════════════════════
    /// <summary>
    /// 쯔꾸르 게임을 한 개씩 넘기며 이름·표지·상태를 정리. 손볼 게 많은 게임(그림 없음·이름 이상함)부터.
    /// 이름은 타자 없이 후보(정리된 이름·원래 이름·폴더 이름·게임 창 제목) 중에서 고름.
    /// </summary>
    class TidyScreen : Screen
    {
        List<GameInfo> list = new List<GameInfo>();
        int idx, focus;   // focus: 0 이름 1 다 깸 2 표지 찍기 3 숨기기
        Rectangle[] btns = new Rectangle[4];
        Rectangle prevBtn, nextBtn;

        public override GameInfo FocusedGame => idx < list.Count ? list[idx] : null;

        public override void OnGamesChanged()
        {
            var cur = FocusedGame;
            list = Shell.VisibleGames.Where(x => x.FromFolder)
                .OrderByDescending(Problems).ThenBy(x => x.Title, StringComparer.CurrentCulture).ToList();
            int i = cur != null ? list.FindIndex(x => x.Exe == cur.Exe) : -1;
            idx = i >= 0 ? i : 0;
        }

        static int Problems(GameInfo g)
        {
            int n = 0;
            var art = CoverArt.Get(g);
            if (art != null && art.Hero == null) n += 2;
            if (GameCapture.IsGenericTitle(g, g.Title)) n += 2;
            if (g.RawTitle != null && g.RawTitle != g.Title) n += 1;
            return n;
        }

        List<string> Candidates(GameInfo g)
        {
            var c = new List<string>();
            Action<string> add = t => { if (!string.IsNullOrWhiteSpace(t) && !c.Contains(t.Trim())) c.Add(t.Trim()); };
            add(g.Title);
            add(TitleTidy.Clean(g.RawTitle ?? g.Title, new List<string>()));
            add(g.RawTitle);
            string folder = g.Folder != null ? System.IO.Path.GetFileName(g.Folder.TrimEnd('\\')) : null;
            add(TitleTidy.Clean(folder, new List<string>()));
            add(folder);
            return c;
        }

        void Act(int b)
        {
            var g = FocusedGame;
            if (g == null) return;
            var st = GameData.Get(g);
            switch (b)
            {
                case 0:
                    var c = Candidates(g);
                    if (c.Count < 2) { Shell.ShowToast("다른 이름 후보가 없어요", 1500); return; }
                    int i = c.IndexOf(g.Title);
                    string next = c[(i + 1) % c.Count];
                    g.Title = next; st.Title = "=" + next; GameData.Save();
                    break;
                case 1:
                    st.Cleared = !st.Cleared; GameData.Save();
                    break;
                case 2:
                    Shell.FindCovers(new List<GameInfo> { g });   // 15초쯤 켜서 타이틀 화면을 찍고 끔
                    break;
                case 3:
                    st.Hidden = true; GameData.Save();
                    Shell.ShowToast("숨겼어요 (설정에서 다시 보이게 할 수 있어요)", 2000);
                    Shell.RefreshLists();
                    return;
            }
            Shell.Invalidate();
        }

        void Go(int d)
        {
            if (list.Count == 0) return;
            idx = (idx + d + list.Count) % list.Count;
            Shell.FocusChanged();
        }

        public override void Paint(Graphics g)
        {
            var A = Area;
            if (list.Count == 0) { Draw.Text(g, "정리할 쯔꾸르 게임이 없어요.", Draw.Menu, A, Theme.SubText, Draw.Center); return; }
            var gm = list[idx];
            var st = GameData.Get(gm);
            int pad = S(40);
            Draw.Text(g, "정리 모드", Theme.Px(20, true), new Rectangle(A.X + pad, A.Y + S(12), S(400), S(44)), Theme.Text, Draw.Left);
            Draw.Text(g, (idx + 1) + " / " + list.Count + "  ·  손볼 게 많은 게임부터", Draw.Body, new Rectangle(A.Right - pad - S(500), A.Y + S(12), S(500), S(44)), Theme.SubText, Draw.Right);

            // 왼쪽: 타이틀 그림 (비율 그대로)
            int top = A.Y + S(70);
            var imgBox = new Rectangle(A.X + pad + S(56), top, (int)(A.Width * 0.5f), (int)(A.Width * 0.5f * 3 / 4));
            if (imgBox.Bottom > A.Bottom - S(20)) { int h = A.Bottom - S(20) - top; imgBox = new Rectangle(imgBox.X, top, h * 4 / 3, h); }
            Draw.Fill(g, imgBox, S(6), Color.FromArgb(255, 14, 17, 22));
            var art = CoverArt.Get(gm);
            if (art?.Hero != null)
            {
                float k = Math.Min(imgBox.Width / (float)art.Hero.Width, imgBox.Height / (float)art.Hero.Height);
                int w = (int)(art.Hero.Width * k), h = (int)(art.Hero.Height * k);
                Draw.Image(g, art.Hero, new Rectangle(imgBox.X + (imgBox.Width - w) / 2, imgBox.Y + (imgBox.Height - h) / 2, w, h), S(4));
            }
            else Draw.Text(g, "타이틀 그림이 없어요\n'표지 찍기'로 채울 수 있어요", Draw.Menu, imgBox, Theme.SubText, Draw.Center | TextFormatFlags.WordBreak);
            // 넘기기 화살표
            prevBtn = new Rectangle(A.X + pad - S(4), imgBox.Y + imgBox.Height / 2 - S(40), S(48), S(80));
            nextBtn = new Rectangle(imgBox.Right + S(8), imgBox.Y + imgBox.Height / 2 - S(40), S(48), S(80));
            Draw.Text(g, "‹", Theme.Px(34, true), prevBtn, Theme.SubText, Draw.Center);
            Draw.Text(g, "›", Theme.Px(34, true), nextBtn, Theme.SubText, Draw.Center);

            // 오른쪽: 정보 + 버튼
            int rx = nextBtn.Right + S(28), rw = A.Right - pad - rx;
            int y = top;
            Draw.Text(g, gm.Title, Theme.Px(22, true), new Rectangle(rx, y, rw, S(40)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            y += S(44);
            if (gm.RawTitle != null && gm.RawTitle != gm.Title)
            {
                Draw.Text(g, "원래 이름: " + gm.RawTitle, Draw.Body, new Rectangle(rx, y, rw, S(26)), Theme.SubText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += S(28);
            }
            string info = (gm.IsTkool ? LibraryScreen.EngineShort(gm) : "기타") + " · " + (LibraryScreen.IsKorean(gm) ? "한글" : "일본어") +
                          (gm.Tags.Contains("체험판") ? " · 체험판" : "") + " · 플레이 " + (st.TotalSeconds / 3600 > 0 ? st.TotalSeconds / 3600 + "시간 " : "") + (st.TotalSeconds / 60 % 60) + "분";
            Draw.Text(g, info, Draw.Body, new Rectangle(rx, y, rw, S(26)), Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            y += S(46);

            var names = new[]
            {
                "이름 바꾸기 (" + Candidates(gm).Count + "개 후보)",
                st.Cleared ? "다 깸 ✓ (누르면 해제)" : "다 깸으로 표시",
                "표지 찍기 (15초 켜서 찍음)",
                "이 게임 숨기기",
            };
            for (int i = 0; i < 4; i++)
            {
                var r = new Rectangle(rx, y, Math.Min(rw, S(420)), S(56));
                btns[i] = r;
                Draw.Fill(g, r, S(6), i == 1 && st.Cleared ? Color.FromArgb(60, 140, 75) : Theme.Tile);
                Draw.Text(g, names[i], Draw.BodyBold, Rectangle.Inflate(r, -S(16), 0), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (i == focus && Shell.ShowFocus) Draw.FocusGlow(g, r, S(6));
                y += S(66);
            }
        }

        public override void OnPad(Pad b)
        {
            switch (b)
            {
                case Pad.Up: focus = Math.Max(0, focus - 1); break;
                case Pad.Down: focus = Math.Min(3, focus + 1); break;
                case Pad.Left: case Pad.LB: Go(-1); break;
                case Pad.Right: case Pad.RB: Go(1); break;
                case Pad.A: Act(focus); break;
                case Pad.B: Shell.Pop(); return;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            if (prevBtn.Contains(p)) { Go(-1); Shell.Invalidate(); return; }
            if (nextBtn.Contains(p)) { Go(1); Shell.Invalidate(); return; }
            for (int i = 0; i < 4; i++) if (btns[i].Contains(p)) { focus = i; Act(i); return; }
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "선택", () => OnPad(Pad.A)),
            new Hint(Pad.LB, "이전 게임", () => OnPad(Pad.LB)),
            new Hint(Pad.RB, "다음 게임", () => OnPad(Pad.RB)),
            new Hint(Pad.B, "끝내기", Shell.Pop),
        };
    }

    // ═════════════════════ 게임 상세 ═════════════════════
    class DetailScreen : Screen
    {
        readonly GameInfo game;
        int focus; // 0 플레이, 1 즐겨찾기, 2 숨기기, 3 해상도
        Rectangle playBtn, favBtn, hideBtn, resBtn, clearBtn;

        public DetailScreen(GameInfo g) { game = g; }
        public override GameInfo FocusedGame => game;

        public override void Paint(Graphics g)
        {
            var full = Shell.ClientRectangle;
            int heroBottom = Area.Top + (int)(Area.Height * 0.46f);
            var heroRect = new Rectangle(0, 0, full.Width, heroBottom);
            var art = CoverArt.Get(game);

            if (art?.Hero != null)
            {
                g.InterpolationMode = art.Hero.Width <= 816 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                CoverArt.DrawCover(g, art.Hero, heroRect, 0.3f);
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }
            else if (art?.Blur != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                CoverArt.DrawCover(g, art.Blur, heroRect, 0.3f);
            }
            // 히어로 아래쪽을 배경색으로 자연스럽게
            using (var lg = new LinearGradientBrush(new Rectangle(0, heroBottom - S(220), full.Width, S(221)),
                Color.FromArgb(0, Theme.Bg), Theme.Bg, 90f))
                g.FillRectangle(lg, 0, heroBottom - S(220), full.Width, S(220));
            using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(b, heroRect);

            int pad = S(48);
            // 제목
            Draw.TextShadow(g, game.Title, Draw.Hero, new Rectangle(pad, heroBottom - S(110), full.Width - pad * 2, S(56)), Theme.Text);

            // 버튼 줄
            int rowY = heroBottom - S(36);
            using (var p = new SolidBrush(Theme.Panel)) g.FillRectangle(p, 0, rowY - S(14), full.Width, S(100));
            playBtn = new Rectangle(pad, rowY, S(230), S(64));
            using (var lg = new LinearGradientBrush(playBtn, Theme.PlayTop, Theme.PlayBottom, 0f))
            using (var path = Theme.Round(playBtn, S(4)))
                g.FillPath(lg, path);
            Draw.PlayTriangle(g, new Rectangle(playBtn.X + S(36), playBtn.Y + S(22), S(18), S(20)), Color.White);
            Draw.Text(g, "플레이", Theme.Px(22, true), new Rectangle(playBtn.X + S(66), playBtn.Y, S(140), playBtn.Height), Color.White);
            if (focus == 0 && Shell.ShowFocus) Draw.FocusGlow(g, playBtn, S(4));

            var st = GameData.Get(game);
            favBtn = new Rectangle(playBtn.Right + S(16), rowY, S(64), S(64));
            Draw.Fill(g, favBtn, S(4), Theme.Tile);
            Draw.Star(g, Rectangle.Inflate(favBtn, -S(18), -S(18)), st.Favorite, st.Favorite ? Color.FromArgb(255, 205, 60) : Theme.Text);
            if (focus == 1 && Shell.ShowFocus) Draw.FocusGlow(g, favBtn, S(4));

            hideBtn = new Rectangle(favBtn.Right + S(12), rowY, S(110), S(64));
            Draw.Fill(g, hideBtn, S(4), Theme.Tile);
            Draw.Text(g, "숨기기", Draw.BodyBold, hideBtn, Theme.SubText, Draw.Center);
            if (focus == 2 && Shell.ShowFocus) Draw.FocusGlow(g, hideBtn, S(4));

            // 이 게임만 해상도 강제 (예: 디아블로 → 1280×720)
            resBtn = new Rectangle(hideBtn.Right + S(12), rowY, S(200), S(64));
            Draw.Fill(g, resBtn, S(4), string.IsNullOrEmpty(st.Res) ? Theme.Tile : Color.FromArgb(40, 90, 150));
            Draw.Text(g, "해상도", Draw.Small, new Rectangle(resBtn.X, resBtn.Y + S(6), resBtn.Width, S(22)), Theme.SubText, Draw.Center);
            Draw.Text(g, string.IsNullOrEmpty(st.Res) ? "그대로" : st.Res, Draw.BodyBold, new Rectangle(resBtn.X, resBtn.Y + S(28), resBtn.Width, S(30)), Theme.Text, Draw.Center);
            if (focus == 3 && Shell.ShowFocus) Draw.FocusGlow(g, resBtn, S(4));

            // 다 깸 표시
            clearBtn = new Rectangle(resBtn.Right + S(12), rowY, S(120), S(64));
            Draw.Fill(g, clearBtn, S(4), st.Cleared ? Color.FromArgb(60, 140, 75) : Theme.Tile);
            Draw.Text(g, st.Cleared ? "다 깸" : "다 깸?", Draw.BodyBold, clearBtn, st.Cleared ? Color.White : Theme.SubText, Draw.Center);
            if (focus == 4 && Shell.ShowFocus) Draw.FocusGlow(g, clearBtn, S(4));

            // 기록
            int ix = clearBtn.Right + S(44);
            InfoBlock(g, ref ix, rowY + S(6), "마지막 플레이", GameData.LastPlayedText(st.LastPlayed));
            InfoBlock(g, ref ix, rowY + S(6), "플레이 시간", GameData.PlayTimeText(st.TotalSeconds));

            // 정보
            // 아래: 왼쪽 소개 / 오른쪽 정보 (스팀 게임 화면처럼)
            int y0 = rowY + S(100);
            int bottom = Area.Bottom - S(12);
            int infoW = Math.Min(S(380), (full.Width - pad * 2) / 3);
            int descW = full.Width - pad * 2 - infoW - S(40);
            int ix0 = pad + descW + S(40);

            Draw.Text(g, "소개", Draw.H2, new Rectangle(pad, y0, S(300), S(30)), Theme.Text);
            string desc = GameDesc.Get(game);
            string dtext = desc == null ? "불러오는 중..." :
                desc.Length > 0 ? desc : "소개 글이 없어요. 게임 폴더에 readme.txt 같은 설명 파일이 있으면 여기에 보여줘요.";
            var dr = new Rectangle(pad, y0 + S(40), descW, bottom - y0 - S(40));
            Draw.Fill(g, Rectangle.Inflate(dr, S(10), S(8)), S(6), Color.FromArgb(110, 23, 29, 37));
            Draw.Text(g, dtext, Draw.Body, dr, desc != null && desc.Length > 0 ? Theme.Text : Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            Draw.Text(g, "정보", Draw.H2, new Rectangle(ix0, y0, S(300), S(30)), Theme.Text);
            int y = y0 + S(42);
            int bx = ix0;
            Draw.Badge(g, ref bx, y, game.IsTkool ? "RPG MAKER " + game.VersionLabel.ToUpperInvariant() : game.VersionLabel + " 게임", Theme.GameColor(game));
            if (game.Manual) Draw.Badge(g, ref bx, y, "직접 추가", Color.FromArgb(70, 80, 96));
            y += S(30); bx = ix0;
            if (game.IsTkool || game.Japanese)
            {
                Draw.Badge(g, ref bx, y, game.Japanese ? "일본어 원어" : "한국어/기타", game.Japanese ? Color.FromArgb(186, 62, 62) : Color.FromArgb(70, 80, 96));
                y += S(30); bx = ix0;
            }
            if (game.NeedsRtp)
            {
                Draw.Badge(g, ref bx, y, "RTP 필요" + (string.IsNullOrEmpty(game.RtpName) ? "" : " (" + game.RtpName + ")"), Color.FromArgb(70, 80, 96));
                y += S(30);
            }
            y += S(8);
            Draw.Text(g, game.Folder, Draw.Small, new Rectangle(ix0, y, infoW, S(40)), Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += S(48);
            Draw.Text(g, "표지 바꾸기: 내 문서\\쯔꾸르 런처\\covers 에 '" + System.IO.Path.GetFileName(game.Folder) + ".png'",
                Draw.Small, new Rectangle(ix0, y, infoW, S(40)), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        void InfoBlock(Graphics g, ref int x, int y, string label, string value)
        {
            int w = Math.Max(Draw.Measure(g, label, Draw.Label), Draw.Measure(g, value, Draw.Menu)) + S(48);
            Draw.Text(g, label.ToUpperInvariant(), Draw.Label, new Rectangle(x, y, w, S(20)), Theme.SubText);
            Draw.Text(g, value, Draw.Menu, new Rectangle(x, y + S(24), w, S(26)), Theme.Text);
            x += w;
        }

        void ToggleFav()
        {
            var st = GameData.Get(game);
            st.Favorite = !st.Favorite;
            GameData.Save();
            Shell.Invalidate();
        }

        void Hide()
        {
            GameData.Get(game).Hidden = true;
            GameData.Save();
            Shell.ShowToast("숨겼어요. 설정 > 숨긴 게임 다시 보이기로 되돌릴 수 있어요", 4000);
            Shell.Pop();
            Shell.RefreshLists();
        }

        public override void OnPad(Pad b)
        {
            switch (b)
            {
                case Pad.Left: focus = Math.Max(0, focus - 1); break;
                case Pad.Right: focus = Math.Min(4, focus + 1); break;
                case Pad.A: if (focus == 0) Shell.Launch(game); else if (focus == 1) ToggleFav(); else if (focus == 2) Hide(); else if (focus == 3) NextRes(); else ToggleCleared(); break;
                case Pad.Y: ToggleFav(); break;
                case Pad.B: Shell.Pop(); break;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            if (playBtn.Contains(p)) { focus = 0; Shell.Launch(game); }
            else if (favBtn.Contains(p)) { focus = 1; ToggleFav(); }
            else if (hideBtn.Contains(p)) { focus = 2; Hide(); }
            else if (resBtn.Contains(p)) { focus = 3; NextRes(); }
            else if (clearBtn.Contains(p)) { focus = 4; ToggleCleared(); }
        }

        void ToggleCleared()
        {
            var st = GameData.Get(game);
            st.Cleared = !st.Cleared;
            GameData.Save();
            Shell.ShowToast(st.Cleared ? "다 깬 게임으로 표시했어요" : "다 깸 표시를 뺐어요", 1500);
            Shell.Invalidate();
        }

        // 그대로 → 화면이 지원하는 해상도들 → 그대로 … 차례로
        void NextRes()
        {
            var st = GameData.Get(game);
            List<string> list;
            try { list = SystemControls.Resolutions(out _); } catch { list = new List<string>(); }
            // 지금 해상도는 빼고(원래 값이니) 큰 것부터
            string cur = ResForce.CurrentRes();
            var opts = new List<string> { null };
            foreach (var r in list) if (r != cur && !opts.Contains(r)) opts.Add(r);
            int i = opts.IndexOf(st.Res);
            st.Res = opts[(i + 1) % opts.Count];
            GameData.Save();
            Shell.ShowToast(st.Res == null ? "이 게임은 해상도를 바꾸지 않아요" : "이 게임을 켜면 " + st.Res + "로 바꾸고, 끄면 되돌려요", 2500);
            Shell.Invalidate();
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, focus == 0 ? "플레이" : focus == 1 ? "즐겨찾기" : focus == 2 ? "숨기기" : focus == 3 ? "해상도 바꾸기" : "다 깸 표시", () => OnPad(Pad.A)),
            new Hint(Pad.Y, "즐겨찾기", ToggleFav),
            new Hint(Pad.B, "뒤로", Shell.Pop),
        };
    }

    // ═════════════════════ 왼쪽 메인 메뉴 ═════════════════════
    class MainMenu
    {
        public ShellForm Shell;
        public bool Open;
        public float T;     // 0 닫힘 ~ 1 열림
        int focus;
        Rectangle panel;
        readonly List<Rectangle> rects = new List<Rectangle>();

        string[] Names => new[] { "홈", "라이브러리", "설정", "게임 폴더 설정", "게임 다시 찾기", "런처 끄기" };

        public bool Tick(float dt)
        {
            float target = Open ? 1f : 0f;
            float before = T;
            T = Draw.Lerp(T, target, dt * 16f);
            if (Math.Abs(T - target) < 0.01f) T = target;
            return Math.Abs(T - before) > 0.0001f;
        }

        public void Paint(Graphics g, Rectangle full)
        {
            if (T <= 0) return;
            int a = (int)(160 * T);
            using (var b = new SolidBrush(Color.FromArgb(a, 0, 0, 0))) g.FillRectangle(b, full);
            int w = Theme.S(340);
            int x = (int)(-w + w * EaseOut(T));
            panel = new Rectangle(x, 0, w, full.Height);
            using (var b = new SolidBrush(Color.FromArgb(14, 20, 27))) g.FillRectangle(b, panel);
            using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawLine(p, panel.Right - 1, 0, panel.Right - 1, full.Height);

            Draw.Text(g, "쯔꾸르 런처", Draw.H2, new Rectangle(x + Theme.S(32), Theme.S(28), w, Theme.S(30)), Theme.Text);
            Draw.Text(g, "v" + Updater.Current, Draw.Small, new Rectangle(x + Theme.S(32), Theme.S(60), w, Theme.S(20)), Theme.SubText);

            rects.Clear();
            int y = Theme.S(110);
            var names = Names;
            for (int i = 0; i < names.Length; i++)
            {
                var r = new Rectangle(x + Theme.S(12), y, w - Theme.S(24), Theme.S(56));
                rects.Add(r);
                if (i == focus) Draw.Fill(g, r, Theme.S(4), Theme.TileHot);
                Draw.Text(g, names[i], i == focus ? Theme.Px(18, true) : Draw.Menu, new Rectangle(r.X + Theme.S(22), r.Y, r.Width, r.Height),
                    i == names.Length - 1 ? Color.FromArgb(240, 140, 140) : Theme.Text);
                y += Theme.S(60);
                if (i == 1 || i == 4) y += Theme.S(14);
            }
        }

        static float EaseOut(float t) => 1 - (1 - t) * (1 - t) * (1 - t);

        void Run(int i)
        {
            Open = false;
            Shell.Animate();
            switch (i)
            {
                case 0: Shell.GoHome(); break;
                case 1: Shell.GoHome(); Shell.Push(new LibraryScreen()); break;
                case 2: Shell.GoHome(); Shell.Push(new SettingsScreen()); break;
                case 3: Shell.OpenFolders(); break;
                case 4: Shell.Rescan(); break;
                case 5: Shell.Close(); break;
            }
        }

        public void OnPad(Pad b)
        {
            switch (b)
            {
                case Pad.Up: focus = Math.Max(0, focus - 1); break;
                case Pad.Down: focus = Math.Min(Names.Length - 1, focus + 1); break;
                case Pad.A: Run(focus); return;
                case Pad.B: case Pad.Back: case Pad.Start: Open = false; Shell.Animate(); return;
            }
            Shell.Invalidate();
        }

        public void OnTap(Point p)
        {
            if (!panel.Contains(p)) { Open = false; Shell.Animate(); return; }
            for (int i = 0; i < rects.Count; i++)
                if (rects[i].Contains(p)) { focus = i; Run(i); return; }
        }

        public List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "선택", () => Run(focus)),
            new Hint(Pad.B, "닫기", () => { Open = false; Shell.Animate(); }),
        };
    }
}

namespace TkoolLauncher
{
    // ═════════════════════ 설정 ═════════════════════
    class SettingsScreen : Screen
    {
        int focus;
        float scroll;
        readonly List<Rectangle> rows = new List<Rectangle>();
        string updateNote;

        class Row { public string Title, Sub, Value; public bool? Toggle; public Action Act; }

        List<Row> Rows()
        {
            return new List<Row>
            {
                new Row { Title = "자동 업데이트", Sub = "켤 때 새 버전이 있으면 알아서 받고 런처만 다시 켜져요", Toggle = Settings.AutoUpdate,
                          Act = () => { Settings.AutoUpdate = !Settings.AutoUpdate; Settings.Save(); } },
                new Row { Title = "지금 업데이트 확인", Sub = updateNote ?? "GitHub에 올라온 새 버전을 바로 확인해요", Value = "v" + Updater.Current,
                          Act = CheckNow },
                new Row { Title = "게임 폴더 설정", Sub = "쯔꾸르·PC 게임이 들어있는 폴더 추가·삭제 (폴더 하나 = 게임 하나)", Value = Settings.GameFolders.Count + "개 폴더",
                          Act = Shell.OpenFolders },
                new Row { Title = "스팀·에픽·GOG 게임 자동 찾기", Sub = "설치된 스토어 게임을 알아서 목록에 넣어요", Toggle = Settings.StoreScan,
                          Act = () => { Settings.StoreScan = !Settings.StoreScan; Settings.Save(); Shell.Rescan(); } },
                new Row { Title = "게임 직접 추가", Sub = "자동으로 못 찾은 게임의 실행 파일(exe)을 골라 넣어요", Value = Settings.ManualGames.Count + "개",
                          Act = Shell.AddManualGame },
                new Row { Title = "고급 하드웨어 제어 (팬·조명·TDP)", Toggle = Settings.HardwareControl,
                          Sub = Settings.HardwareControl ? (Hardware.Ready ? "켜짐 · " + (Hardware.SmuReady ? "TDP " : "") + (Hardware.EcReady ? "팬 조명 " : "") + (Hardware.TempReady ? "온도" : "") : Hardware.Problem)
                                                         : (Hardware.PawnInstalled ? "켜면 관리자 권한으로 다시 켜져요 (허락 창은 처음 한 번만)" : "PawnIO 드라이버 설치부터 해요 (누르면 설치 시작)"),
                          Act = ToggleHardware },
                new Row { Title = "위쪽 LC 버튼", Sub = "누를 때마다 다음 기능으로 바뀌어요", Value = ButtonActions.Names[Settings.LcAction % ButtonActions.Names.Length],
                          Act = () => { Settings.LcAction = (Settings.LcAction + 1) % ButtonActions.Names.Length; Settings.Save(); } },
                new Row { Title = "위쪽 RC 버튼", Sub = "누를 때마다 다음 기능으로 바뀌어요", Value = ButtonActions.Names[Settings.RcAction % ButtonActions.Names.Length],
                          Act = () => { Settings.RcAction = (Settings.RcAction + 1) % ButtonActions.Names.Length; Settings.Save(); } },
                new Row { Title = "테마 색", Sub = "포커스·버튼·슬라이더 색", Value = Theme.AccentNames[Settings.AccentIndex % Theme.AccentNames.Length],
                          Act = () => { Settings.AccentIndex = (Settings.AccentIndex + 1) % Theme.AccentNames.Length; Settings.Save(); Theme.ApplyAccent(); } },
                new Row { Title = "배경 사진", Toggle = Settings.UseBgImage, Sub = "내 문서\\쯔꾸르 런처 에 background.jpg 를 넣으면 배경이 돼요",
                          Act = () => { Settings.UseBgImage = !Settings.UseBgImage; Settings.Save(); Shell.LoadBackground(); } },
                new Row { Title = "라이브러리 첫 탭", Sub = "라이브러리를 열면 처음 보일 탭 (비어 있으면 전체)", Value = Settings.LibStartTab,
                          Act = () =>
                          {
                              var opts = new[] { "즐겨찾기", "전체", "쯔꾸르", "PC 게임", "최근 플레이" };
                              int i = Array.IndexOf(opts, Settings.LibStartTab);
                              Settings.LibStartTab = opts[(i + 1) % opts.Length];
                              Settings.Save();
                          } },
                new Row { Title = "배경음악", Toggle = Settings.Bgm, Sub = Bgm.FilePath != null ? "런처 화면에서만 반복 재생" : "내 문서\\쯔꾸르 런처 에 bgm.mp3 를 넣어 주세요",
                          Act = () => { Settings.Bgm = !Settings.Bgm; Settings.Save(); } },
                new Row { Title = "최소화 반복 감시", Toggle = Settings.LoopGuard,
                          Sub = LoopGuard.LastReport != "" ? "마지막 감지: " + LoopGuard.LastReport : "창이 저절로 내려갔다 올라오길 반복하면 원인 입력을 막아요",
                          Act = () => { Settings.LoopGuard = !Settings.LoopGuard; Settings.Save(); } },
                new Row { Title = "작업 표시줄 끝 눌러 바탕화면 보기", Toggle = LoopGuard.CornerPeekOn, Sub = "최소화 반복이 생기면 끄는 걸 추천해요",
                          Act = () => LoopGuard.CornerPeekOn = !LoopGuard.CornerPeekOn },
                new Row { Title = "오류 기록 보기", Sub = "내 문서\\쯔꾸르 런처\\logs — Claude에게 보여주면 고칠 수 있어요",
                          Value = System.IO.File.Exists(Log.ErrPath) ? "있음" : "없음",
                          Act = () => ErrorPopup.Show2("최근 오류 기록", Log.Tail(900) is string t && t.Length > 0 ? t : "오류 기록이 없어요") },
                new Row { Title = "표지·이름 한 번에 찾기", Sub = "표지나 이름이 빠진 게임을 하나씩 15초씩 켜서 타이틀 화면을 찍어요 (홈 버튼으로 멈춤)",
                          Value = GameCapture.Missing(Shell.Games).Count + "개", Act = () => Shell.FindCovers() },
                new Row { Title = "표지 없는 게임 자동 표지", Toggle = Settings.AutoCover, Sub = "처음 켤 때 타이틀 화면을 찍어 표지로, 이름이 'Game'이면 창 제목으로 바꿔요",
                          Act = () => { Settings.AutoCover = !Settings.AutoCover; Settings.Save(); } },
                new Row { Title = "쯔꾸르 게임 자동 확대", Toggle = Settings.AutoScaleTkool, Sub = "2000~VX Ace 게임을 켜면 작은 창을 도트 선명하게 꽉 채워요",
                          Act = () => { Settings.AutoScaleTkool = !Settings.AutoScaleTkool; Settings.Save(); } },
                new Row { Title = "AYA 큰 버튼 연결", Sub = learning == 1 ? "지금 AYA 큰 버튼을 누르세요..." : "빠른 설정 열기 · 패드 가이드 버튼, 보기+메뉴 동시 누름도 돼요",
                          Value = Hotkeys.Describe(Settings.HomeKey), Act = () => Learn(1) },
                new Row { Title = "AYA 큰 버튼 길게 누르기", Sub = learning == 3 ? "지금 AYA 큰 버튼을 길게 누르세요..." : "라이브러리 전체 화면 · 길게 눌러도 안 되면 여기서 배워 주세요",
                          Value = string.IsNullOrEmpty(Settings.HomeLongKey) ? "0.6초 누름" : Hotkeys.Describe(Settings.HomeLongKey), Act = () => Learn(3) },
                new Row { Title = "AYA 작은 버튼 연결", Sub = learning == 2 ? "지금 AYA 작은 버튼을 누르세요..." : "런처 홈 화면으로", Value = Hotkeys.Describe(Settings.SmallKey), Act = () => Learn(2) },
                new Row { Title = "윈도우 시작할 때 자동 실행", Sub = "켜면 부팅하자마자 런처가 떠요", Toggle = SystemControls.GetAutoStart(),
                          Act = () => SystemControls.SetAutoStart(!SystemControls.GetAutoStart()) },
                new Row { Title = "숨긴 게임 다시 보이기", Sub = "게임 화면에서 [숨기기]로 숨긴 게임을 모두 되돌려요", Value = GameData.HiddenCount() + "개",
                          Act = () => { GameData.UnhideAll(); Shell.RefreshLists(); Shell.ShowToast("숨긴 게임을 다시 보이게 했어요"); } },
                new Row { Title = "게임 다시 찾기", Sub = "폴더에 새 게임을 넣었을 때", Value = "게임 " + Shell.Games.Count + "개",
                          Act = Shell.Rescan },
            };
        }

        int learning; // 0 없음, 1 큰 버튼, 2 작은 버튼

        void ToggleHardware()
        {
            if (!Settings.HardwareControl)
            {
                if (!Hardware.PawnInstalled)
                {
                    Hardware.InstallPawn();
                    Shell.ShowToast("PawnIO 설치가 끝나면 이 스위치를 다시 켜 주세요", 6000);
                    return;
                }
                Settings.HardwareControl = true;
                Settings.Save();
                if (Hardware.IsAdmin) { System.Threading.Tasks.Task.Run(() => { Hardware.Init(); Hardware.ApplySaved(); }); return; }
                if (Hardware.CreateAdminTask()) { Settings.AdminTaskExe = Application.ExecutablePath; Settings.Save(); if (Hardware.RunAdminTask()) { Application.Exit(); return; } }
                Settings.HardwareControl = false;
                Settings.Save();
                Shell.ShowToast("관리자 허락을 받지 못했어요", 4000);
            }
            else
            {
                Settings.HardwareControl = false;
                Settings.Save();
                Hardware.Shutdown();
                Hardware.DeleteAdminTask();
                Shell.ShowToast("껐어요. 다음에 켤 때부터 일반 권한으로 켜져요", 4000);
            }
        }

        void Learn(int which)
        {
            if (learning != 0) { learning = 0; Hotkeys.Learning = false; return; }
            learning = which;
            Hotkeys.Learned = combo => Shell.BeginInvoke(new Action(() =>
            {
                if (learning == 2) Settings.SmallKey = combo;
                else if (learning == 3) { if (combo != Settings.HomeKey) Settings.HomeLongKey = combo; }
                else Settings.HomeKey = combo;
                learning = 0;
                Settings.Save();
                Shell.ShowToast("연결했어요: " + Hotkeys.Describe(combo), 4000);
                Shell.Invalidate();
            }));
            Hotkeys.Learning = true;
        }

        void CheckNow()
        {
            updateNote = "확인하는 중...";
            Updater.CheckAsync(Shell, msg => { updateNote = msg; Shell.ShowToast(msg, 4000); Shell.Invalidate(); }, () =>
            {
                updateNote = "새 버전을 받았어요. 다시 켜는 중...";
                Shell.Invalidate();
                if (!GameWatch.Running) Updater.Restart();
            }, true);
            Shell.Invalidate();
        }

        public override void Paint(Graphics g)
        {
            int pad = S(48);
            int w = Math.Min(Area.Width - pad * 2, S(900));
            int x = Area.X + (Area.Width - w) / 2;
            Draw.Text(g, "설정", Draw.H1, new Rectangle(x, Area.Y + S(8), w, S(40)), Theme.Text);

            rows.Clear();
            var list = Rows();
            int rowStep = S(74);
            int visible = Area.Bottom - (Area.Y + S(58)) - S(8);
            float need = (focus + 1) * rowStep - visible;
            if (need > scroll) scroll = need;
            if (focus * rowStep < scroll) scroll = focus * rowStep;
            if (scroll < 0) scroll = 0;
            var clipS = g.Clip;
            g.SetClip(new Rectangle(Area.X, Area.Y + S(54), Area.Width, Area.Height - S(54)));
            int y = Area.Y + S(58) - (int)scroll;
            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i];
                var r = new Rectangle(x, y, w, S(68));
                rows.Add(r);
                Draw.Fill(g, r, S(4), i == focus ? Theme.TileHot : Color.FromArgb(150, 23, 29, 37));
                Draw.Text(g, row.Title, Draw.Menu, new Rectangle(r.X + S(24), r.Y + S(9), r.Width - S(260), S(28)), Theme.Text);
                Draw.Text(g, row.Sub, Draw.Small, new Rectangle(r.X + S(24), r.Y + S(38), r.Width - S(260), S(22)), Theme.SubText);
                if (row.Toggle.HasValue)
                {
                    var t = new Rectangle(r.Right - S(24) - S(56), r.Y + (r.Height - S(30)) / 2, S(56), S(30));
                    Draw.Fill(g, t, S(15), row.Toggle.Value ? Theme.Accent : Color.FromArgb(70, 78, 90));
                    int k = S(24);
                    var knob = new Rectangle(row.Toggle.Value ? t.Right - k - S(3) : t.X + S(3), t.Y + S(3), k, k);
                    using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, knob);
                }
                else if (row.Value != null)
                {
                    Draw.Text(g, row.Value, Draw.Body, new Rectangle(r.Right - S(224), r.Y, S(200), r.Height), Theme.SubText, Draw.Right);
                }
                if (i == focus && Shell.ShowFocus) Draw.Ring(g, r, S(4), Theme.Focus, S(2));
                y += S(74);
            }
            g.Clip = clipS;
        }

        void Run(int i)
        {
            var list = Rows();
            if (i >= 0 && i < list.Count) list[i].Act?.Invoke();
            Shell.Invalidate();
        }

        public override void OnPad(Pad b)
        {
            int n = Rows().Count;
            switch (b)
            {
                case Pad.Up: focus = Math.Max(0, focus - 1); break;
                case Pad.Down: focus = Math.Min(n - 1, focus + 1); break;
                case Pad.A: Run(focus); break;
                case Pad.B: Shell.Pop(); break;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Contains(p)) { focus = i; Run(i); return; }
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "선택", () => Run(focus)),
            new Hint(Pad.B, "뒤로", Shell.Pop),
        };
    }
}
