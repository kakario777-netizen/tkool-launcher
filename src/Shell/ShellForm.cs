using System;
using System.IO;
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
        readonly QuickPanel quick = new QuickPanel();
        public bool CameFromGame;   // 게임 중에 홈 버튼으로 불려 나왔는지
        int scaleWas;               // 런처를 불러낼 때 잠시 꺼둔 화면 확대
        IntPtr gameHwnd;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
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
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Theme.Bg;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            menu.Shell = this;
            quick.Shell = this;
            Gamepad.Home = HomeAction;
            Hotkeys.Home = () => BeginInvoke(new Action(HomeAction));
            Hotkeys.Small = () => BeginInvoke(new Action(SmallAction));
            Hotkeys.Button = which => BeginInvoke(new Action(() => ButtonActions.Run(this, which == 1 ? Settings.LcAction : Settings.RcAction)));
            LoopGuard.Alert = msg => { try { BeginInvoke(new Action(() => ShowToast(msg, 8000))); } catch { } };
            try { LoopGuard.Start(); } catch { }
            Theme.ApplyAccent();
            LoadBackground();
            try { Hotkeys.Start(); } catch { }
            anim.Tick += OnAnim;
            clock.Tick += (s, e) => Invalidate();
            clock.Start();

            CoverArt.Init(this);
            CoverArt.Changed = () => { UpdateBg(); Invalidate(); };
            GameDesc.Changed = () => { try { BeginInvoke(new Action(Invalidate)); } catch { } };

            var home = new HomeScreen { Shell = this };
            stack.Add(home);

            Shown += (s, e) =>
            {
                if (Log.PreviousCrash != null)
                    ErrorPopup.Show2("지난번에 런처가 갑자기 꺼졌어요",
                        Log.PreviousCrash + "\n\n기록: 내 문서\\쯔꾸르 런처\\logs (log.txt, errors.txt)\n이 기록을 Claude에게 보여주면 원인을 찾아 고칠 수 있어요.");
                // 팬·조명·TDP 드라이버 준비 (느릴 수 있어 뒤에서)
                Task.Run(() =>
                {
                    try { Hardware.Init(); Hardware.ApplySaved(); } catch (Exception ex) { Log.Error("하드웨어 준비", ex); }
                    Log.Info("관리자=" + Hardware.IsAdmin + " PawnIO=" + Hardware.PawnInstalled + " 기기일치=" + Hardware.IsAirPlusMendocino +
                             " EC=" + Hardware.EcReady + " SMU=" + Hardware.SmuReady + " 온도=" + Hardware.TempReady + (Hardware.Problem != "" ? " 문제=" + Hardware.Problem : ""));
                    // 가상 패드: 켜 뒀으면 다시 켜고, 아니면 혹시 숨겨진 채 남은 진짜 패드를 풀어줌 (런처가 비정상 종료됐을 때 대비)
                    try { if (Settings.VPadOn) { bool ok = VirtualPad.Start(); Log.Info("가상 패드 " + (ok ? "켜짐" : "실패: " + VirtualPad.Problem)); } else VirtualPad.Stop(); }
                    catch (Exception ex) { Log.Error("가상 패드", ex); }
                    try { BeginInvoke(new Action(Invalidate)); } catch { }
                });
                var hw = new Timer { Interval = 15000 };
                hw.Tick += (s2, e2) =>
                {
                    bool smart = Settings.SmartFps > 0 && GameWatch.Running;
                    if (Hardware.SmuReady && Settings.Tdp > 0 && !smart) Task.Run(() => Hardware.SetTdp(Settings.Tdp));
                };
                hw.Start();

                // 1초마다: FPS 측정 켜고 끄기, 스마트 TDP, 배경음악
                int sec = 0;
                var fpsTimer = new Timer { Interval = 1000 };
                fpsTimer.Tick += (s3, e3) =>
                {
                    bool need = GameWatch.Running && (OverlayForm.IsOn || Settings.SmartFps > 0);
                    if (need) { Fps.Start(); Fps.Tick(); } else if (Fps.Running) Fps.Stop();
                    if (++sec % 3 == 0 && need) Task.Run(() => SmartTdp.Tick());
                    Bgm.Update(!GameWatch.Running && WindowState != FormWindowState.Minimized && Visible && mini == null);
                };
                fpsTimer.Start();
                if (Settings.AutoUpdate) Updater.CheckAsync(this, msg => ShowToast(msg, 60000), () =>
                {
                    if (!GameWatch.Running) Updater.Restart();
                    else ShowToast("게임을 끄면 새 버전으로 바뀌어요", 5000);
                });
                if (Settings.GameFolders.Count == 0 && !Settings.StoreScan) OpenFolders(true);
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

        // ───────── 게임 위 오른쪽 팝업 (AYASpace처럼) ─────────
        QuickPopup popup;
        public bool PopupClosing;
        public bool PopupOpen => popup != null && popup.Visible;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

        /// <summary>다른 창이 앞에 있어도 확실히 앞으로 (Alt를 살짝 눌러 윈도우의 포커스 제한을 풂)</summary>
        static void ForceForeground(IntPtr h)
        {
            keybd_event(0x12, 0, 0, IntPtr.Zero);
            keybd_event(0x12, 0, 2, IntPtr.Zero);
            SetForegroundWindow(h);
        }

        public void ShowPopup()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != Handle) gameHwnd = fg;
            CameFromGame = GameWatch.Running;
            scaleWas = Scaler.Mode;
            if (scaleWas != 0) Scaler.Stop();   // 확대 중이면 팝업까지 커지니 잠시 풂
            if (popup == null || popup.IsDisposed) popup = new QuickPopup(this);
            popup.Open();
            ForceForeground(popup.Handle);
        }

        /// <summary>팝업 닫기. backToGame이면 원래 앞에 있던 게임으로 포커스를 돌려줌</summary>
        public void ClosePopup(bool backToGame = true)
        {
            if (popup == null || !popup.Visible) return;
            PopupClosing = true;
            popup.Hide();
            PopupClosing = false;
            if (backToGame && gameHwnd != IntPtr.Zero && gameHwnd != Handle)
            {
                ShowWindow(gameHwnd, 9);
                ForceForeground(gameHwnd);
            }
            if (scaleWas != 0 && gameHwnd != IntPtr.Zero && GameWatch.Running) { int m = scaleWas; scaleWas = 0; Scaler.Start(m, gameHwnd); }
            scaleWas = 0;
            CameFromGame = false;
        }

        // ───────── 홈 버튼·빠른 설정 ─────────
        /// <summary>AYA 버튼·가이드 버튼·보기+메뉴: 게임 중이면 런처를 불러와 빠른 설정을, 런처 안이면 빠른 설정 열고 닫기</summary>
        public void HomeAction()
        {
            if (GameCapture.Finding) { GameCapture.Cancel = true; return; }
            if (PopupOpen) { ClosePopup(); return; }
            // 런처가 앞에 없으면(게임·바탕화면 등) 런처를 띄우지 않고 오른쪽 팝업만
            if (Form.ActiveForm == null || WindowState == FormWindowState.Minimized) { ShowPopup(); return; }
            if (GameWatch.Running && (WindowState == FormWindowState.Minimized || Form.ActiveForm == null))
            {
                gameHwnd = GetForegroundWindow();
                CameFromGame = true;
                scaleWas = Scaler.Mode;
                if (scaleWas != 0) Scaler.Stop();
                WindowState = FormWindowState.Maximized;
                Show();
                Activate();
                SetForegroundWindow(Handle);
                menu.Open = false;
                quick.Show();
                return;
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Maximized;
                Activate();
                menu.Open = false;
                quick.Show();
                return;
            }
            if (quick.Open) quick.Hide(); else { menu.Open = false; quick.Show(); }
        }

        /// <summary>AYA 작은 버튼: 게임 중이면 런처 홈 화면을, 런처 안이면 홈으로</summary>
        public void SmallAction()
        {
            if (WindowState == FormWindowState.Minimized || Form.ActiveForm == null)
            {
                if (GameWatch.Running) { gameHwnd = GetForegroundWindow(); CameFromGame = true; }
                WindowState = FormWindowState.Maximized;
                Show(); Activate(); SetForegroundWindow(Handle);
            }
            quick.Open = false; menu.Open = false;
            GoHome();
        }

        /// <summary>빠른 버튼: 게임에서 불려 나왔으면 게임으로 돌아간 뒤 실행</summary>
        public void SendToGame(Action a)
        {
            if (PopupOpen)
            {
                ClosePopup();
                var tp = new Timer { Interval = 350 };
                tp.Tick += (s, e) => { tp.Stop(); tp.Dispose(); try { a(); } catch { } };
                tp.Start();
                return;
            }
            if (CameFromGame && GameWatch.Running)
            {
                ReturnToGame();
                var t = new Timer { Interval = 350 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); try { a(); } catch { } };
                t.Start();
            }
            else
            {
                quick.Hide();
                try { a(); } catch { }
            }
        }

        // ───────── 미니 모드 ─────────
        MiniBubble mini;

        public void EnterMini()
        {
            if (PopupOpen) ClosePopup();
            quick.Open = false; quick.T = 0; menu.Open = false;
            if (mini == null) { mini = new MiniBubble(this); mini.Show(); }
            if (CameFromGame && GameWatch.Running) ReturnToGame();
            else WindowState = FormWindowState.Minimized;
        }

        public void LeaveMini()
        {
            if (mini != null) { mini.Close(); mini = null; }
            if (GameWatch.Running) { gameHwnd = GetForegroundWindow(); CameFromGame = true; }
            WindowState = FormWindowState.Maximized;
            Show(); Activate(); SetForegroundWindow(Handle);
        }

        // ───────── 배경 사진 (내 문서\쯔꾸르 런처\background.jpg/png) ─────────
        Bitmap bgImage;

        public void LoadBackground()
        {
            bgImage?.Dispose();
            bgImage = null;
            if (!Settings.UseBgImage) return;
            foreach (var n in new[] { "background.jpg", "background.png", "background.jpeg" })
            {
                string p = System.IO.Path.Combine(Settings.Dir, n);
                if (!System.IO.File.Exists(p)) continue;
                try
                {
                    using (var fs = System.IO.File.OpenRead(p))
                    using (var img = Image.FromStream(fs))
                    {
                        // 화면 크기로 한 번만 줄여 둠 (메모리 아끼기)
                        var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                        int w = Math.Min(sc.Width, 1920), h = w * sc.Height / Math.Max(1, sc.Width);
                        bgImage = new Bitmap(w, h);
                        using (var g = Graphics.FromImage(bgImage))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            CoverArt.DrawCover(g, img, new Rectangle(0, 0, w, h));
                        }
                    }
                }
                catch { bgImage = null; }
                break;
            }
            Invalidate();
        }

        public void OpenQuick()
        {
            menu.Open = false;
            quick.Show();
        }

        public void ReturnToGame()
        {
            if (PopupOpen) { ClosePopup(); return; }
            quick.Open = false;
            quick.T = 0;
            CameFromGame = false;
            WindowState = FormWindowState.Minimized;
            if (gameHwnd != IntPtr.Zero) { ShowWindow(gameHwnd, 9); SetForegroundWindow(gameHwnd); }
            if (scaleWas != 0 && gameHwnd != IntPtr.Zero) { int m = scaleWas; scaleWas = 0; Scaler.Start(m, gameHwnd); }
        }

        public void QuitGame()
        {
            if (PopupOpen) ClosePopup(false);
            string dir = GameWatch.Dir;
            if (dir == null) return;
            foreach (var p in Process.GetProcesses())
            {
                try { if (p.Id != Process.GetCurrentProcess().Id && GameWatch.InDir(p.Id, dir)) p.Kill(); }
                catch { }
                finally { p.Dispose(); }
            }
            quick.Hide();
            CameFromGame = false;
            ShowToast("게임을 끝냈어요", 2500);
        }

        public void AfterDisplayChange()
        {
            // 해상도·회전이 바뀌면 창을 다시 꽉 채움
            BeginInvoke(new Action(() =>
            {
                WindowState = FormWindowState.Normal;
                WindowState = FormWindowState.Maximized;
                LayoutScreens();
                foreach (var sc in stack) sc.OnGamesChanged();
                Invalidate();
            }));
        }

        public void OpenMenu()
        {
            menu.Open = true;
            Animate();
        }

        public IEnumerable<GameInfo> VisibleGames => Games.Where(g => !GameData.Get(g).Hidden);

        public List<GameInfo> Recent(int max)
        {
            return VisibleGames.OrderByDescending(g => GameData.Get(g).LastPlayed)
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

        // 배경은 한 장으로 미리 합쳐 두고 그대로 찍기 (매 프레임 전체 화면 그림 늘리기가 버벅임의 큰 원인)
        Bitmap bgCache; object bgKeyA, bgKeyB, bgKeyC; int bgKeyFade = -1; Size bgKeySize;

        void PaintBackground(Graphics g)
        {
            var r = ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            int fq = bgFade >= 1f ? 100 : (int)(bgFade * 12);
            if (bgCache == null || bgKeySize != r.Size || bgKeyA != bgImage || bgKeyB != bgPrev || bgKeyC != bgCur || bgKeyFade != fq)
            {
                var nb = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var cg = Graphics.FromImage(nb)) ComposeBackground(cg, r);
                bgCache?.Dispose();
                bgCache = nb; bgKeySize = r.Size; bgKeyA = bgImage; bgKeyB = bgPrev; bgKeyC = bgCur; bgKeyFade = fq;
            }
            var oc = g.CompositingMode; var oi = g.InterpolationMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImageUnscaled(bgCache, 0, 0);
            g.CompositingMode = oc; g.InterpolationMode = oi;
        }

        void ComposeBackground(Graphics g, Rectangle r)
        {
            using (var lg = new LinearGradientBrush(r, Theme.BgTop, Theme.BgBottom, 90f)) g.FillRectangle(lg, r);
            if (bgImage != null)
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(bgImage, r);
                using (var dim = new SolidBrush(Color.FromArgb(120, 9, 12, 16))) g.FillRectangle(dim, r);
            }
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float f = bgFade >= 1f ? 1f : bgKeyFadeValue();
            if (bgPrev != null && f < 1f) DrawBg(g, bgPrev, (1f - f) * 0.55f);
            if (bgCur != null) DrawBg(g, bgCur, f * 0.55f);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            // 아래로 갈수록 어둡게
            using (var lg = new LinearGradientBrush(r, Color.FromArgb(40, 0, 0, 0), Color.FromArgb(220, 9, 12, 16), 90f)) g.FillRectangle(lg, r);
        }

        float bgKeyFadeValue() => Math.Min(1f, (int)(bgFade * 12) / 12f);

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
            more |= quick.Tick(dt);
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
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 왜 꺼졌는지 기록 (사용자·윈도우·코드 중 누가 닫았는지)
            try
            {
                var st = new System.Diagnostics.StackTrace().GetFrames();
                var who = new System.Collections.Generic.List<string>();
                if (st != null)
                    foreach (var fr in st)
                    {
                        var m = fr.GetMethod();
                        if (m != null && m.DeclaringType != null && m.DeclaringType.Namespace != null && m.DeclaringType.Namespace.StartsWith("TkoolLauncher"))
                            who.Add(m.DeclaringType.Name + "." + m.Name);
                        if (who.Count >= 6) break;
                    }
                Log.Info("창 닫힘: " + e.CloseReason + (who.Count > 1 ? " ← " + string.Join(" ← ", who.GetRange(1, who.Count - 1)) : ""));
            }
            catch { }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Hotkeys.Stop();
            try { Scaler.Stop(); } catch { }
            try { VirtualPad.Stop(); } catch { }
            LoopGuard.Stop();
            Fps.Stop();
            Bgm.Close();
            Hardware.Shutdown();
            OverlayForm.Toggle(false);
            base.OnFormClosed(e);
        }

        int paintErrAt;

        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintAll(e); }
            catch (Exception ex)
            {
                // 그리기 오류가 계속 나도 런처가 꺼지지 않게: 1분에 한 번만 기록
                if (Environment.TickCount - paintErrAt > 60000) { paintErrAt = Environment.TickCount; Log.Error("화면 그리기", ex); ErrorPopup.ShowError(ex); }
            }
        }

        void PaintAll(PaintEventArgs e)
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
            quick.Paint(g, ClientRectangle);
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

            hints = quick.Open ? quick.Hints() : menu.Open ? menu.Hints() : Cur.Hints();
            if (!quick.Open && !menu.Open) hints.Insert(0, new Hint(Pad.Start, "빠른 설정", OpenQuick));
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
            if (quick.Open) { quick.OnPad(b); return; }
            if (menu.Open) { menu.OnPad(b); return; }
            if (b == Pad.Back || b == Pad.Guide) { OpenMenu(); return; }
            if (b == Pad.Start) { OpenQuick(); return; }
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
            if (quick.Open) { quick.OnDrag(e.Location); dragging = Math.Abs(dx) > Theme.S(12); return; }
            // 위쪽 끝에서 아래로 쓸어내리면 빠른 설정
            if (down.Y < Theme.S(TopH) && dy > Theme.S(60)) { isDown = false; OpenQuick(); return; }
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
            if (dragging && quick.Open) { dragging = false; return; }
            if (dragging) { dragging = false; Cur.OnDrag(int.MinValue, int.MinValue, false); Animate(); return; }
            var p = e.Location;
            foreach (var h in hints) if (h.Bounds.Contains(p)) { h.Act?.Invoke(); Animate(); return; }
            if (quick.Open) { quick.OnTap(p); Invalidate(); return; }
            if (menu.Open) { menu.OnTap(p); Invalidate(); return; }
            if (menuIcon.Contains(p)) { OpenMenu(); return; }
            if (p.Y < Theme.S(TopH) && p.X > Width - Theme.S(220)) { OpenQuick(); return; }
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

        public void AddManualGame()
        {
            using (var f = new FolderPickerForm(null, true))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (!Settings.ManualGames.Contains(f.SelectedPath, StringComparer.OrdinalIgnoreCase))
                {
                    Settings.ManualGames.Add(f.SelectedPath);
                    Settings.Save();
                }
                Rescan();
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

        public void RefreshLists()
        {
            foreach (var s in stack) s.OnGamesChanged();
            FocusChanged();
        }

        public GameInfo PlayingGame;

        /// <summary>표지·이름 없는 게임을 하나씩 잠깐 켜서 채우기</summary>
        public void FindCovers()
        {
            if (GameWatch.Running) { ShowToast("게임을 끈 다음에 해 주세요", 3000); return; }
            var list = GameCapture.Missing(Games);
            if (list.Count == 0) { ShowToast("표지·이름이 빠진 게임이 없어요", 3000); return; }
            Gamepad.MapKeys = false;
            WindowState = FormWindowState.Minimized;
            var tip = new OverlayTip();
            tip.Show();
            GameCapture.FindAll(list,
                msg => { try { BeginInvoke(new Action(() => tip.SetText("표지 찾는 중 " + msg))); } catch { } },
                n => BeginInvoke(new Action(() =>
                {
                    tip.Close();
                    WindowState = FormWindowState.Maximized;
                    Activate();
                    RefreshLists();
                    ShowToast(list.Count + "개 중 " + n + "개의 표지·이름을 채웠어요", 5000);
                })));
        }

        public void SetGames(List<GameInfo> found)
        {
            // 게임 창 제목에서 가져와 저장해 둔 진짜 이름 적용
            foreach (var g in found)
            {
                string t = GameData.Get(g).Title;
                if (!string.IsNullOrEmpty(t)) g.Title = t;
            }
            Games = found;
            LayoutScreens();
            foreach (var s in stack) s.OnGamesChanged();
            FocusChanged();
        }

        public void Launch(GameInfo g)
        {
            if (GameWatch.Running) { ShowToast("다른 게임이 아직 실행 중이에요", 3000); return; }
            Log.Info("게임 실행: " + g.Title + " (" + g.Kind + " " + g.VersionLabel + ") " + (g.LaunchUri ?? (g.Launcher != null ? g.Launcher + " " + g.Args : g.Exe)));
            Process p = null;
            try
            {
                if (g.LaunchUri != null)
                    Process.Start(new ProcessStartInfo(g.LaunchUri) { UseShellExecute = true });
                else if (g.Launcher != null)
                    Process.Start(new ProcessStartInfo(g.Launcher, g.Args ?? "") { WorkingDirectory = Path.GetDirectoryName(g.Launcher), UseShellExecute = true });
                else
                    p = Process.Start(new ProcessStartInfo(g.Exe) { WorkingDirectory = g.Folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("게임 실행 " + g.Title, ex);
                ShowToast("실행하지 못했어요: " + ex.Message, 5000);
                return;
            }

            var st = GameData.Get(g);
            st.LastPlayed = DateTime.Now.Ticks;
            GameData.Save();
            var started = DateTime.Now;
            WindowState = FormWindowState.Minimized; // 게임하는 동안 런처는 쉬기

            // 키보드 변환은 패드를 못 읽는 쯔꾸르(2000~VX Ace)만
            Gamepad.MapKeys = g.IsTkool && g.Version != TkoolVersion.MV && g.Version != TkoolVersion.MZ;
            string dir = g.InstallDir ?? g.Folder;
            CameFromGame = false;
            SmartTdp.Reset();
            PlayingGame = g;
            if (Settings.AutoCover) GameCapture.Watch(this, g);
            if (Settings.AutoScaleTkool && Gamepad.MapKeys)
            {
                // 게임 창이 뜰 때까지 조금 기다렸다가 확대 (앞 창이 게임 폴더 프로그램일 때만)
                int tries = 0;
                var st2 = new Timer { Interval = 1500 };
                st2.Tick += (s2, e2) =>
                {
                    IntPtr fg = GetForegroundWindow();
                    GetWindowThreadProcessId(fg, out uint fpid);
                    if (GameWatch.InDir((int)fpid, GameWatch.Dir)) { st2.Stop(); st2.Dispose(); Scaler.Start(2, fg); }
                    else if (++tries > 10) { st2.Stop(); st2.Dispose(); }
                };
                st2.Start();
            }
            GameWatch.Start(p, dir, () => BeginInvoke(new Action(() =>
            {
                CameFromGame = false;
                quick.Open = false; quick.T = 0;
                st.TotalSeconds += (long)(DateTime.Now - started).TotalSeconds;
                st.LastPlayed = DateTime.Now.Ticks;
                GameData.Save();
                Gamepad.MapKeys = false;
                if (Updater.RestartPending) { Updater.Restart(); return; }
                foreach (var sc in stack) if (!(sc is DetailScreen)) sc.OnGamesChanged();
                WindowState = FormWindowState.Maximized;
                Activate();
                Invalidate();
            })));
        }
    }
}
