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
        public Point DownAt;            // 마지막으로 누른 곳 (끌기 시작 위치)
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
        int chipIdx, extraSel;   // extraSel: 0 칩, 1.. 오른쪽 단추(중복 확인·정리 모드·정렬)
        Rectangle[] extraRects = new Rectangle[0];

        List<KeyValuePair<string, Action>> Extras()
        {
            var l = new List<KeyValuePair<string, Action>>();
            int nn = NewInbox.Count;
            if (nn > 0) l.Add(new KeyValuePair<string, Action>("새 게임 " + nn, NewMenu));
            int n = DupFinder.Groups.Count;
            if (n > 0) l.Add(new KeyValuePair<string, Action>("중복 확인 " + n, () => Shell.Push(new DupScreen())));
            int pc = GameCapture.PendingCount(Shell.Games);
            if (pc > 0) l.Add(new KeyValuePair<string, Action>("표지 확인 " + pc, () => Shell.Push(new CoverReviewScreen())));
            l.Add(new KeyValuePair<string, Action>("정리 모드", () => Shell.Push(new TidyScreen())));
            l.Add(new KeyValuePair<string, Action>("정렬 · " + SortNames[sortMode], NextSort));
            return l;
        }
        Rectangle[] chipRects = new Rectangle[0];

        // 모든 탭이 같은 가로 카드 모양, 분류 칩·정렬은 쯔꾸르 탭에만
        bool Wide => true;
        bool TkTab => tab < tabs.Count && tabs[tab].Name == "쯔꾸르";
        int ChipH => TkTab ? S(58) : 0;
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

        /// <summary>카드에 넣을 짧은 엔진 이름</summary>
        public static string ShortEngine(string e)
        {
            if (string.IsNullOrEmpty(e)) return "기타";
            var map = new Dictionary<string, string>
            {
                { "픽셀 게임 메이커 MV", "픽겜MV" }, { "스마일 게임 빌더 / 바킨", "SGB/바킨" }, { "RPG 개발자 바킨", "바킨" },
                { "울프 RPG 에디터", "울프RPG" }, { "티라노 스크립트/빌더", "티라노" }, { "쯔꾸르 유나이트(Unity)", "유나이트" },
                { "직접 만든 엔진 (SDL/DirectX)", "자체엔진" }, { "키리키리(吉里吉里)", "키리키리" }, { "AGS (어드벤처 게임 스튜디오)", "AGS" },
                { "비주얼 노벨 메이커", "VN메이커" }, { "일렉트론 (HTML5)", "HTML5" }, { "NW.js (HTML5)", "HTML5" }, { "DX 라이브러리", "DX라이브러리" },
            };
            if (map.TryGetValue(e, out string v)) return v;
            e = e.Split('(')[0].Trim();
            return e.Length > 9 ? e.Substring(0, 9) : e;
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

        public static bool IsKorean(GameInfo g)
        {
            int lang = GameData.Get(g).Lang;   // 직접 고친 값이 먼저
            if (lang == 1) return true;
            if (lang == 2) return false;
            return g.Tags.Contains("한글") || !g.Japanese;
        }

        /// <summary>0 안 해봄 1 하는 중 2 다 깸</summary>
        public static int Status(GameInfo g) { var st = GameData.Get(g); return st.Cleared ? 2 : st.LastPlayed > 0 ? 1 : 0; }

        void BuildChips(List<GameInfo> pool)
        {
            // 위 칸: 상태·언어·체험판·숨김만 (태그·엔진은 왼쪽 주황 단추 → 고르기 창)
            chips = new List<Chip>
            {
                new Chip { Name = "전체", Filter = x => true },
                new Chip { Name = "플레이 중", Filter = x => Status(x) == 1 },
                new Chip { Name = "완료", Filter = x => Status(x) == 2 },
            };
            if (pool.Any(IsKorean)) chips.Add(new Chip { Name = "한글", Filter = IsKorean });
            if (pool.Any(x => !IsKorean(x))) chips.Add(new Chip { Name = "일본어", Filter = x => !IsKorean(x) });
            if (pool.Any(x => x.Tags.Contains("체험판"))) chips.Add(new Chip { Name = "체험판", Filter = x => x.Tags.Contains("체험판") });
            if (Shell.Games != null && Shell.Games.Any(x => x.FromFolder && GameData.Get(x).Hidden))
                chips.Add(new Chip { Name = "숨김", Filter = x => false });   // 목록은 따로 (숨긴 게임만)
            // 설정한 순서·숨김 적용
            var order = ChipEditScreen.Order();
            var sorted = new List<Chip>();
            foreach (var o in order)
            {
                if (o.Hidden) continue;
                var c = chips.FirstOrDefault(x => x.Name == o.Name);
                if (c != null) sorted.Add(c);
            }
            foreach (var c in chips) if (!order.Any(o => o.Name == c.Name)) sorted.Add(c);
            if (sorted.Count == 0) sorted.Add(chips[0]);
            chips = sorted;
            chipIdx = Math.Max(0, chips.FindIndex(c => c.Name == chipSel));
            if (chipIdx >= chips.Count) chipIdx = 0;
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
            ChipFollow();
            Shell.FocusChanged();
        }

        // ── 왼쪽 주황 단추: 태그·엔진 (고르기 창) ──
        static string tagSel, engSel;
        Rectangle tagBtn, engBtn, inpBtn;
        // 조작 필터: 패드·키보드·마우스·터치마다 '-' 상관없음 / 'o' 됨 / 'n' 꼭 필요 / 'x' 안 됨 / '?' 모름
        static char[] inpSel = { '-', '-', '-', '-' };
        static bool InpOn => inpSel.Any(c => c != '-');
        static string InpWord(char c) => c == 'o' ? "됨" : c == 'n' ? "꼭 필요" : c == 'x' ? "안 됨" : c == '?' ? "모름" : "상관없음";
        static bool InpMatch(GameInfo x)
        {
            if (!InpOn) return true;
            var s = InputNeeds.Get(x).S;
            for (int k = 0; k < 4; k++)
            {
                char w = inpSel[k], c = s[k];
                if (w == '-') continue;
                if (w == 'o' ? !(c == 'o' || c == 'n') : c != w) return false;
            }
            return true;
        }
        string InpLabel()
        {
            if (!InpOn) return "조작 ▾";
            var parts = new List<string>();
            for (int k = 0; k < 4; k++) if (inpSel[k] != '-') parts.Add(InputNeeds.Names[k] + " " + InpWord(inpSel[k]));
            string t = string.Join("·", parts);
            return (t.Length > 14 ? "조작 " + parts.Count + "개" : t) + "  ×";
        }
        void OpenInputFilter()
        {
            var cur = (char[])inpSel.Clone();
            Action show = null;
            show = () =>
            {
                var opts = new List<string>();
                for (int k = 0; k < 4; k++) opts.Add(InputNeeds.Names[k] + ": " + InpWord(cur[k]));
                opts.Add("✓ 이대로 보기");
                opts.Add("모두 풀기");
                PickerScreen.Show(Shell, "조작 방식으로 보기 (누를 때마다 바뀜)", opts.ToArray(), -1, i =>
                {
                    if (i < 4) { cur[i] = cur[i] == '-' ? 'o' : cur[i] == 'o' ? 'n' : cur[i] == 'n' ? 'x' : cur[i] == 'x' ? '?' : '-'; show(); return; }
                    inpSel = i == 4 ? cur : new[] { '-', '-', '-', '-' };
                    focus = 0; scroll = target = 0; OnGamesChanged();
                }, "상관없음 → 됨 → 꼭 필요 → 안 됨 → 모름");
            };
            show();
        }

        /// <summary>내 태그 + DLsite 장르(한글)</summary>
        static List<string> TagsAll(GameInfo g)
        {
            var l = new List<string>(GameData.TagsOf(g));
            var d = DlsiteInfo.Get(g);
            if (d != null)
                for (int k = 0; k < d.Genres.Count; k++)
                {
                    string t = DlsiteInfo.GenreKo(d.Genres[k]);
                    if (!l.Contains(t)) l.Add(t);
                }
            // 조작 방식도 태그로 고를 수 있게
            try
            {
                var inp = InputNeeds.Get(g).S;
                if (inp[0] == 'o' || inp[0] == 'n') l.Add("조작: 패드 됨"); else if (inp[0] == 'x') l.Add("조작: 패드 안 됨");
                if (inp[1] == 'n') l.Add("조작: 키보드 필요");
                if (inp[2] == 'n') l.Add("조작: 마우스 필요");
                if (inp[3] == 'o' || inp[3] == 'n') l.Add("조작: 터치 됨");
            }
            catch { }
            return l;
        }

        static string EngineOf(GameInfo g) => g.IsTkool ? EngineShort(g) : g.Engine != null ? ShortEngine(g.Engine) : "기타";

        IEnumerable<GameInfo> TkPool => Shell.VisibleGames.Where(x => x.FromFolder);

        void OpenTagPicker()
        {
            var counts = TkPool.SelectMany(TagsAll).GroupBy(x => x).OrderByDescending(x => x.Count()).ThenBy(x => x.Key)
                .Select(x => new KeyValuePair<string, int>(x.Key, x.Count())).ToList();
            if (counts.Count == 0) { Shell.ShowToast("아직 태그가 없어요 (상세 화면 + 태그, DLsite 장르)", 2500); return; }
            FlowPicker.Show(Shell, "태그로 보기", counts, tagSel, t => { tagSel = t; focus = 0; scroll = target = 0; OnGamesChanged(); });
        }

        void OpenEnginePicker()
        {
            var counts = TkPool.GroupBy(EngineOf).OrderByDescending(x => x.Count()).ThenBy(x => x.Key)
                .Select(x => new KeyValuePair<string, int>(x.Key, x.Count())).ToList();
            FlowPicker.Show(Shell, "엔진으로 보기", counts, engSel, t => { engSel = t; focus = 0; scroll = target = 0; OnGamesChanged(); });
        }

        void OpenDetail(GameInfo g)
        {
            var snap = items.ToList();
            Shell.Push(new DetailScreen(g, snap, x => { int i = items.IndexOf(x); if (i >= 0) { focus = i; Follow(); } }));
        }

        void NextSort()
        {
            PickerScreen.Show(Shell, "정렬 방식", SortNames, sortMode, i =>
            {
                sortMode = i;
                focus = 0; scroll = target = 0;
                OnGamesChanged();
            });
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
            if (TkTab)
            {
                BuildChips(items);
                var cf = chips[chipIdx].Filter;
                IEnumerable<GameInfo> src = chips[chipIdx].Name == "숨김"
                    ? Shell.Games.Where(x => x.FromFolder && GameData.Get(x).Hidden)
                    : items.Where(cf);
                if (tagSel != null) src = src.Where(x => TagsAll(x).Contains(tagSel));
                if (engSel != null) src = src.Where(x => EngineOf(x) == engSel);
                if (InpOn) src = src.Where(InpMatch);
                items = SortWide(src);
                newN = 0;
                if (chips[chipIdx].Name != "숨김" && NewInbox.Count > 0)
                {
                    var nw = items.Where(NewInbox.IsNew).ToList();
                    newN = nw.Count;
                    if (newN > 0) items = nw.Concat(items.Where(x => !NewInbox.IsNew(x))).ToList();
                }
            }
            else { chipFocus = false; newN = 0; }
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

        // ── 새로 들어온 게임: 맨 위 노란 테두리 칸 (확인 끝 전까지) ──
        int newN;
        int NewHdrH => newN > 0 ? S(64) : 0;
        int NewRows => (newN + Cols - 1) / Cols;
        int NewBlockH => newN > 0 ? NewHdrH + NewRows * (CapH + Gap) - Gap + S(20) + S(40) : 0;   // 칸 + 아래 띄움
        Rectangle newChkR, newEndR;

        /// <summary>목록 맨 위에서부터의 카드 위치(스크롤 전)</summary>
        int ItemTop(int i)
        {
            if (i < newN) return S(20) + NewHdrH + (i / Cols) * (CapH + Gap);
            int j = i - newN;
            return S(20) + NewBlockH + (j / Cols) * (CapH + Gap);
        }
        int ItemCol(int i) => i < newN ? i % Cols : (i - newN) % Cols;

        Rectangle CapRect(int i)
        {
            return new Rectangle(Area.X + Pad0 + ItemCol(i) * (CapW + Gap), (int)(GridArea.Y + ItemTop(i) - scroll), CapW, CapH);
        }

        float MaxScroll
        {
            get
            {
                if (items.Count == 0) return 0;
                return Math.Max(0, ItemTop(items.Count - 1) + CapH + S(20) - GridArea.Height);
            }
        }

        /// <summary>위아래 이웃 카드 (새 게임 칸과 아래 목록 사이도 자연스럽게)</summary>
        int VNeighbor(int d)
        {
            int myTop = ItemTop(focus), myCol = ItemCol(focus), best = -1, bestRowTop = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int t = ItemTop(i);
                if (d > 0 ? t <= myTop : t >= myTop) continue;
                if (best < 0 || (d > 0 ? t < bestRowTop : t > bestRowTop)) { bestRowTop = t; best = i; }
            }
            if (best < 0) return -1;
            int pick = -1, dist = int.MaxValue;
            for (int i = 0; i < items.Count; i++)
                if (ItemTop(i) == bestRowTop && Math.Abs(ItemCol(i) - myCol) < dist) { dist = Math.Abs(ItemCol(i) - myCol); pick = i; }
            return pick;
        }

        void Follow()
        {
            if (items.Count == 0) { target = 0; return; }
            float top = ItemTop(focus) - (focus < Cols && newN > 0 ? NewHdrH : 0);
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
            if (TkTab) PaintChips(g);

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
            newChkR = newEndR = Rectangle.Empty;
            if (newN > 0) PaintNewBox(g);
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
            PaintBar(g);
        }

        static readonly Color NewYellow = Color.FromArgb(255, 238, 196, 60);

        void PaintNewBox(Graphics g)
        {
            int y = (int)(GridArea.Y + S(20) - scroll) - S(4);
            var box = new Rectangle(Area.X + Pad0 - S(14), y, Area.Width - Pad0 * 2 + S(28), NewHdrH + NewRows * (CapH + Gap) - Gap + S(18));
            using (var path = Theme.Round(box, S(12)))
            {
                using (var b = new SolidBrush(Color.FromArgb(28, 238, 196, 60))) g.FillPath(b, path);
                using (var p = new Pen(NewYellow, S(3))) g.DrawPath(p, path);
            }
            int hy = box.Y + S(8), hh = S(48);
            newEndR = new Rectangle(box.Right - S(16) - S(150), hy, S(150), hh);
            newChkR = new Rectangle(newEndR.X - S(12) - S(210), hy, S(210), hh);
            int dupN = NewDupGroups().Count;
            string sub = DupFinder.Busy ? "중복 검사 중…" : dupN > 0 ? "중복·새 버전 " + dupN + "묶음 확인 필요" : "중복 없음";
            Draw.Text(g, "새로 들어온 게임 " + newN + "개", Theme.Px(18, true), new Rectangle(box.X + S(18), hy, S(320), hh), NewYellow, Draw.Left);
            Draw.Text(g, sub + " · 이름·정보는 알아서 채우는 중", Draw.Small, new Rectangle(box.X + S(18) + S(300), hy, newChkR.X - box.X - S(330), hh), Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Draw.Fill(g, newChkR, S(6), dupN > 0 ? Color.FromArgb(255, 150, 112, 20) : Color.FromArgb(255, 52, 62, 78));
            Draw.Text(g, dupN > 0 ? "중복·버전 보기 " + dupN : "중복·버전 보기", Draw.BodyBold, newChkR, Color.White, Draw.Center);
            Draw.Fill(g, newEndR, S(6), Color.FromArgb(255, 60, 120, 70));
            Draw.Text(g, "확인 끝", Draw.BodyBold, newEndR, Color.White, Draw.Center);
        }

        public static List<DupFinder.Group> NewDupGroups() => DupFinder.Groups.Where(gr => gr.Games.Any(NewInbox.IsNew)).ToList();

        void OpenNewDups()
        {
            if (NewDupGroups().Count == 0) { Shell.ShowToast(DupFinder.Busy ? "중복 검사 중이에요. 조금 뒤에 다시 봐 주세요" : "새 게임 중 기존 게임과 겹치는 건 없어요", 3500); return; }
            Shell.Push(new DupScreen(true));
        }

        void EndNew()
        {
            int n = NewInbox.Count;
            PickerScreen.Confirm(Shell, "새 게임 " + n + "개 확인을 끝낼까요?", "확인 끝", () =>
            {
                Shell.BeginInvoke(new Action(() =>
                    PickerScreen.Confirm(Shell, "정말 끝낼까요?", "네, 끝내기", () =>
                    {
                        NewInbox.Clear();
                        Shell.RefreshLists();
                        Shell.ShowToast("노란 칸을 풀었어요 — 일반 목록에 섞였어요", 3000);
                    }, "노란 칸에서 빠지고 일반 목록에 섞여요. 되돌릴 수 없어요")));
            }, NewDupGroups().Count > 0 ? "아직 중복·새 버전 " + NewDupGroups().Count + "묶음을 안 봤어요" : null);
        }

        void NewMenu()
        {
            PickerScreen.Show(Shell, "새로 들어온 게임 " + NewInbox.Count + "개", new[] { "중복·새 버전 보기", "확인 끝 (노란 칸 풀기)", "닫기" }, 0, i =>
            {
                if (i == 0) Shell.BeginInvoke(new Action(OpenNewDups));
                else if (i == 1) Shell.BeginInvoke(new Action(EndNew));
            });
        }

        // ── 오른쪽 스크롤 손잡이 (터치·마우스로 끌기) ──
        bool barDrag;
        Rectangle BarZone => new Rectangle(Area.Right - S(38), GridArea.Y + S(8), S(38), GridArea.Height - S(16));
        int ThumbH => Math.Max(S(64), (int)(BarZone.Height * GridArea.Height / (float)(GridArea.Height + MaxScroll)));

        Rectangle ThumbRect()
        {
            var z = BarZone;
            float k = MaxScroll <= 0 ? 0 : scroll / MaxScroll;
            k = Math.Max(0, Math.Min(1, k));
            return new Rectangle(z.X + S(12), z.Y + (int)((z.Height - ThumbH) * k), S(14), ThumbH);
        }

        void PaintBar(Graphics g)
        {
            if (MaxScroll <= 0) return;
            var z = BarZone;
            using (var b = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) g.FillRectangle(b, z.X + S(17), z.Y, S(4), z.Height);
            var t = ThumbRect();
            if (barDrag) t = Rectangle.Inflate(t, S(3), 0);
            Draw.Fill(g, t, t.Width / 2, barDrag ? Theme.Accent : Color.FromArgb(150, 255, 255, 255));
        }

        void BarJump(int y)
        {
            var z = BarZone;
            float k = (y - z.Y - ThumbH / 2f) / Math.Max(1f, z.Height - ThumbH);
            scroll = target = Math.Max(0, Math.Min(MaxScroll, k * MaxScroll));
            Shell.Invalidate();
        }

        // 칩 줄: 왼쪽 칩 칸과 오른쪽 단추 칸을 나눠 고정. 칩이 넘치면 좌우로 밀어 보기(끌기·화살표·패드로 따라감)
        int chipScroll, chipScrollStart, chipTotalW;
        Rectangle chipArea, chipLeftR, chipRightR;
        bool chipDrag;

        void PaintChips(Graphics g)
        {
            int y = Area.Y + TabH + S(10), h = S(38);
            var font = Theme.Px(14, true);
            // 1) 오른쪽 단추 (자리 고정)
            var ex = Extras();
            int rx = Area.Right - Pad0;
            extraRects = new Rectangle[ex.Count];
            for (int i = ex.Count - 1; i >= 0; i--)
            {
                int w = Draw.Measure(g, ex[i].Key, font) + S(32);
                rx -= w;
                extraRects[i] = new Rectangle(rx, y, w, h);
                rx -= S(10);
            }
            for (int i = 0; i < ex.Count; i++)
            {
                var r = extraRects[i];
                bool dup = ex[i].Key.StartsWith("중복");
                if (dup) Draw.Fill(g, r, h / 2, Color.FromArgb(200, 190, 120, 40));
                else using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1, S(1)))) g.DrawPath(pen, Theme.Round(r, h / 2));
                Draw.Text(g, ex[i].Key, font, r, dup ? Color.White : Theme.SubText, Draw.Center);
                if (chipFocus && extraSel == i + 1 && Shell.ShowFocus) Draw.FocusGlow(g, r, h / 2);
            }
            // 2) 맨 왼쪽 주황 단추 (태그·엔진, 자리 고정)
            int lx = Area.X + Pad0;
            var orange = Color.FromArgb(255, 214, 120, 30);
            string tagText = tagSel == null ? "태그 ▾" : "# " + tagSel + "  ×";
            string engText = engSel == null ? "엔진 ▾" : engSel + "  ×";
            tagBtn = new Rectangle(lx, y, Math.Min(S(200), Draw.Measure(g, tagText, font) + S(32)), h);
            engBtn = new Rectangle(tagBtn.Right + S(10), y, Math.Min(S(200), Draw.Measure(g, engText, font) + S(32)), h);
            string inpText = InpLabel();
            inpBtn = new Rectangle(engBtn.Right + S(10), y, Math.Min(S(220), Draw.Measure(g, inpText, font) + S(32)), h);
            foreach (var bt in new[] { tagBtn, engBtn, inpBtn })
            {
                bool set = bt == tagBtn ? tagSel != null : bt == engBtn ? engSel != null : InpOn;
                if (set) Draw.Fill(g, bt, h / 2, orange);
                else using (var pen = new Pen(orange, Math.Max(1, S(2)))) g.DrawPath(pen, Theme.Round(bt, h / 2));
                Draw.Text(g, bt == tagBtn ? tagText : bt == engBtn ? engText : inpText, font, bt, set ? Color.White : orange, Draw.Center | TextFormatFlags.EndEllipsis);
            }
            if (chipFocus && extraSel == -3 && Shell.ShowFocus) Draw.FocusGlow(g, tagBtn, h / 2);
            if (chipFocus && extraSel == -2 && Shell.ShowFocus) Draw.FocusGlow(g, engBtn, h / 2);
            if (chipFocus && extraSel == -1 && Shell.ShowFocus) Draw.FocusGlow(g, inpBtn, h / 2);
            lx = inpBtn.Right + S(14);
            // 3) 칩 칸
            chipArea = new Rectangle(lx, y - S(4), rx - S(14) - lx, h + S(8));
            if (chipRects.Length != chips.Count) chipRects = new Rectangle[chips.Count];
            int x = 0;
            var widths = new int[chips.Count];
            for (int i = 0; i < chips.Count; i++) { widths[i] = Draw.Measure(g, chips[i].Name, font) + S(32); x += widths[i] + S(10); }
            chipTotalW = Math.Max(0, x - S(10));
            int maxScroll = Math.Max(0, chipTotalW - chipArea.Width);
            chipScroll = Math.Max(0, Math.Min(maxScroll, chipScroll));
            var oc = g.Clip;
            g.SetClip(chipArea, System.Drawing.Drawing2D.CombineMode.Intersect);
            x = chipArea.X - chipScroll;
            for (int i = 0; i < chips.Count; i++)
            {
                var r = new Rectangle(x, y, widths[i], h);
                chipRects[i] = r;
                bool on = i == chipIdx;
                Draw.Fill(g, r, h / 2, on ? Theme.Accent : Color.FromArgb(46, 255, 255, 255));
                Draw.Text(g, chips[i].Name, font, r, on ? Color.White : Theme.SubText, Draw.Center);
                if (chipFocus && on && extraSel == 0 && Shell.ShowFocus) Draw.FocusGlow(g, r, h / 2);
                x += widths[i] + S(10);
            }
            // 넘치면 양끝에 밀기 화살표
            chipLeftR = chipRightR = Rectangle.Empty;
            if (chipScroll > 0)
            {
                chipLeftR = new Rectangle(chipArea.X, chipArea.Y, S(44), chipArea.Height);
                using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(chipLeftR, Color.FromArgb(240, 18, 22, 28), Color.FromArgb(0, 18, 22, 28), 0f)) g.FillRectangle(lg, chipLeftR);
                Draw.Text(g, "‹", Theme.Px(22, true), chipLeftR, Theme.Text, Draw.Left);
            }
            if (chipScroll < maxScroll)
            {
                chipRightR = new Rectangle(chipArea.Right - S(44), chipArea.Y, S(44), chipArea.Height);
                using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(chipRightR, Color.FromArgb(0, 18, 22, 28), Color.FromArgb(240, 18, 22, 28), 0f)) g.FillRectangle(lg, chipRightR);
                Draw.Text(g, "›", Theme.Px(22, true), chipRightR, Theme.Text, Draw.Right);
            }
            g.Clip = oc;
        }

        /// <summary>고른 칩이 칸 안에 보이게 밀기</summary>
        void ChipFollow()
        {
            if (chipIdx < 0 || chipIdx >= chipRects.Length || chipArea.Width <= 0) return;
            var r = chipRects[chipIdx];
            int left = r.X - chipArea.X + chipScroll, right = left + r.Width;
            if (left < chipScroll + S(40)) chipScroll = Math.Max(0, left - S(40));
            else if (right > chipScroll + chipArea.Width - S(40)) chipScroll = right - chipArea.Width + S(40);
        }

        /// <summary>쯔꾸르 카드: 위 4:3 타이틀 그림(늘리지 않고 맞춤) + 아래 띠(이름 한 줄 · 엔진·한/일 · 상태) + 맨 아래 상태 선</summary>
        void PaintWideCard(Graphics g, GameInfo gm, Rectangle r)
        {
            int rad = S(6);
            Draw.Fill(g, r, rad, Color.FromArgb(255, 28, 33, 41));
            var imgR = new Rectangle(r.X, r.Y, r.Width, r.Height - BandH);
            var ec = EngineColor(gm);
            // 작은 썸네일(메모리에 미리 있음) → 없으면 큰 그림 → 그래도 없으면 그림 없음 표시
            Image src = CoverArt.Thumb(gm);
            if (src == null && !CoverArt.HasNoHero(gm)) { var art = CoverArt.Get(gm); src = art?.Hero; }
            bool loading = src == null && !CoverArt.HasNoHero(gm);
            if (loading)
            {
                using (var b = new SolidBrush(Color.FromArgb(255, 20, 24, 30))) using (var path = TopRound(imgR, rad)) g.FillPath(b, path);
            }
            else if (src != null)
            {
                // 그림 비율 그대로 맞춰 넣기 (남는 곳은 어두운 바탕)
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
            string eng = gm.IsTkool ? EngineShort(gm) : ShortEngine(gm.Engine);
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
            // LT·RT = 위 동그라미 칩 왼쪽·오른쪽으로 (어디에 있든)
            if ((b == Pad.LT || b == Pad.RT) && chips.Count > 0)
            {
                int ni = Math.Max(0, Math.Min(chips.Count - 1, chipIdx + (b == Pad.RT ? 1 : -1)));
                if (ni != chipIdx) PickChip(ni);
                Shell.Invalidate();
                return;
            }
            switch (b)
            {
                case Pad.Left:
                    if (chipFocus)
                    {
                        if (extraSel > 0) extraSel--;
                        else if (extraSel < 0) extraSel = Math.Max(-3, extraSel - 1);
                        else if (chipIdx > 0) PickChip(chipIdx - 1);
                        else extraSel = -1;    // 칸 맨 앞에서 왼쪽 → 엔진·태그 단추
                        Shell.Invalidate();
                    }
                    else Move(-1);
                    break;
                case Pad.Right:
                    if (chipFocus && extraSel < 0) { extraSel++; Shell.Invalidate(); }
                    else if (chipFocus) { if (chipIdx < chips.Count - 1 && extraSel == 0) PickChip(chipIdx + 1); else extraSel = Math.Min(Extras().Count, extraSel + 1); }   // 칸 끝에서 오른쪽 → 오른쪽 단추들
                    else Move(1);
                    break;
                case Pad.Up:
                    if (chipFocus) break;
                    { int up = VNeighbor(-1); if (up >= 0) { Move(up - focus); break; } }
                    if (TkTab) { chipFocus = true; target = 0; Shell.Animate(); Shell.FocusChanged(); }   // 맨 윗줄에서 위 → 분류 줄
                    break;
                case Pad.Down:
                    if (chipFocus) { chipFocus = false; extraSel = 0; Shell.FocusChanged(); break; }
                    { int dn = VNeighbor(1); if (dn >= 0) Move(dn - focus); } break;
                case Pad.X: if (TkTab) NextSort(); break;
                case Pad.LB: SetTab(tab - 1); break;
                case Pad.RB: SetTab(tab + 1); break;
                case Pad.A:
                    if (chipFocus && extraSel == -3) { OpenTagPicker(); break; }
                    if (chipFocus && extraSel == -2) { OpenEnginePicker(); break; }
                    if (chipFocus && extraSel == -1) { OpenInputFilter(); break; }
                    if (chipFocus && extraSel > 0) { var ex = Extras(); if (extraSel <= ex.Count) ex[extraSel - 1].Value(); break; }
                    if (chipFocus) { chipFocus = false; Shell.FocusChanged(); } else if (FocusedGame != null) OpenDetail(FocusedGame); break;
                case Pad.B: Shell.Pop(); break;
                case Pad.Y: Shell.Push(new SearchScreen()); break;
            }
        }

        public override void OnTap(Point p)
        {
            for (int i = 0; i < tabRects.Length; i++) if (tabRects[i].Contains(p)) { SetTab(i); return; }
            if (TkTab)
            {
                if (tagBtn.Contains(p)) { if (tagSel != null && p.X > tagBtn.Right - S(40)) { tagSel = null; OnGamesChanged(); } else OpenTagPicker(); return; }
                if (engBtn.Contains(p)) { if (engSel != null && p.X > engBtn.Right - S(40)) { engSel = null; OnGamesChanged(); } else OpenEnginePicker(); return; }
                if (inpBtn.Contains(p)) { if (InpOn && p.X > inpBtn.Right - S(40)) { inpSel = new[] { '-', '-', '-', '-' }; OnGamesChanged(); } else OpenInputFilter(); return; }
                if (chipLeftR.Contains(p)) { chipScroll -= chipArea.Width * 2 / 3; Shell.Invalidate(); return; }
                if (chipRightR.Contains(p)) { chipScroll += chipArea.Width * 2 / 3; Shell.Invalidate(); return; }
                for (int i = 0; i < chipRects.Length; i++) if (chipRects[i].Contains(p) && chipArea.Contains(p)) { chipFocus = false; PickChip(i); return; }
                var exs = Extras();
                for (int i = 0; i < extraRects.Length && i < exs.Count; i++) if (extraRects[i].Contains(p)) { exs[i].Value(); return; }
            }
            if (MaxScroll > 0 && BarZone.Contains(p)) { BarJump(p.Y); return; }
            if (!GridArea.Contains(p)) return;
            if (newChkR.Contains(p)) { OpenNewDups(); return; }
            if (newEndR.Contains(p)) { EndNew(); return; }
            for (int i = 0; i < items.Count; i++)
                if (CapRect(i).Contains(p)) { focus = i; Shell.FocusChanged(); OpenDetail(items[i]); return; }
        }

        public override void OnDrag(int dx, int dy, bool start)
        {
            if (start)
            {
                dragStart = scroll; dragging = true; barDrag = MaxScroll > 0 && BarZone.Contains(DownAt);
                chipDrag = TkTab && chipArea.Contains(DownAt); chipScrollStart = chipScroll;
            }
            if (dx == int.MinValue) { dragging = false; barDrag = false; chipDrag = false; target = Math.Max(0, Math.Min(MaxScroll, scroll)); Shell.Animate(); return; }
            if (chipDrag) { chipScroll = Math.Max(0, Math.Min(Math.Max(0, chipTotalW - chipArea.Width), chipScrollStart - dx)); return; }
            if (barDrag)
            {
                // 손잡이를 끌면 목록 전체 길이에 맞춰 크게 움직임
                float per = MaxScroll / Math.Max(1f, BarZone.Height - ThumbH);
                scroll = target = Math.Max(0, Math.Min(MaxScroll, dragStart + dy * per));
                return;
            }
            scroll = Math.Max(-S(60), Math.Min(MaxScroll + S(60), dragStart - dy));
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "열기", () => OnPad(Pad.A)),
            new Hint(Pad.Y, "검색", () => Shell.Push(new SearchScreen())),
        }.Concat(TkTab ? new List<Hint> { new Hint(Pad.X, "정렬", NextSort) } : new List<Hint>()).Concat(new List<Hint>
        {
            new Hint(Pad.RB, "탭 이동", () => SetTab(tab + 1)),
        }).ToList();
    }

    // ═════════════════════ 쯔꾸르 분류 칸 순서·숨김 ═════════════════════
    class ChipEditScreen : Screen
    {
        public class Item { public string Name; public bool Hidden; }
        static readonly string[] All = { "전체", "플레이 중", "완료", "한글", "일본어", "체험판", "숨김" };
        List<Item> items;
        int focus;
        Rectangle[] rowR = new Rectangle[0], upR = new Rectangle[0], downR = new Rectangle[0];

        /// <summary>저장된 순서 (없는 이름은 기본 순서로 뒤에)</summary>
        public static List<Item> Order()
        {
            var list = new List<Item>();
            foreach (var part in (Settings.ChipOrder ?? "").Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                bool hid = p.StartsWith("-");
                string n = hid ? p.Substring(1) : p;
                if (Array.IndexOf(All, n) >= 0 && !list.Any(x => x.Name == n)) list.Add(new Item { Name = n, Hidden = hid });
            }
            foreach (var n in All) if (!list.Any(x => x.Name == n)) list.Add(new Item { Name = n });
            return list;
        }

        void Save()
        {
            Settings.ChipOrder = string.Join(",", items.Select(x => (x.Hidden ? "-" : "") + x.Name));
            Settings.Save();
        }

        public override void OnGamesChanged() { if (items == null) items = Order(); }

        void MoveItem(int i, int d)
        {
            int j = i + d;
            if (i < 0 || j < 0 || j >= items.Count) return;
            var t = items[i]; items[i] = items[j]; items[j] = t;
            focus = j;
            Save();
        }

        public override void Paint(Graphics g)
        {
            if (items == null) items = Order();
            int pad = S(40);
            Draw.Text(g, "쯔꾸르 분류 칸", Theme.Px(20, true), new Rectangle(Area.X + pad, Area.Y + S(12), S(500), S(44)), Theme.Text, Draw.Left);
            Draw.Text(g, "A 보이기/숨기기 · LB/RB 위아래로 옮기기", Draw.Body, new Rectangle(Area.Right - pad - S(600), Area.Y + S(12), S(600), S(44)), Theme.SubText, Draw.Right);
            int cols = 2, rowH = S(58), colW = (Area.Width - pad * 2 - S(20)) / cols;
            int perCol = (items.Count + 1) / 2;
            if (rowR.Length != items.Count) { rowR = new Rectangle[items.Count]; upR = new Rectangle[items.Count]; downR = new Rectangle[items.Count]; }
            for (int i = 0; i < items.Count; i++)
            {
                int c = i / perCol, r = i % perCol;
                var rr = new Rectangle(Area.X + pad + c * (colW + S(20)), Area.Y + S(70) + r * rowH, colW, rowH - S(8));
                rowR[i] = rr;
                Draw.Fill(g, rr, S(6), Theme.Tile);
                Draw.Text(g, (i + 1) + ".  " + items[i].Name, Draw.BodyBold, new Rectangle(rr.X + S(16), rr.Y, rr.Width / 2, rr.Height),
                    items[i].Hidden ? Theme.Dim : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                Draw.Text(g, items[i].Hidden ? "숨김" : "보임", Draw.Body, new Rectangle(rr.Right - S(200), rr.Y, S(70), rr.Height),
                    items[i].Hidden ? Theme.Dim : Theme.Accent, Draw.Center);
                upR[i] = new Rectangle(rr.Right - S(120), rr.Y + S(5), S(52), rr.Height - S(10));
                downR[i] = new Rectangle(rr.Right - S(62), rr.Y + S(5), S(52), rr.Height - S(10));
                Draw.Fill(g, upR[i], S(4), Color.FromArgb(50, 255, 255, 255));
                Draw.Fill(g, downR[i], S(4), Color.FromArgb(50, 255, 255, 255));
                Draw.Text(g, "▲", Draw.Body, upR[i], Theme.Text, Draw.Center);
                Draw.Text(g, "▼", Draw.Body, downR[i], Theme.Text, Draw.Center);
                if (i == focus && Shell.ShowFocus) Draw.FocusGlow(g, rr, S(6));
            }
        }

        public override void OnPad(Pad b)
        {
            if (items == null) items = Order();
            int perCol = (items.Count + 1) / 2;
            switch (b)
            {
                case Pad.Up: focus = Math.Max(0, focus - 1); break;
                case Pad.Down: focus = Math.Min(items.Count - 1, focus + 1); break;
                case Pad.Left: focus = Math.Max(0, focus - perCol); break;
                case Pad.Right: focus = Math.Min(items.Count - 1, focus + perCol); break;
                case Pad.A: if (items[focus].Name != "전체") { items[focus].Hidden = !items[focus].Hidden; Save(); } else Shell.ShowToast("'전체'는 뺄 수 없어요", 1500); break;
                case Pad.LB: MoveItem(focus, -1); break;
                case Pad.RB: MoveItem(focus, 1); break;
                case Pad.B: Shell.Pop(); Shell.RefreshLists(); return;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            for (int i = 0; i < rowR.Length; i++)
            {
                if (upR[i].Contains(p)) { MoveItem(i, -1); Shell.Invalidate(); return; }
                if (downR[i].Contains(p)) { MoveItem(i, 1); Shell.Invalidate(); return; }
                if (rowR[i].Contains(p)) { focus = i; OnPad(Pad.A); return; }
            }
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, "보이기/숨기기", () => OnPad(Pad.A)),
            new Hint(Pad.LB, "위로", () => OnPad(Pad.LB)),
            new Hint(Pad.RB, "아래로", () => OnPad(Pad.RB)),
            new Hint(Pad.B, "끝", () => OnPad(Pad.B)),
        };
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

        public static List<string> Candidates(GameInfo g)
        {
            var c = new List<string>();
            Action<string> add = t => { if (!string.IsNullOrWhiteSpace(t) && !c.Contains(t.Trim())) c.Add(t.Trim()); };
            add(g.Title);
            add(NameFinder.ReadmeName(g));
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
                    Shell.ManualCover(g);   // 게임을 켜고, 표지로 쓸 화면에서 Y 두 번
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
                "표지 직접 찍기 (켜고 Y 두 번)",
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
        Rectangle playBtn, favBtn, hideBtn, resBtn, clearBtn, prevR, nextR;
        /// <summary>목록에서 들어왔으면 그 목록 (LB/RB·좌우로 밀기·‹ ›로 앞뒤 게임)</summary>
        public List<GameInfo> List;
        public Action<GameInfo> OnMove;

        public DetailScreen(GameInfo g) { game = g; }
        public DetailScreen(GameInfo g, List<GameInfo> list, Action<GameInfo> onMove = null) { game = g; List = list; OnMove = onMove; }

        /// <summary>앞뒤 게임으로 (화면을 새로 만들어 바꿔 끼움)</summary>
        void Go(int d)
        {
            if (List == null || List.Count < 2) return;
            int i = List.IndexOf(game);
            if (i < 0) return;
            int j = i + d;
            if (j < 0 || j >= List.Count) { Shell.ShowToast(d < 0 ? "맨 처음 게임이에요" : "마지막 게임이에요", 1200); return; }
            var next = new DetailScreen(List[j], List, OnMove) { focus = focus };
            try { OnMove?.Invoke(List[j]); } catch { }
            Shell.Pop();
            Shell.Push(next);
        }
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
            Draw.Text(g, "더보기", Draw.BodyBold, hideBtn, Theme.SubText, Draw.Center);
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

            PaintDesc(g, new Rectangle(pad, y0, descW, bottom - y0));

            // 앞뒤 게임 ‹ › (목록에서 들어왔을 때)
            prevR = nextR = Rectangle.Empty;
            if (List != null && List.Count > 1)
            {
                int idx = List.IndexOf(game);
                int ah = S(90), aw = S(46), ay = Area.Y + S(70);
                if (idx > 0)
                {
                    prevR = new Rectangle(Area.X + S(10), ay, aw, ah);
                    Draw.Fill(g, prevR, S(10), Color.FromArgb(120, 0, 0, 0));
                    Draw.Text(g, "‹", Theme.Px(30, true), prevR, Theme.Text, Draw.Center);
                }
                if (idx >= 0 && idx < List.Count - 1)
                {
                    nextR = new Rectangle(Area.Right - aw - S(10), ay, aw, ah);
                    Draw.Fill(g, nextR, S(10), Color.FromArgb(120, 0, 0, 0));
                    Draw.Text(g, "›", Theme.Px(30, true), nextR, Theme.Text, Draw.Center);
                }
                if (idx >= 0)
                    Draw.Text(g, (idx + 1) + " / " + List.Count, Draw.Small, new Rectangle(Area.Right - S(200), Area.Y + S(12), S(180), S(24)), Theme.SubText, Draw.Right);
            }

            Draw.Text(g, "정보", Draw.H2, new Rectangle(ix0, y0, S(300), S(30)), Theme.Text);
            // 정보가 길어 밑으로 넘치면 위아래로 밀어 봄 (정보 칸만 잘라서 그림)
            infoRect = new Rectangle(ix0 - S(6), y0 + S(38), infoW + S(12), bottom - (y0 + S(38)));
            var oldClip = g.Clip;
            g.SetClip(infoRect);
            int y = y0 + S(42) - infoScroll;
            int yStart = y;
            int bx = ix0;
            // 조작 방식 (맨 위): 패드·키보드·마우스·터치 — 누르면 직접 고치기
            {
                var inp = InputNeeds.Get(game);
                int x0 = bx;
                Draw.Text(g, "조작", Draw.Small, new Rectangle(bx, y, S(44), S(26)), Theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                bx += S(46);
                for (int k = 0; k < 4; k++)
                {
                    char c = inp.S[k];
                    Color col = c == 'n' ? Color.FromArgb(200, 120, 40) : c == 'o' ? Color.FromArgb(46, 130, 80) : c == 'x' ? Color.FromArgb(120, 60, 60) : Color.FromArgb(70, 80, 96);
                    string lb = InputNeeds.Label(k, c);
                    if (bx + Draw.Measure(g, lb, Draw.Small) + S(20) > ix0 + infoW) { y += S(30); bx = ix0 + S(46); }
                    Draw.Badge(g, ref bx, y, lb, col);
                }
                if (inp.User) Draw.Badge(g, ref bx, y, "직접 고침", Color.FromArgb(70, 80, 96));
                inputRect = new Rectangle(x0, y - S(4), ix0 + infoW - x0, S(34));
                y += S(30); bx = ix0;
                if (inp.Keys.Count > 0)
                {
                    string kt = "필요한 키: " + string.Join(" · ", inp.Keys) + (inp.Unmapped.Count > 0 ? "   (패드에 없음: " + string.Join(" · ", inp.Unmapped) + ")" : "");
                    Draw.Text(g, kt, Draw.Small, new Rectangle(ix0, y - S(2), infoW, S(24)), inp.Unmapped.Count > 0 ? Color.FromArgb(230, 170, 90) : Theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    y += S(26);
                }
                if (inp.Note.Length > 0 && !inp.User)
                {
                    Draw.Text(g, inp.Note, Draw.Small, new Rectangle(ix0, y - S(2), infoW, S(24)), Theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    y += S(26);
                }
            }
            // 엔진
            string eng = game.IsTkool ? "RPG MAKER " + game.VersionLabel.ToUpperInvariant() : (EngineName() ?? "엔진 모름") + (game.Kind == GameKind.Pc ? "" : " · " + game.VersionLabel);
            Draw.Badge(g, ref bx, y, eng, Theme.GameColor(game));
            if (game.Manual) Draw.Badge(g, ref bx, y, "직접 추가", Color.FromArgb(70, 80, 96));
            y += S(30); bx = ix0;
            // 언어
            bool ko = LibraryScreen.IsKorean(game);
            Draw.Badge(g, ref bx, y, ko ? "한국어" : "일본어", ko ? Color.FromArgb(52, 110, 180) : Color.FromArgb(186, 62, 62));
            if (GameData.Get(game).Lang != 0) Draw.Badge(g, ref bx, y, "직접 고침", Color.FromArgb(70, 80, 96));
            y += S(30); bx = ix0;
            // 번역 방식
            var tr = TransKinds();
            if (tr.Count > 0)
            {
                foreach (var t in tr)
                {
                    int w = Draw.Measure(g, t, Draw.Small) + S(20);
                    if (bx + w > ix0 + infoW) { y += S(30); bx = ix0; }
                    Draw.Badge(g, ref bx, y, t, Color.FromArgb(120, 90, 160));
                }
                y += S(30); bx = ix0;
            }
            if (game.NeedsRtp)
            {
                Draw.Badge(g, ref bx, y, "RTP 필요" + (string.IsNullOrEmpty(game.RtpName) ? "" : " (" + game.RtpName + ")"), Color.FromArgb(70, 80, 96));
                y += S(30); bx = ix0;
            }
            // DLsite 서클·장르 (최대 2줄)
            var dl = DlsiteInfo.Get(game);
            if (dl != null)
            {
                int lines = 0;
                var items = new List<string>();
                if (!string.IsNullOrEmpty(dl.Circle)) items.Add("서클 " + dl.Circle);
                for (int k = 0; k < dl.Genres.Count; k++) items.Add(DlsiteInfo.GenreKo(dl.Genres[k]));
                foreach (var t in items)
                {
                    int w = Draw.Measure(g, t, Draw.Small) + S(20);
                    if (bx + w > ix0 + infoW) { if (++lines >= 2) break; y += S(30); bx = ix0; }
                    Draw.Badge(g, ref bx, y, t, t.StartsWith("서클 ") ? Color.FromArgb(150, 95, 40) : Color.FromArgb(110, 70, 130));
                }
                y += S(30); bx = ix0;
            }
            // 내 태그 + 추가
            tagRects.Clear();
            foreach (var t in GameData.TagsOf(game))
            {
                int w = Draw.Measure(g, "# " + t, Draw.Small) + S(20);
                if (bx + w > ix0 + infoW) { y += S(30); bx = ix0; }
                int x0 = bx;
                Draw.Badge(g, ref bx, y, "# " + t, Color.FromArgb(40, 120, 110));
                tagRects.Add(new Rectangle(x0, y, bx - x0, S(26)));
            }
            {
                int w = Draw.Measure(g, "+ 태그", Draw.Small) + S(20);
                if (bx + w > ix0 + infoW) { y += S(30); bx = ix0; }
                int x0 = bx;
                Draw.Badge(g, ref bx, y, "+ 태그", Color.FromArgb(60, 70, 86));
                addTagRect = new Rectangle(x0, y - S(4), bx - x0, S(34));
                y += S(30);
            }
            y += S(8);
            string nf = GameData.Get(game).NameFrom;
            if (nf != null && nf != "-")
            {
                Draw.Text(g, "원래 이름: " + nf, Draw.Small, new Rectangle(ix0, y, infoW, S(40)), Color.FromArgb(150, 190, 235),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                y += S(42);
            }
            Draw.Text(g, game.Folder, Draw.Small, new Rectangle(ix0, y, infoW, S(40)), Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += S(46);
            g.Clip = oldClip;
            int contentH = y - yStart, maxS = Math.Max(0, contentH - infoRect.Height);
            if (infoScroll > maxS) infoScroll = maxS;
            if (maxS > 0)
            {
                // 오른쪽 가는 막대로 남은 양 표시
                int th = Math.Max(S(30), infoRect.Height * infoRect.Height / Math.Max(1, contentH));
                int ty = infoRect.Y + (int)((infoRect.Height - th) * (infoScroll / (float)maxS));
                Draw.Fill(g, new Rectangle(infoRect.Right - S(4), ty, S(4), th), S(2), Color.FromArgb(120, 255, 255, 255));
            }
            infoMax = maxS;
        }
        Rectangle infoRect;
        int infoScroll, infoMax, infoDragStart;
        bool infoDrag;

        // ── 소개: 구글 번역 + 원문, 스크롤 ──
        Rectangle descRect, addTagRect, inputRect;

        /// <summary>조작 방식 직접 고치기: 줄마다 누르면 됨 → 꼭 필요 → 안 됨 → 모름 차례로</summary>
        void EditInput()
        {
            var r = InputNeeds.Get(game);
            var cur = (char[])r.S.Clone();
            Action show = null;
            show = () =>
            {
                var opts = new List<string>();
                for (int k = 0; k < 4; k++) opts.Add(InputNeeds.Names[k] + ": " + InputNeeds.StateWord(cur[k]));
                opts.Add("✓ 저장");
                opts.Add("자동으로 되돌리기");
                PickerScreen.Show(Shell, "조작 방식 (누를 때마다 바뀜)", opts.ToArray(), -1, i =>
                {
                    if (i < 4) { cur[i] = cur[i] == 'o' ? 'n' : cur[i] == 'n' ? 'x' : cur[i] == 'x' ? '?' : 'o'; show(); return; }
                    if (i == 4) { InputNeeds.SetUser(game, new string(cur)); Shell.ShowToast("조작 방식을 저장했어요", 2000); }
                    else { InputNeeds.SetUser(game, null); Shell.ShowToast("자동 판단으로 되돌렸어요", 2000); }
                    Shell.Invalidate();
                }, "됨 → 꼭 필요 → 안 됨 → 모름");
            };
            show();
        }
        readonly List<Rectangle> tagRects = new List<Rectangle>();
        int descScroll, descContentH, descDragStart;
        bool descDrag;
        List<string> transKinds;
        string engineName; bool engineDone;

        string EngineName()
        {
            if (!engineDone) { engineDone = true; engineName = game.Engine ?? EngineDetect.Detect(game.InstallDir ?? game.Folder, game.Exe); game.Engine = engineName; }
            return engineName;
        }

        List<string> TransKinds()
        {
            if (transKinds == null)
            {
                transKinds = new List<string>();
                if (game.FromFolder || game.IsTkool)
                    try { transKinds = TransDetect.Detect(game, GameDesc.Raw(game)).Select(x => "번역: " + x).ToList(); } catch { }
            }
            return transKinds;
        }

        void PaintDesc(Graphics g, Rectangle area)
        {
            string desc = GameDesc.Get(game);
            bool needTr = desc != null && Translate.Needs(desc);
            string tr = needTr ? Translate.Get(GameData.IdOf(game) + "_" + ((uint)desc.GetHashCode()).ToString("x8"), desc) : null;   // 글이 바뀌면(예: 글자 깨짐 고침) 새로 번역
            Draw.Text(g, "소개", Draw.H2, new Rectangle(area.X, area.Y, S(80), S(30)), Theme.Text);
            if (needTr)
                Draw.Text(g, tr == null ? "(구글 번역 중…)" : tr.Length > 0 ? "(구글 번역)" : "(번역 안 됨 — 잠시 뒤 다시 시도, 원문)", Draw.Body,
                    new Rectangle(area.X + S(78), area.Y, S(300), S(30)), Color.FromArgb(90, 170, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            var dr = new Rectangle(area.X, area.Y + S(40), area.Width, area.Height - S(40));
            descRect = dr;
            Draw.Fill(g, Rectangle.Inflate(dr, S(10), S(8)), S(6), Color.FromArgb(110, 23, 29, 37));
            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
            var blocks = new List<Tuple<string, Font, Color>>();
            if (desc == null) blocks.Add(Tuple.Create("불러오는 중...", Draw.Body, Theme.SubText));
            else if (desc.Length == 0) blocks.Add(Tuple.Create("소개 글이 없어요. 게임 폴더에 readme.txt 같은 설명 파일이 있으면 여기에 보여줘요.", Draw.Body, Theme.SubText));
            else if (needTr && !string.IsNullOrEmpty(tr))
            {
                blocks.Add(Tuple.Create(tr, Draw.Body, Color.FromArgb(170, 210, 255)));
                blocks.Add(Tuple.Create("—", Draw.Small, Color.Empty));   // 구분선
                blocks.Add(Tuple.Create("원문", Draw.Small, Theme.SubText));
                blocks.Add(Tuple.Create(desc, Draw.Body, Theme.SubText));
            }
            else blocks.Add(Tuple.Create(desc, Draw.Body, Theme.Text));

            var oc = g.Clip;
            g.SetClip(dr, System.Drawing.Drawing2D.CombineMode.Intersect);
            int w = dr.Width - S(18), yy = dr.Y - descScroll, total = 0;
            foreach (var b in blocks)
            {
                if (b.Item3 == Color.Empty)
                {
                    using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1, S(1)))) g.DrawLine(pen, dr.X, yy + S(10), dr.X + w, yy + S(10));
                    yy += S(20); total += S(20);
                    continue;
                }
                int h = TextRenderer.MeasureText(g, b.Item1, b.Item2, new Size(w, int.MaxValue), flags).Height;
                if (yy + h > dr.Y && yy < dr.Bottom) Draw.Text(g, b.Item1, b.Item2, new Rectangle(dr.X, yy, w, h), b.Item3, flags);
                yy += h + S(8); total += h + S(8);
            }
            g.Clip = oc;
            descContentH = total;
            int max = Math.Max(0, descContentH - dr.Height);
            if (descScroll > max) descScroll = max;
            if (max > 0)
            {
                int th = Math.Max(S(30), dr.Height * dr.Height / (descContentH));
                int ty = dr.Y + (int)((dr.Height - th) * (descScroll / (float)max));
                Draw.Fill(g, new Rectangle(dr.Right - S(6), ty, S(5), th), S(2), Color.FromArgb(140, 255, 255, 255));
            }
        }

        void ScrollDesc(int d)
        {
            int max = Math.Max(0, descContentH - descRect.Height);
            descScroll = Math.Max(0, Math.Min(max, descScroll + d));
            Shell.Invalidate();
        }

        int swipeDx, swipeDy;
        public override void OnDrag(int dx, int dy, bool start)
        {
            if (start) { descDrag = descRect.Contains(DownAt); infoDrag = !descDrag && infoRect.Contains(DownAt); infoDragStart = infoScroll; descDragStart = descScroll; swipeDx = swipeDy = 0; }
            if (dx != int.MinValue && infoDrag && Math.Abs(dy) >= Math.Abs(dx)) { infoScroll = Math.Max(0, Math.Min(infoMax, infoDragStart - dy)); swipeDx = dx; swipeDy = dy; Shell.Invalidate(); return; }
            if (dx == int.MinValue)
            {
                // 옆으로 크게 밀면 앞뒤 게임
                if (!descDrag && Math.Abs(swipeDx) > S(140) && Math.Abs(swipeDx) > Math.Abs(swipeDy) * 2) Go(swipeDx < 0 ? 1 : -1);
                descDrag = false; infoDrag = false; return;
            }
            swipeDx = dx; swipeDy = dy;
            if (descDrag && Math.Abs(dy) >= Math.Abs(dx)) { descScroll = descDragStart; ScrollDesc(-dy); }
        }

        /// <summary>이름 고치기: 후보(원래 이름·읽어보기·DLsite 원제·번역)에서 고르거나 직접 입력</summary>
        /// <summary>이 게임 이름 찾기: 인터넷·읽어보기 → 가진 그림 글자 → 그래도 없으면 켜서 창 제목·타이틀 화면 글자</summary>
        void FindName()
        {
            var g = game;
            Shell.ShowToast("이름 찾는 중…", 2000);
            new System.Threading.Thread(() =>
            {
                Activity.Set("name1", "이름 찾는 중 · " + g.Title, 0, 0);
                bool changed = false;
                try { changed = NameFinder.FindOne(g); } catch (Exception ex) { Log.Info("이름 찾기 " + ex.Message); }
                bool still = NameFinder.Meaningless(g, g.Title) || NameFinder.Foreign(g.Title);
                Activity.Done("name1", changed ? "이름 찾음 · " + g.Title : "이름 못 찾음 · " + g.Title);
                try
                {
                    Shell.BeginInvoke(new Action(() =>
                    {
                        Shell.RefreshLists(); CoverArt.Invalidate(g); Shell.Invalidate();
                        if (changed && !still) { Shell.ShowToast("이름을 찾았어요: " + g.Title, 4000); return; }
                        if (GameWatch.Running) { Shell.ShowToast(changed ? "이름: " + g.Title : "인터넷에서 못 찾았어요", 4000); return; }
                        PickerScreen.Confirm(Shell, changed ? "'" + g.Title + "' — 게임을 켜서 더 찾아볼까요?" : "인터넷에서 못 찾았어요. 게임을 켜서 찾아볼까요?", "켜서 찾기",
                            () => Shell.FindCovers(new List<GameInfo> { g }),
                            "잠깐 켜서 창 제목과 타이틀 화면 글자를 읽고 꺼요 (표지가 없으면 표지도 찍어요)");
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        void RenameMenu(GameStat st)
        {
            var c = TidyScreen.Candidates(game);
            Action<string> add = t => { if (!string.IsNullOrWhiteSpace(t) && !c.Contains(t.Trim())) c.Add(t.Trim()); };
            if (st.NameFrom != null && st.NameFrom.Length > 2) add(st.NameFrom.Split(new[] { " · " }, StringSplitOptions.None)[0]);
            var dl = DlsiteInfo.Get(game);
            if (dl != null) add(dl.Name);
            foreach (var o in Ocr.Get(game)) add(o);   // 타이틀 화면에서 읽은 글자
            foreach (var fn in GameScanner.FolderNames(game.Folder)) add(TitleTidy.Clean(fn, new List<string>()));   // 폴더 이름 (2중 폴더면 위 폴더도)
            var opts = new List<string>(c) { "✎ 직접 입력하기" };
            PickerScreen.Show(Shell, "이름 고치기", opts.ToArray(), c.IndexOf(game.Title), k =>
            {
                string name = null;
                if (k == opts.Count - 1)
                {
                    name = InputDialog.Ask(Shell, "게임 이름", game.Title);
                    if (string.IsNullOrWhiteSpace(name)) return;
                    name = name.Trim();
                }
                else name = c[k];
                string orig = st.NameFrom != null && !st.NameFrom.StartsWith("-") ? st.NameFrom.Split(new[] { " · " }, StringSplitOptions.None)[0] : (game.RawTitle ?? game.Title);
                st.NameFrom = orig + NameFinder.ByUser;   // 다시 자동으로 안 바꾸게 (원래 이름은 남김)
                game.Title = name; st.Title = "=" + name; GameData.Save(); Shell.RefreshLists();
                Shell.ShowToast("이름을 '" + name + "'(으)로 바꿨어요", 2000);
            }, "원래 이름: " + (game.RawTitle ?? game.Title));
        }

        // 태그 고르기: 목록 + 새 태그 만들기
        void EditTags()
        {
            var all = (Settings.TagList ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
            var mine = GameData.TagsOf(game);
            foreach (var t in mine) if (!all.Contains(t)) all.Add(t);
            var opts = all.Select(t => (mine.Contains(t) ? "● " : "○ ") + t).ToList();
            opts.Add("+ 새 태그 만들기");
            PickerScreen.Show(Shell, "태그 (누르면 붙이기/떼기)", opts.ToArray(), -1, i =>
            {
                if (i == all.Count)
                {
                    string nt = InputDialog.Ask(Shell, "새 태그 이름", "");
                    if (string.IsNullOrWhiteSpace(nt)) return;
                    nt = nt.Replace(",", " ").Replace("|", " ").Trim();
                    if (!all.Contains(nt)) { all.Add(nt); Settings.TagList = string.Join(",", all); Settings.Save(); }
                    if (!mine.Contains(nt)) mine.Add(nt);
                }
                else
                {
                    string t = all[i];
                    if (mine.Contains(t)) mine.Remove(t); else mine.Add(t);
                }
                GameData.Get(game).UserTags = string.Join(",", mine);
                GameData.Save();
                EditTags();   // 여러 개 고르기 편하게 다시 열기
            });
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
                case Pad.Up: ScrollDesc(-S(90)); return;
                case Pad.Down: ScrollDesc(S(90)); return;
                case Pad.A: if (focus == 0) Shell.Launch(game); else if (focus == 1) ToggleFav(); else if (focus == 2) More(); else if (focus == 3) NextRes(); else ToggleCleared(); break;
                case Pad.X: EditTags(); return;
                case Pad.Y: ToggleFav(); break;
                case Pad.LB: Go(-1); return;
                case Pad.RB: Go(1); return;
                case Pad.B: Shell.Pop(); break;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            if (prevR.Contains(p)) { Go(-1); return; }
            if (nextR.Contains(p)) { Go(1); return; }
            if (playBtn.Contains(p)) { focus = 0; Shell.Launch(game); }
            else if (favBtn.Contains(p)) { focus = 1; ToggleFav(); }
            else if (hideBtn.Contains(p)) { focus = 2; More(); }
            else if (resBtn.Contains(p)) { focus = 3; NextRes(); }
            else if (clearBtn.Contains(p)) { focus = 4; ToggleCleared(); }
            else if (inputRect.Contains(p)) EditInput();
            else if (addTagRect.Contains(p) || tagRects.Any(r => r.Contains(p))) EditTags();
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
            string cur = ResForce.CurrentRes();
            var opts = new List<string> { null };
            foreach (var r in list) if (!opts.Contains(r)) opts.Add(r);
            var names = opts.Select(r => r == null ? "그대로 (바꾸지 않음)" : r + (r == cur ? "  (지금 화면)" : "")).ToArray();
            PickerScreen.Show(Shell, "이 게임을 켤 때 해상도", names, Math.Max(0, opts.IndexOf(st.Res)), i =>
            {
                st.Res = opts[i];
                GameData.Save();
                Shell.ShowToast(st.Res == null ? "이 게임은 해상도를 바꾸지 않아요" : "이 게임을 켜면 " + st.Res + "로 바꾸고, 끄면 되돌려요", 2500);
            }, "끄면 원래 해상도로 돌아와요");
        }

        /// <summary>더보기: 표지 직접 찍기 · 표지 지우기 · 언어 · 이름 · 숨기기</summary>
        void More()
        {
            var st = GameData.Get(game);
            string[] langs = { "자동", "한글", "일본어" };
            string[] wins = { "그대로", "꽉 찬 창 (작업 표시줄 보임)" };
            var opts = new[]
            {
                "표지 직접 찍기 (게임 켜고 Y 두 번)",
                "표지 지우기",
                "언어: " + langs[Math.Max(0, Math.Min(2, st.Lang))] + (st.Lang == 0 ? " (" + (LibraryScreen.IsKorean(game) ? "한글" : "일본어") + "로 보임)" : ""),
                "이름 바꾸기",
                "이름 찾기 (인터넷 → 타이틀 그림 글자 → 게임 켜서)",
                st.Hidden ? "다시 보이기 (숨김 풀기)" : "숨기기",
                "게임 위치 열기 (탐색기)",
                "창 모양: " + wins[Math.Max(0, Math.Min(1, st.WinMode))],
                "일본어 환경으로 켜기: " + (JpLocale.Has(game) ? "켬" : "끔"),
                "조작 방식 고치기 (패드·키보드·마우스·터치)",
                "패드 → 키보드 바꾸기: " + PadMap.Label(game),
            };
            PickerScreen.Show(Shell, game.Title, opts, -1, i =>
            {
                switch (i)
                {
                    case 0: Shell.ManualCover(game); break;
                    case 1:
                        PickerScreen.Confirm(Shell, "표지를 지울까요?", "지우기", () =>
                        {
                            CoverArt.DeleteCover(game);
                            Shell.ShowToast("표지를 지웠어요", 2000);
                        }, "직접 찍은 표지·자동으로 찍은 표지를 지우고 기본 그림으로 돌아가요");
                        break;
                    case 2:
                        PickerScreen.Show(Shell, "언어", langs, st.Lang, k => { st.Lang = k; GameData.Save(); Shell.RefreshLists(); });
                        break;
                    case 3:
                        RenameMenu(st);
                        break;
                    case 4: FindName(); break;
                    case 5:
                        if (st.Hidden) { st.Hidden = false; GameData.Save(); Shell.ShowToast("다시 보이게 했어요", 1500); Shell.RefreshLists(); }
                        else Hide();
                        break;
                    case 6: DupFinder.OpenFolder(game); break;
                    case 7:
                        PickerScreen.Show(Shell, "창 모양", wins, st.WinMode, k => { st.WinMode = k; GameData.Save(); },
                            "꽉 찬 창: 테두리 없이 작업 표시줄만 남기고 화면을 채움 (다음 실행부터)");
                        break;
                    case 9: EditInput(); break;
                    case 10:
                        PickerScreen.Show(Shell, "패드 → 키보드 바꾸기", new[] { "자동 (쯔꾸르 2000~VX Ace만)", "켬 (A=Z·B=X·X=Shift·Y=Esc…)", "끔 (게임이 패드를 직접 읽음)" },
                            PadMap.Get(game) == "on" ? 1 : PadMap.Get(game) == "off" ? 2 : 0,
                            pi => { PadMap.Set(game, pi == 1 ? "on" : pi == 2 ? "off" : null); Shell.ShowToast("다음 실행부터 적용 — 지금 게임 중이면 다시 켜 주세요", 3000); },
                            "버튼을 누를 때 두 번 눌리거나 화면이 깜빡이면 '끔', 패드가 아예 안 먹으면 '켬'");
                        break;
                    case 8:
                        {
                            var g8 = game; bool on = !JpLocale.Has(g8);
                            JpLocale.Set(g8, on);
                            Shell.ShowToast(on ? "다음부터 일본어 환경으로 켜요 (글자 깨짐·파일 못 찾음 오류용)" : "일본어 환경을 껐어요", 3000);
                            if (on) new System.Threading.Thread(() => JpLocale.Ensure()) { IsBackground = true }.Start();
                        }
                        break;
                }
            });
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, focus == 0 ? "플레이" : focus == 1 ? "즐겨찾기" : focus == 2 ? "더보기" : focus == 3 ? "해상도 고르기" : "다 깸 표시", () => OnPad(Pad.A)),
            new Hint(Pad.Y, "즐겨찾기", ToggleFav),
            new Hint(Pad.X, "태그", EditTags),
            new Hint(Pad.RB, "다음 게임", () => Go(1)),
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

        string[] Names => new[] { "라이브러리", "설정", "게임 폴더 설정", "게임 다시 찾기", "런처가 한 일", "런처 끄기" };

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
                case 1: Shell.GoHome(); Shell.Push(new SettingsScreen()); break;
                case 2: Shell.OpenFolders(); break;
                case 3: Shell.Rescan(); break;
                case 4: Shell.ShowActivityLog(); break;
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
        bool freeScroll; float dragBase;
        public override void OnDrag(int dx, int dy, bool start)
        {
            if (dx == int.MinValue) return;
            if (start) dragBase = scroll;
            freeScroll = true;
            scroll = dragBase - dy;
        }
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
                new Row { Title = "쯔꾸르 분류 칸", Sub = "쯔꾸르 탭 위 동그라미 칸 순서 바꾸기·빼기", Value = "정리", Act = () => Shell.Push(new ChipEditScreen()) },
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
                new Row { Title = "빈 표지 한 번에 찍기", Sub = "모든 게임을 확인해서 표지가 없는 것만 하나씩 켜 타이틀 화면을 찍어요. 인터넷에서 이름을 못 찾은 게임은 창 제목도 봐요 (오류 창은 부품 설치 후 다시, 홈 버튼으로 멈춤)",
                          Value = "전체 " + GameCapture.Candidates(Shell.Games).Count + "개 확인", Act = () => Shell.FindCovers() },
                new Row { Title = "빈 정보 지금 찾기", Sub = "그림·장르·소개가 없는 게임을 DLsite → 스팀 → FANZA → ふりーむ → itch.io에서 처음부터 다시 찾아요 (전에 못 찾은 것도)",
                          Act = () => { WebInfo.ResetNotFound(); DlsiteInfo.RunAll(Shell.Games, m => { try { Shell.BeginInvoke(new Action(() => { Shell.RefreshLists(); WebInfo.RunAll(Shell.Games, w => { try { Shell.BeginInvoke(new Action(Shell.RefreshLists)); } catch { } }); })); } catch { } }); Shell.ShowToast("빈 정보 찾기 시작 — 아래 왼쪽에 진행이 보여요", 2500); } },
                new Row { Title = "이름 다시 찾기", Sub = "RJ 번호·Game·짧은 영어 같은 이름이나 한글판 영어 이름을 읽어보기·DLsite에서 다시 찾아요",
                          Value = "전체 " + Shell.Games.Count(x => x.FromFolder) + "개", Act = () => { NameFinder.ResetTried(Shell.Games); NameFinder.RunAll(Shell.Games, true, n => { try { Shell.BeginInvoke(new Action(() => { Shell.RefreshLists(); Shell.ShowToast(n + "개의 이름을 찾았어요", 4000); })); } catch { } }); Shell.ShowToast("이름 찾는 중…", 2000); } },
                new Row { Title = "찍은 표지 확인", Sub = "한 번에 찾기로 찍은 표지를 늘어놓고, 아닌 것만 취소한 뒤 적용해요",
                          Value = GameCapture.Pending(Shell.Games).Count + "개", Act = () => Shell.Push(new CoverReviewScreen()) },
                new Row { Title = "표지 없는 게임 자동 표지", Toggle = Settings.AutoCover, Sub = "처음 켤 때 타이틀 화면을 찍어 표지로, 이름이 'Game'이면 창 제목으로 바꿔요",
                          Act = () => { Settings.AutoCover = !Settings.AutoCover; Settings.Save(); } },
                new Row { Title = "쯔꾸르 게임 자동 확대", Toggle = Settings.AutoScaleTkool, Sub = "2000~VX Ace 게임을 켜면 작은 창을 도트 선명하게 꽉 채워요",
                          Act = () => { Settings.AutoScaleTkool = !Settings.AutoScaleTkool; Settings.Save(); } },
                new Row { Title = "게임 중 작업 표시줄 보이기", Toggle = Settings.TaskbarInGame, Sub = "끄면 창 모드 게임도 런처 검은 화면 위에만 떠요 (게임 끝나면 다시 보여요)",
                          Act = () => { Settings.TaskbarInGame = !Settings.TaskbarInGame; Settings.Save(); if (Settings.TaskbarInGame) TaskbarHide.Show(); else if (Shell.PlayingGame != null) TaskbarHide.Hide(); } },
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
            if (!freeScroll)
            {
                float need = (focus + 1) * rowStep - visible;
                if (need > scroll) scroll = need;
                if (focus * rowStep < scroll) scroll = focus * rowStep;
            }
            scroll = Math.Min(scroll, Math.Max(0, list.Count * rowStep - visible));
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
            freeScroll = false;
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
