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
                    Settings.GameFolders.Count == 0 ? "쯔꾸르 게임 폴더를 먼저 골라 주세요" : "등록한 폴더에서 쯔꾸르 게임을 못 찾았어요";
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
                Draw.Badge(g, ref x, y, gm.VersionLabel, Theme.VersionColor(gm.Version));
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
            h.Add(new Hint(Pad.Y, "라이브러리", () => Shell.Push(new LibraryScreen())));
            h.Add(new Hint(Pad.Back, "메뉴", Shell.OpenMenu));
            return h;
        }
    }

    // ═════════════════════ 라이브러리: 탭 + 격자 ═════════════════════
    class LibraryScreen : Screen
    {
        static readonly string[] TabNames = { "최근 플레이", "즐겨찾기", "전체" };
        static int lastTab = 2;
        int tab = lastTab;
        List<GameInfo> items = new List<GameInfo>();
        int focus;
        float scroll, target, dragStart;
        bool dragging;
        Rectangle[] tabRects = new Rectangle[3];

        public override GameInfo FocusedGame => focus < items.Count ? items[focus] : null;

        public override void OnGamesChanged()
        {
            var cur = FocusedGame;
            if (tab == 0) items = Shell.Recent(int.MaxValue).Where(x => GameData.Get(x).LastPlayed > 0).ToList();
            else if (tab == 1) items = Shell.Games.Where(x => GameData.Get(x).Favorite).ToList();
            else items = Shell.Games.ToList();
            int i = cur != null ? items.FindIndex(x => x.Exe == cur.Exe) : -1;
            focus = i >= 0 ? i : 0;
            Follow();
        }

        void SetTab(int t)
        {
            t = (t + 3) % 3;
            if (t == tab) return;
            tab = lastTab = t;
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
            var widths = new int[3];
            for (int i = 0; i < 3; i++) { widths[i] = Draw.Measure(g, TabNames[i], Draw.Menu) + S(40); totalW += widths[i]; }
            int x = Area.X + (Area.Width - totalW) / 2;
            Draw.PadGlyph(g, Pad.LB, x - S(50), cy);
            for (int i = 0; i < 3; i++)
            {
                var r = new Rectangle(x, Area.Y, widths[i], TabH);
                tabRects[i] = r;
                bool on = i == tab;
                Draw.Text(g, TabNames[i], on ? Theme.Px(17, true) : Draw.Menu, r, on ? Theme.Text : Theme.SubText, Draw.Center);
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
            }
        }

        public override void OnTap(Point p)
        {
            for (int i = 0; i < 3; i++) if (tabRects[i].Contains(p)) { SetTab(i); return; }
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
            new Hint(Pad.B, "뒤로", Shell.Pop),
            new Hint(Pad.RB, "탭 이동", () => SetTab(tab + 1)),
        };
    }

    // ═════════════════════ 게임 상세 ═════════════════════
    class DetailScreen : Screen
    {
        readonly GameInfo game;
        int focus; // 0 플레이, 1 즐겨찾기
        Rectangle playBtn, favBtn;

        public DetailScreen(GameInfo g) { game = g; }
        public override GameInfo FocusedGame => game;

        public override void Paint(Graphics g)
        {
            var full = Shell.ClientRectangle;
            int heroBottom = Area.Top + (int)(Area.Height * 0.52f);
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

            // 기록
            int ix = favBtn.Right + S(48);
            InfoBlock(g, ref ix, rowY + S(6), "마지막 플레이", GameData.LastPlayedText(st.LastPlayed));
            InfoBlock(g, ref ix, rowY + S(6), "플레이 시간", GameData.PlayTimeText(st.TotalSeconds));

            // 정보
            int y = rowY + S(104);
            Draw.Text(g, "정보", Draw.H2, new Rectangle(pad, y, S(300), S(30)), Theme.Text);
            y += S(42);
            int bx = pad;
            Draw.Badge(g, ref bx, y, "RPG MAKER " + game.VersionLabel.ToUpperInvariant(), Theme.VersionColor(game.Version));
            Draw.Badge(g, ref bx, y, game.Japanese ? "일본어 원어" : "한국어/기타", game.Japanese ? Color.FromArgb(186, 62, 62) : Color.FromArgb(70, 80, 96));
            if (game.NeedsRtp) Draw.Badge(g, ref bx, y, "RTP 필요" + (string.IsNullOrEmpty(game.RtpName) ? "" : " (" + game.RtpName + ")"), Color.FromArgb(70, 80, 96));
            y += S(36);
            Draw.Text(g, game.Folder, Draw.Small, new Rectangle(pad, y, full.Width - pad * 2, S(20)), Theme.SubText,
                Draw.Left | TextFormatFlags.PathEllipsis);
            y += S(26);
            Draw.Text(g, "표지 바꾸기: 내 문서\\쯔꾸르 런처\\covers 에 '" + System.IO.Path.GetFileName(game.Folder) + ".png' 그림을 넣으면 바뀌어요",
                Draw.Small, new Rectangle(pad, y, full.Width - pad * 2, S(20)), Theme.Dim);
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

        public override void OnPad(Pad b)
        {
            switch (b)
            {
                case Pad.Left: focus = 0; break;
                case Pad.Right: focus = 1; break;
                case Pad.A: if (focus == 0) Shell.Launch(game); else ToggleFav(); break;
                case Pad.Y: ToggleFav(); break;
                case Pad.B: Shell.Pop(); break;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            if (playBtn.Contains(p)) { focus = 0; Shell.Launch(game); }
            else if (favBtn.Contains(p)) { focus = 1; ToggleFav(); }
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, focus == 0 ? "플레이" : "즐겨찾기", () => OnPad(Pad.A)),
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
                new Row { Title = "게임 폴더 설정", Sub = "쯔꾸르 게임이 들어있는 폴더 추가·삭제", Value = Settings.GameFolders.Count + "개 폴더",
                          Act = Shell.OpenFolders },
                new Row { Title = "게임 다시 찾기", Sub = "폴더에 새 게임을 넣었을 때", Value = "게임 " + Shell.Games.Count + "개",
                          Act = Shell.Rescan },
            };
        }

        void CheckNow()
        {
            updateNote = "확인하는 중...";
            Updater.CheckAsync(Shell, msg => { updateNote = msg; Shell.ShowToast(msg, 4000); Shell.Invalidate(); }, () =>
            {
                updateNote = "새 버전을 받았어요. 다시 켜는 중...";
                Shell.Invalidate();
                if (Gamepad.Game == null || Gamepad.Game.HasExited) Updater.Restart();
            }, true);
            Shell.Invalidate();
        }

        public override void Paint(Graphics g)
        {
            int pad = S(48);
            int w = Math.Min(Area.Width - pad * 2, S(900));
            int x = Area.X + (Area.Width - w) / 2;
            Draw.Text(g, "설정", Draw.H1, new Rectangle(x, Area.Y + S(20), w, S(40)), Theme.Text);

            rows.Clear();
            int y = Area.Y + S(80);
            var list = Rows();
            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i];
                var r = new Rectangle(x, y, w, S(76));
                rows.Add(r);
                Draw.Fill(g, r, S(4), i == focus ? Theme.TileHot : Color.FromArgb(150, 23, 29, 37));
                Draw.Text(g, row.Title, Draw.Menu, new Rectangle(r.X + S(24), r.Y + S(12), r.Width - S(260), S(28)), Theme.Text);
                Draw.Text(g, row.Sub, Draw.Small, new Rectangle(r.X + S(24), r.Y + S(42), r.Width - S(260), S(22)), Theme.SubText);
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
                y += S(86);
            }
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
