using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 스팀OS 게임 모드처럼 생긴 전체 화면 껍데기.
    /// 화면(홈·라이브러리·상세)을 쌓아 두고 맨 위 것만 그림. 움직일 때만 타이머가 돌고, 가만히 있으면 쉰다.
    /// </summary>
    class ShellForm : Form, IPadTarget
    {
        public List<GameInfo> Games = new List<GameInfo>();
        public bool Scanning;
        public bool ShowFocus => true;

        readonly List<Screen> stack = new List<Screen>();
        readonly MainMenu menu = new MainMenu();
        readonly Timer anim = new Timer { Interval = 15 };
        readonly Timer clock = new Timer { Interval = 15000 };
        readonly Stopwatch sw = Stopwatch.StartNew();
        long lastTick;

        // 배경 (고른 게임 색이 은은하게)
        Bitmap bgCur, bgPrev;
        string bgId;
        float bgFade = 1f;

        string toast;
        long toastUntil;

        List<Hint> hints = new List<Hint>();
        Rectangle menuIcon;

        const int TopH = 44, BottomH = 52;

        Screen Cur => stack[stack.Count - 1];

        public ShellForm()
        {
            Text = "쯔꾸르 런처";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Theme.Bg;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            menu.Shell = this;
            anim.Tick += OnAnim;
            clock.Tick += (s, e) => Invalidate();
            clock.Start();

            CoverArt.Init(this);
            CoverArt.Changed = () => { UpdateBg(); Invalidate(); };

            var home = new HomeScreen { Shell = this };
            stack.Add(home);

            Shown += (s, e) =>
            {
                if (Settings.AutoUpdate) Updater.CheckAsync(this, msg => ShowToast(msg, 60000), () =>
                {
                    if (Gamepad.Game == null || Gamepad.Game.HasExited) Updater.Restart();
                    else ShowToast("게임을 끄면 새 버전으로 바뀌어요", 5000);
                });
                if (Settings.GameFolders.Count == 0) OpenFolders(true);
                else Rescan();
            };
        }

        // ───────── 화면 쌓기 ─────────
        void LayoutScreens()
        {
            Theme.Scale = Math.Max(0.6f, ClientSize.Height / 800f);
            Draw.EnsureFonts();
            var area = new Rectangle(0, Theme.S(TopH), ClientSize.Width, ClientSize.Height - Theme.S(TopH) - Theme.S(BottomH));
            foreach (var s in stack) s.Area = area;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Height <= 0) return;
            LayoutScreens();
            foreach (var s in stack) s.OnGamesChanged();
            Invalidate();
        }

        public void Push(Screen s)
        {
            s.Shell = this;
            stack.Add(s);
            LayoutScreens();
            s.OnGamesChanged();
            FocusChanged();
            Animate();
        }

        public void Pop()
        {
            if (stack.Count <= 1) return;
            stack.RemoveAt(stack.Count - 1);
            FocusChanged();
            Animate();
        }

        public void GoHome()
        {
            while (stack.Count > 1) stack.RemoveAt(stack.Count - 1);
            FocusChanged();
            Animate();
        }

        public void OpenMenu()
        {
            menu.Open = true;
            Animate();
        }

        public List<GameInfo> Recent(int max)
        {
            return Games.OrderByDescending(g => GameData.Get(g).LastPlayed)
                        .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
                        .Take(max).ToList();
        }

        // ───────── 배경 ─────────
        public void FocusChanged()
        {
            UpdateBg();
            Invalidate();
        }

        void UpdateBg()
        {
            var g = stack.Count > 0 ? Cur.FocusedGame : null;
            string id = g != null ? GameData.IdOf(g) : null;
            var art = g != null ? CoverArt.Get(g) : null;
            if (id == bgId && (bgCur != null || art == null)) return;
            if (g != null && art == null) return; // 그림 다 만들어지면 다시 불림
            bgPrev?.Dispose();
            bgPrev = bgCur;
            bgCur = art?.Blur != null ? new Bitmap(art.Blur) : null;
            bgId = id;
            bgFade = 0f;
            Animate();
        }

        void PaintBackground(Graphics g)
        {
            var r = ClientRectangle;
            using (var lg = new LinearGradientBrush(r, Theme.BgTop, Theme.BgBottom, 90f)) g.FillRectangle(lg, r);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (bgPrev != null && bgFade < 1f) DrawBg(g, bgPrev, (1f - bgFade) * 0.55f);
            if (bgCur != null) DrawBg(g, bgCur, bgFade * 0.55f);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            // 아래로 갈수록 어둡게
            using (var lg = new LinearGradientBrush(r, Color.FromArgb(40, 0, 0, 0), Color.FromArgb(220, 9, 12, 16), 90f)) g.FillRectangle(lg, r);
        }

        static void DrawBg(Graphics g, Bitmap b, float alpha)
        {
            if (alpha <= 0.01f) return;
            using (var ia = new System.Drawing.Imaging.ImageAttributes())
            {
                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
                ia.SetColorMatrix(cm);
                // 작은 그림을 크게 늘리면 자연스럽게 흐려짐 (가장자리 번짐 방지로 살짝 안쪽만 사용)
                var cr = g.ClipBounds;
                g.DrawImage(b, new Rectangle(0, 0, (int)cr.Right, (int)cr.Bottom), 1, 1, b.Width - 2, b.Height - 2, GraphicsUnit.Pixel, ia);
            }
        }

        // ───────── 애니메이션 ─────────
        public void Animate()
        {
            if (!anim.Enabled)
            {
                lastTick = sw.ElapsedMilliseconds;
                anim.Start();
            }
            Invalidate();
        }

        void OnAnim(object sender, EventArgs e)
        {
            long now = sw.ElapsedMilliseconds;
            float dt = Math.Min(0.05f, (now - lastTick) / 1000f);
            lastTick = now;
            bool more = Cur.Tick(dt);
            more |= menu.Tick(dt);
            if (bgFade < 1f) { bgFade = Math.Min(1f, bgFade + dt * 3f); more = true; }
            if (toast != null && now > toastUntil) toast = null;
            Invalidate();
            if (!more) anim.Stop();
        }

        public void ShowToast(string text, int ms = 2500)
        {
            toast = text;
            toastUntil = sw.ElapsedMilliseconds + ms;
            Invalidate();
            var t = new Timer { Interval = ms + 50 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); Invalidate(); if (toast != null && sw.ElapsedMilliseconds > toastUntil) toast = null; Invalidate(); };
            t.Start();
        }

        // ───────── 그리기 ─────────
        protected override void OnPaint(PaintEventArgs e)
        {
            if (stack.Count == 0) return;
            if (Cur.Area.Width == 0) LayoutScreens();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.EnsureFonts();

            PaintBackground(g);
            Cur.Paint(g);
            PaintTopBar(g);
            menu.Paint(g, ClientRectangle);
            PaintBottomBar(g);
            PaintToast(g);
        }

        void PaintTopBar(Graphics g)
        {
            int h = Theme.S(TopH);
            using (var lg = new LinearGradientBrush(new Rectangle(0, 0, Width, h + 1), Color.FromArgb(150, 9, 12, 16), Color.FromArgb(0, 9, 12, 16), 90f))
                g.FillRectangle(lg, 0, 0, Width, h);
            menuIcon = new Rectangle(Theme.S(8), 0, Theme.S(52), h);
            Draw.Hamburger(g, menuIcon, Theme.Text);

            int right = Width - Theme.S(20);
            string time = DateTime.Now.ToString("HH:mm");
            int tw = Draw.Measure(g, time, Draw.BodyBold);
            Draw.Text(g, time, Draw.BodyBold, new Rectangle(right - tw, 0, tw, h), Theme.Text);
            right -= tw + Theme.S(18);

            PowerStatus ps = null;
            try { ps = SystemInformation.PowerStatus; var _ = ps.BatteryChargeStatus; } catch { ps = null; }
            if (ps != null && ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown)
            {
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
                string pt = pct + "%";
                int pw = Draw.Measure(g, pt, Draw.Small);
                Draw.Text(g, pt, Draw.Small, new Rectangle(right - pw, 0, pw, h), Theme.SubText);
                right -= pw + Theme.S(6);
                Draw.Battery(g, right, h / 2, pct, charging);
            }
        }

        void PaintBottomBar(Graphics g)
        {
            int h = Theme.S(BottomH);
            var r = new Rectangle(0, Height - h, Width, h);
            using (var b = new SolidBrush(Color.FromArgb(menu.T > 0.5f ? 255 : 235, 14, 18, 24))) g.FillRectangle(b, r);
            using (var p = new Pen(Color.FromArgb(30, 255, 255, 255))) g.DrawLine(p, 0, r.Y, Width, r.Y);

            hints = menu.Open ? menu.Hints() : Cur.Hints();
            int x = Width - Theme.S(24);
            int cy = r.Y + h / 2;
            for (int i = hints.Count - 1; i >= 0; i--)
            {
                var hnt = hints[i];
                int lw = Draw.Measure(g, hnt.Label, Draw.Body);
                int gw = hnt.Button == Pad.A || hnt.Button == Pad.B || hnt.Button == Pad.X || hnt.Button == Pad.Y ? Theme.S(22) : Theme.S(30);
                int total = gw + Theme.S(8) + lw;
                int x0 = x - total;
                Draw.PadGlyph(g, hnt.Button, x0, cy);
                Draw.Text(g, hnt.Label, Draw.Body, new Rectangle(x0 + gw + Theme.S(8), r.Y, lw + 2, h), Theme.Text);
                hnt.Bounds = new Rectangle(x0 - Theme.S(10), r.Y, total + Theme.S(20), h);
                x = x0 - Theme.S(28);
            }
            if (Scanning) Draw.Text(g, "게임 찾는 중...", Draw.Small, new Rectangle(Theme.S(24), r.Y, Theme.S(300), h), Theme.SubText);
        }

        void PaintToast(Graphics g)
        {
            if (toast == null) return;
            int w = Draw.Measure(g, toast, Draw.Body) + Theme.S(40);
            var r = new Rectangle((Width - w) / 2, Theme.S(TopH) + Theme.S(8), w, Theme.S(40));
            Draw.Fill(g, r, Theme.S(20), Color.FromArgb(235, 35, 41, 51));
            Draw.Text(g, toast, Draw.Body, r, Theme.Text, Draw.Center);
        }

        // ───────── 입력: 패드 ─────────
        public void OnPad(Pad b)
        {
            if (menu.Open) { menu.OnPad(b); return; }
            if (b == Pad.Back || b == Pad.Start) { OpenMenu(); return; }
            Cur.OnPad(b);
            Animate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            Pad p = Pad.None;
            switch (key)
            {
                case Keys.Left: p = Pad.Left; break;
                case Keys.Right: p = Pad.Right; break;
                case Keys.Up: p = Pad.Up; break;
                case Keys.Down: p = Pad.Down; break;
                case Keys.Enter: case Keys.Space: p = Pad.A; break;
                case Keys.Escape: case Keys.Back: p = Pad.B; break;
                case Keys.Tab: p = Pad.Back; break;
                case Keys.PageUp: p = Pad.LB; break;
                case Keys.PageDown: p = Pad.RB; break;
                case Keys.F5: p = Pad.Y; break;
            }
            if (p == Pad.None) return base.ProcessCmdKey(ref msg, key);
            OnPad(p);
            return true;
        }

        // ───────── 입력: 터치·마우스 ─────────
        Point down;
        bool isDown, dragging;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            down = e.Location; isDown = true; dragging = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!isDown || menu.Open) return;
            int dx = e.X - down.X, dy = e.Y - down.Y;
            if (!dragging && (Math.Abs(dx) > Theme.S(12) || Math.Abs(dy) > Theme.S(12)) && Cur.Area.Contains(down))
            {
                dragging = true;
                Cur.OnDrag(dx, dy, true);
            }
            if (dragging) { Cur.OnDrag(dx, dy, false); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!isDown) return;
            isDown = false;
            if (dragging) { dragging = false; Cur.OnDrag(int.MinValue, int.MinValue, false); Animate(); return; }
            var p = e.Location;
            foreach (var h in hints) if (h.Bounds.Contains(p)) { h.Act?.Invoke(); Animate(); return; }
            if (menu.Open) { menu.OnTap(p); Invalidate(); return; }
            if (menuIcon.Contains(p)) { OpenMenu(); return; }
            Cur.OnTap(p);
            Animate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Cur.OnPad(e.Delta > 0 ? Pad.Up : Pad.Down);
            Animate();
        }

        // ───────── 게임 찾기·실행·폴더 ─────────
        public void OpenFolders() => OpenFolders(false);

        void OpenFolders(bool firstRun)
        {
            using (var f = new FolderManagerForm(firstRun))
            {
                f.ShowDialog(this);
                if (f.Changed || firstRun) Rescan();
            }
        }

        public void Rescan()
        {
            if (Scanning) return;
            Scanning = true;
            Invalidate();
            var roots = Settings.GameFolders.ToList();
            Task.Run(() =>
            {
                List<GameInfo> found;
                try { found = GameScanner.ScanAll(roots); } catch { found = new List<GameInfo>(); }
                BeginInvoke(new Action(() =>
                {
                    Scanning = false;
                    SetGames(found);
                    ShowToast("게임 " + found.Count + "개를 찾았어요");
                }));
            });
        }

        public void SetGames(List<GameInfo> found)
        {
            Games = found;
            LayoutScreens();
            foreach (var s in stack) s.OnGamesChanged();
            FocusChanged();
        }

        public void Launch(GameInfo g)
        {
            try
            {
                var psi = new ProcessStartInfo(g.Exe) { WorkingDirectory = g.Folder, UseShellExecute = true };
                var p = Process.Start(psi);
                var st = GameData.Get(g);
                st.LastPlayed = DateTime.Now.Ticks;
                GameData.Save();
                var started = DateTime.Now;
                WindowState = FormWindowState.Minimized; // 게임하는 동안 런처는 쉬기
                if (p == null) return;

                // MV·MZ는 게임 자체가 패드를 잘 읽으므로 키 변환은 2000~VX Ace만
                Gamepad.Game = p;
                Gamepad.MapKeys = g.Version != TkoolVersion.MV && g.Version != TkoolVersion.MZ;
                p.EnableRaisingEvents = true;
                p.Exited += (s, e) => BeginInvoke(new Action(() =>
                {
                    if (Gamepad.Game == p) Gamepad.Game = null;
                    st.TotalSeconds += (long)(DateTime.Now - started).TotalSeconds;
                    st.LastPlayed = DateTime.Now.Ticks;
                    GameData.Save();
                    if (Updater.RestartPending) { Updater.Restart(); return; }
                    foreach (var sc in stack) if (!(sc is DetailScreen)) sc.OnGamesChanged();
                    WindowState = FormWindowState.Maximized;
                    Activate();
                    Invalidate();
                }));
            }
            catch (Exception ex)
            {
                WindowState = FormWindowState.Maximized;
                ShowToast("실행하지 못했어요: " + ex.Message, 5000);
            }
        }
    }
}
