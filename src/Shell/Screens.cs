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

        public override GameInfo FocusedGame => focus < items.Count ? items[focus] : null;

        void BuildTabs()
        {
            var all = Shell.VisibleGames.ToList();
            tabs = new List<Tab>
            {
                new Tab { Name = "최근 플레이", Filter = x => GameData.Get(x).LastPlayed > 0 },
                new Tab { Name = "즐겨찾기", Filter = x => GameData.Get(x).Favorite },
                new Tab { Name = "전체", Filter = x => true },
            };
            var kinds = new[] { GameKind.Tkool, GameKind.Steam, GameKind.Epic, GameKind.Gog, GameKind.Pc };
            var names = new[] { "쯔꾸르", "Steam", "Epic", "GOG", "PC" };
            for (int i = 0; i < kinds.Length; i++)
            {
                var k = kinds[i];
                if (all.Any(x => x.Kind == k)) tabs.Add(new Tab { Name = names[i], Filter = x => x.Kind == k });
            }
            tab = Math.Max(0, tabs.FindIndex(t => t.Name == lastTab));
        }

        public override void OnGamesChanged()
        {
            var cur = FocusedGame;
            string keep = tabs.Count > 0 ? tabs[tab].Name : lastTab;
            BuildTabs();
            int ti = tabs.FindIndex(t => t.Name == keep);
            if (ti >= 0) tab = ti;
            var f = tabs[tab].Filter;
            items = tab == 0 ? Shell.Recent(int.MaxValue).Where(f).ToList() : Shell.VisibleGames.Where(f).ToList();
            int i = cur != null ? items.FindIndex(x => x.Exe == cur.Exe) : -1;
            focus = i >= 0 ? i : 0;
            Follow();
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
        int Cols => Math.Max(3, (Area.Width - Pad0 * 2 + Gap) / (S(150) + Gap));
        int CapW => (Area.Width - Pad0 * 2 - Gap * (Cols - 1)) / Cols;
        int CapH => CapW * 3 / 2;
        Rectangle GridArea => new Rectangle(Area.X, Area.Y + TabH, Area.Width, Area.Height - TabH);

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

            var clip = g.Clip;
            g.SetClip(GridArea);
            if (items.Count == 0)
            {
                string msg = tab == 1 ? "즐겨찾기한 게임이 없어요.\n게임 화면에서 ★를 누르면 여기에 모여요." :
                             tab == 0 ? "아직 플레이한 게임이 없어요." : "게임이 없어요.";
                if (tabs.Count == 0) msg = "";
                Draw.Text(g, msg, Draw.Menu, GridArea, Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
            for (int i = 0; i < items.Count; i++)
            {
                var r = CapRect(i);
                if (r.Bottom < GridArea.Top - S(20) || r.Top > GridArea.Bottom + S(20)) continue;
                bool f = i == focus && Shell.ShowFocus;
                if (f) r = Rectangle.Inflate(r, S(5), (int)(S(5) * 1.5f));
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
                case Pad.Left: Move(-1); break;
                case Pad.Right: Move(1); break;
                case Pad.Up: if (focus - Cols >= 0) Move(-Cols); break;
                case Pad.Down: if (focus + Cols < items.Count) Move(Cols); else if (focus / Cols < (items.Count - 1) / Cols) Move(items.Count - 1 - focus); break;
                case Pad.LB: SetTab(tab - 1); break;
                case Pad.RB: SetTab(tab + 1); break;
                case Pad.A: if (FocusedGame != null) Shell.Push(new DetailScreen(FocusedGame)); break;
                case Pad.B: Shell.Pop(); break;
                case Pad.Y: Shell.Push(new SearchScreen()); break;
            }
        }

        public override void OnTap(Point p)
        {
            for (int i = 0; i < tabRects.Length; i++) if (tabRects[i].Contains(p)) { SetTab(i); return; }
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
            new Hint(Pad.B, "뒤로", Shell.Pop),
            new Hint(Pad.RB, "탭 이동", () => SetTab(tab + 1)),
        };
    }

    // ═════════════════════ 게임 상세 ═════════════════════
    class DetailScreen : Screen
    {
        readonly GameInfo game;
        int focus; // 0 플레이, 1 즐겨찾기, 2 숨기기
        Rectangle playBtn, favBtn, hideBtn;

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

            // 기록
            int ix = hideBtn.Right + S(44);
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
                case Pad.Right: focus = Math.Min(2, focus + 1); break;
                case Pad.A: if (focus == 0) Shell.Launch(game); else if (focus == 1) ToggleFav(); else Hide(); break;
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
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, focus == 0 ? "플레이" : focus == 1 ? "즐겨찾기" : "숨기기", () => OnPad(Pad.A)),
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
                          Value = GameCapture.Missing(Shell.Games).Count + "개", Act = Shell.FindCovers },
                new Row { Title = "표지 없는 게임 자동 표지", Toggle = Settings.AutoCover, Sub = "처음 켤 때 타이틀 화면을 찍어 표지로, 이름이 'Game'이면 창 제목으로 바꿔요",
                          Act = () => { Settings.AutoCover = !Settings.AutoCover; Settings.Save(); } },
                new Row { Title = "쯔꾸르 게임 자동 확대", Toggle = Settings.AutoScaleTkool, Sub = "2000~VX Ace 게임을 켜면 작은 창을 도트 선명하게 꽉 채워요",
                          Act = () => { Settings.AutoScaleTkool = !Settings.AutoScaleTkool; Settings.Save(); } },
                new Row { Title = "AYA 큰 버튼 연결", Sub = learning == 1 ? "지금 AYA 큰 버튼을 누르세요..." : "빠른 설정 열기 · 패드 가이드 버튼, 보기+메뉴 동시 누름도 돼요",
                          Value = Hotkeys.Describe(Settings.HomeKey), Act = () => Learn(1) },
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
                if (Hardware.CreateAdminTask() && Hardware.RunAdminTask()) { Application.Exit(); return; }
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
                if (learning == 2) Settings.SmallKey = combo; else Settings.HomeKey = combo;
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
