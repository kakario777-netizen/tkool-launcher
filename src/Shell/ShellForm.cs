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

        public Screen Cur => stack[stack.Count - 1];

        public ShellForm()
        {
            Text = "쯔꾸르 런처";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            if (StartInTray) Opacity = 0;
            BackColor = Theme.Bg;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            menu.Shell = this;
            quick.Shell = this;
            Gamepad.Home = HomeAction;
            Hotkeys.Home = () => BeginInvoke(new Action(AyaAction));
            Hotkeys.HomeLong = () => BeginInvoke(new Action(AyaLongAction));
            Hotkeys.Small = () => BeginInvoke(new Action(SmallAction));
            Hotkeys.Button = which => BeginInvoke(new Action(() => ButtonActions.Run(this, which == 1 ? Settings.LcAction : Settings.RcAction)));
            LoopGuard.Alert = msg => { try { BeginInvoke(new Action(() => ShowToast(msg, 8000))); } catch { } };
            try { LoopGuard.Start(); } catch { }
            Theme.ApplyAccent();
            LoadBackground();
            try { Hotkeys.Start(); } catch { }
            SetupTray();
            ResForce.Start(() => GameDoctor.Library);
            StartHealth();
            if (StartInTray) trayTipShown = true;
            try { if (SystemControls.GetAutoStart()) SystemControls.SetAutoStart(true); } catch { }   // 예전 등록도 --tray로 고침
            if (Settings.OvOn) { try { OverlayForm.Toggle(true); } catch { } }
            anim.Tick += OnAnim;
            clock.Tick += (s, e) => Invalidate();
            clock.Start();

            CoverArt.Init(this);
            CoverArt.Changed = () => { UpdateBg(); Invalidate(); };
            GameDesc.Changed = () => { try { BeginInvoke(new Action(Invalidate)); } catch { } };
            Translate.Changed = () => { try { BeginInvoke(new Action(Invalidate)); } catch { } };

            // 첫 화면 = 라이브러리 (설정의 "라이브러리 첫 탭"), 최근 게임 선반 홈은 안 씀
            stack.Add(new LibraryScreen { Shell = this });

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
                Activity.Changed = () => { try { BeginInvoke(new Action(() => Invalidate(new Rectangle(0, Height - Theme.S(BottomH), Width, Theme.S(BottomH))))); } catch { } };
                var actTimer = new Timer { Interval = 300 };
                actTimer.Tick += (o, ev) => { if (Activity.Any && Visible && WindowState != FormWindowState.Minimized) Invalidate(new Rectangle(0, Height - Theme.S(BottomH), Width, Theme.S(BottomH))); };
                actTimer.Start();
                var fpsTimer = new Timer { Interval = 1000 };
                fpsTimer.Tick += (s3, e3) =>
                {
                    // 런처 밖에서 켠 게임(스팀 등)도 재도록: 런처 화면이 앞에 있을 때만 쉼
                    bool launcherFront = Visible && WindowState != FormWindowState.Minimized && Form.ActiveForm == this;
                    bool need = (OverlayForm.IsOn || Settings.SmartFps > 0) && (GameWatch.Running || !launcherFront);
                    if (need) { Fps.Start(); Fps.Tick(); } else if (Fps.Running) Fps.Stop();
                    if (++sec % 3 == 0 && need && Settings.SmartFps > 0) Task.Run(() => SmartTdp.Tick());
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
                WatchFolders();
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
            if (cheatOpen) { CheatLink.Send("hide"); EndCheatInput(); }   // 치트 팝업이 떠 있으면 내리고 빠른 설정을 띄움
            IntPtr fg = GetForegroundWindow();
            if (fg != Handle) gameHwnd = fg;
            CameFromGame = GameWatch.Running;
            scaleWas = Scaler.Mode;
            if (scaleWas != 0) Scaler.Stop();   // 확대 중이면 팝업까지 커지니 잠시 풂
            if (popup == null || popup.IsDisposed) popup = new QuickPopup(this);
            // 게임은 계속 앞 창(포커스 그대로) — 팝업은 위에 떠서 패드만 받음, 그동안 게임엔 버튼이 안 감
            popupMapWas = Gamepad.MapKeys; Gamepad.MapKeys = false;
            popupPadOffWas = VirtualPad.ControllerOff;
            if (VirtualPad.Running) VirtualPad.ControllerOff = true;
            Gamepad.Overlay = popup;
            popup.Open();
        }

        /// <summary>팝업 닫기. backToGame이면 원래 앞에 있던 게임으로 포커스를 돌려줌</summary>
        bool popupMapWas, popupPadOffWas;

        /// <summary>전체 화면 ↔ 창 (Alt+Enter) 뒤 창이 되면 화면 가운데로</summary>
        public void ToggleFullscreen()
        {
            IntPtr gw = gameHwnd != IntPtr.Zero ? gameHwnd : GetForegroundWindow();
            SendToGame(() => KeySender.Combo(KeySender.Alt, KeySender.Enter));
            int tries = 0;
            var t = new Timer { Interval = 500 };
            t.Tick += (s, e) =>
            {
                if (++tries >= 5) { t.Stop(); t.Dispose(); }
                if (tries == 2 || tries == 5) CenterGameWin(gw, true);   // 창 크기가 자리 잡을 때까지 두 번
            };
            t.Start();
        }

        public void ClosePopup(bool backToGame = true)
        {
            if (popup == null || !popup.Visible) return;
            PopupClosing = true;
            popup.Hide();
            PopupClosing = false;
            Gamepad.Overlay = null;
            Gamepad.MapKeys = popupMapWas && GameWatch.Running;
            VirtualPad.ControllerOff = popupPadOffWas;
            // 팝업이 포커스를 안 가져가니 보통은 게임이 그대로 앞 — 혹시 다른 창이 앞이면 게임으로
            if (backToGame && gameHwnd != IntPtr.Zero && gameHwnd != Handle && GetForegroundWindow() != gameHwnd)
            {
                ShowWindow(gameHwnd, 9);
                ForceForeground(gameHwnd);
            }
            if (scaleWas != 0 && gameHwnd != IntPtr.Zero && GameWatch.Running) { int m = scaleWas; scaleWas = 0; Scaler.Start(m, gameHwnd); }
            scaleWas = 0;
            CameFromGame = false;
        }

        // ───────── AYA 큰 버튼 ─────────
        bool LauncherFront => Visible && WindowState != FormWindowState.Minimized && Form.ActiveForm == this;

        /// <summary>짧게: 라이브러리가 앞에 있으면 라이브러리를 내림, 아니면(게임·바탕화면) 오른쪽 팝업</summary>
        public void AyaAction()
        {
            if (coverFor != null) { EndManualCover(); ShowToast("표지 찍기를 그만뒀어요", 2000); }
            if (GameCapture.Finding) { GameCapture.Cancel = true; return; }
            if (PopupOpen) { ClosePopup(); return; }
            if (LauncherFront)
            {
                if (quick.Open) { quick.Hide(); return; }
                menu.Open = false;
                if (CameFromGame && GameWatch.Running) { ReturnToGame(); return; }
                ToTray();
                return;
            }
            ShowPopup();
        }

        /// <summary>길게: 라이브러리 전체 화면</summary>
        public void AyaLongAction()
        {
            if (PopupOpen) ClosePopup(false);
            if (LauncherFront) { quick.Open = false; menu.Open = false; OpenLibrary(); return; }
            if (GameWatch.Running || TrackedExternal) { gameHwnd = GetForegroundWindow(); CameFromGame = true; }
            scaleWas = Scaler.Mode;
            if (scaleWas != 0) Scaler.Stop();
            Bring();
            ForceForeground(Handle);
            quick.Open = false; menu.Open = false;
            OpenLibrary();
        }

        /// <summary>라이브러리(탭 있는 게임 목록) 화면으로</summary>
        public void OpenLibrary()
        {
            GoHome();
        }

        // ───────── 홈 버튼·빠른 설정 ─────────
        /// <summary>AYA 버튼·가이드 버튼·보기+메뉴: 게임 중이면 런처를 불러와 빠른 설정을, 런처 안이면 빠른 설정 열고 닫기</summary>
        public void HomeAction()
        {
            if (GameCapture.Finding) { GameCapture.Cancel = true; return; }
            if (PopupOpen) { ClosePopup(); return; }
            // 런처가 앞에 없으면(게임·바탕화면 등) 런처를 띄우지 않고 오른쪽 팝업만
            if (Form.ActiveForm == null || WindowState == FormWindowState.Minimized) { ShowPopup(); return; }
            if (GameWatch.Running && curtain) { ShowPopup(); return; }   // 게임 중(검은 판 뒤)엔 언제나 게임 위 팝업
            if (GameWatch.Running && (WindowState == FormWindowState.Minimized || Form.ActiveForm == null))
            {
                gameHwnd = GetForegroundWindow();
                CameFromGame = true;
                scaleWas = Scaler.Mode;
                if (scaleWas != 0) Scaler.Stop();
                Bring();
                Show();
                Activate();
                SetForegroundWindow(Handle);
                menu.Open = false;
                quick.Show();
                return;
            }
            if (WindowState == FormWindowState.Minimized)
            {
                Bring();
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
            // 런처 화면이 앞에 떠 있으면 내림(알림 영역으로), 아니면 올림 — 라이브러리만 올렸다 내렸다
            if (Visible && WindowState != FormWindowState.Minimized && Form.ActiveForm == this && !PopupOpen)
            {
                quick.Open = false; menu.Open = false;
                ToTray();
                return;
            }
            if (!Visible || WindowState == FormWindowState.Minimized || Form.ActiveForm == null)
            {
                if (GameWatch.Running) { gameHwnd = GetForegroundWindow(); CameFromGame = true; }
                Bring();
                Show(); Activate(); SetForegroundWindow(Handle);
            }
            quick.Open = false; menu.Open = false;
            OpenLibrary();
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
            Bring();
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
            if (curtain) Invalidate();   // 게임 뒤 빈 배경으로 (내리지 않음)
            else WindowState = FormWindowState.Minimized;
            if (gameHwnd != IntPtr.Zero) { ShowWindow(gameHwnd, 9); ForceForeground(gameHwnd); }
            if (scaleWas != 0 && gameHwnd != IntPtr.Zero) { int m = scaleWas; scaleWas = 0; Scaler.Start(m, gameHwnd); }
        }

        public void QuitGame()
        {
            if (PopupOpen) ClosePopup(false);
            string dir = GameWatch.Dir ?? ForegroundAppDir();
            if (dir == null) { ShowToast("끝낼 게임을 못 찾았어요", 2500); return; }
            Log.Info("게임 끝내기: " + dir);
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

        /// <summary>팝업을 부르기 전 앞에 있던 프로그램의 폴더 (런처로 안 켠 게임도 끝낼 수 있게). 윈도우·탐색기·런처는 제외</summary>
        public string ForegroundAppDir()
        {
            try
            {
                if (gameHwnd == IntPtr.Zero || gameHwnd == Handle) return null;
                GetWindowThreadProcessId(gameHwnd, out uint pid);
                if ((int)pid == Process.GetCurrentProcess().Id) return null;
                string path = GameWatch.PathOf((int)pid);
                if (path == null) return null;
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (path.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return null;
                string dir = System.IO.Path.GetDirectoryName(path);
                if (dir == null || dir.Length < 8) return null;
                if (dir.Equals(System.IO.Path.GetDirectoryName(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase)) return null;
                return dir;
            }
            catch { return null; }
        }

        public bool CanQuitForeground => !GameWatch.Running && ForegroundAppDir() != null;

        // ───────── 치트 모드 (MiniCheat --panel): 게임 옆 팝업 자리에 포커스 없이 ─────────
        bool cheatOpen, cheatMapWas, cheatPadOffWas;
        DateTime cheatSince;
        Timer cheatWatch;
        CheatPad cheatPad;

        public void OpenCheatPanel()
        {
            IntPtr g = PopupOpen ? gameHwnd : GetForegroundWindow();
            if (g == Handle) g = gameHwnd;
            string dir = GameWatch.Dir ?? ForegroundAppDir();
            uint pid = 0;
            if (g != IntPtr.Zero) GetWindowThreadProcessId(g, out pid);
            if (PopupOpen) ClosePopup(false);
            string exe = FindCheat();
            if (exe != null) { RunCheatPanel(exe, dir, (int)pid); return; }
            DownloadCheat(path => RunCheatPanel(path, dir, (int)pid));
        }

        void RunCheatPanel(string exe, string dir, int pid)
        {
            try
            {
                var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                int w = QuickPanel.PanelWidth();
                var r = new Rectangle(sc.Right - w, sc.Top, w, sc.Height);
                string d = (dir ?? "").TrimEnd('\\');
                string cmd = "show\t" + d + "\t" + pid + "\t" + r.X + "\t" + r.Y + "\t" + r.Width + "\t" + r.Height;
                if (!CheatLink.Send(cmd))
                    Process.Start(new ProcessStartInfo(exe, "--panel --dir \"" + d + "\" --pid " + pid + " --rect " + r.X + "," + r.Y + "," + r.Width + "," + r.Height)
                    { WorkingDirectory = System.IO.Path.GetDirectoryName(exe), UseShellExecute = false });
                BeginCheatInput();
                Log.Info("치트 모드: " + d);
            }
            catch (Exception ex) { Log.Error("치트 모드", ex); ShowToast("치트 모드를 켜지 못했어요: " + ex.Message, 5000); }
        }

        // 치트 팝업이 떠 있는 동안: 게임엔 버튼이 안 가고(빠른 설정 팝업과 같게), 패드는 치트 팝업으로 보냄
        void BeginCheatInput()
        {
            cheatSince = DateTime.Now;
            if (cheatOpen) return;
            cheatOpen = true;
            cheatMapWas = Gamepad.MapKeys; Gamepad.MapKeys = false;
            cheatPadOffWas = VirtualPad.ControllerOff;
            if (VirtualPad.Running) VirtualPad.ControllerOff = true;
            if (cheatPad == null) cheatPad = new CheatPad();
            Gamepad.Overlay = cheatPad;
            if (cheatWatch == null)
            {
                cheatWatch = new Timer { Interval = 300 };
                // ✕ 로 닫으면 미니 치트 창이 숨으니 그걸 보고 되돌림 (처음 켤 땐 창이 뜰 때까지 6초 기다림)
                cheatWatch.Tick += (s, e) => { if ((DateTime.Now - cheatSince).TotalSeconds > 6 && !CheatLink.Visible()) EndCheatInput(); };
            }
            cheatWatch.Start();
        }

        void EndCheatInput()
        {
            if (!cheatOpen) return;
            cheatOpen = false;
            if (cheatWatch != null) cheatWatch.Stop();
            if (Gamepad.Overlay == cheatPad) Gamepad.Overlay = null;
            Gamepad.MapKeys = cheatMapWas && GameWatch.Running;
            VirtualPad.ControllerOff = cheatPadOffWas;
        }

        class CheatPad : IPadTarget
        {
            public void OnPad(Pad b)
            {
                string n = b == Pad.B ? "B" : b == Pad.LB ? "LB" : b == Pad.RB ? "RB" : b == Pad.Up ? "Up" : b == Pad.Down ? "Down" : null;
                if (n != null) CheatLink.Send("pad\t" + n);
            }
        }

        // ───────── 치트 에디터 (MiniCheat) ─────────
        public void OpenCheat()
        {
            if (PopupOpen) ClosePopup(false);
            string exe = FindCheat();
            if (exe != null) { RunCheat(exe); return; }
            DownloadCheat(RunCheat);
        }

        void DownloadCheat(Action<string> then)
        {
            // 없으면 GitHub 최신 릴리스에서 받아 tools 폴더에 둠
            string dest = System.IO.Path.Combine(Settings.Dir, "tools", "MiniCheat.exe");
            ShowToast("치트 에디터를 받는 중...", 8000);
            Task.Run(() =>
            {
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                    using (var wc = new System.Net.WebClient())
                    {
                        wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "TkoolLauncher";
                        string json = wc.DownloadString("https://api.github.com/repos/kakario777-netizen/mini-cheat/releases/latest");
                        string url = TextDecode.JsonString(json, "browser_download_url");
                        if (url == null) throw new Exception("릴리스를 못 찾음");
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                        wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "TkoolLauncher";
                        wc.DownloadFile(url, dest);
                    }
                    BeginInvoke(new Action(() => { Settings.CheatExe = dest; Settings.Save(); then(dest); }));
                }
                catch (Exception ex)
                {
                    Log.Error("치트 에디터 받기", ex);
                    try { BeginInvoke(new Action(() => ShowToast("치트 에디터를 받지 못했어요 — 인터넷 연결을 확인해 주세요", 5000))); } catch { }
                }
            });
        }

        static string FindCheat()
        {
            if (!string.IsNullOrEmpty(Settings.CheatExe) && System.IO.File.Exists(Settings.CheatExe)) return Settings.CheatExe;
            var dirs = new List<string>();
            string me = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            dirs.Add(me);
            try { dirs.Add(System.IO.Path.GetDirectoryName(me)); } catch { }
            dirs.Add(System.IO.Path.Combine(Settings.Dir, "tools"));
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            dirs.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            foreach (var d in dirs)
            {
                if (string.IsNullOrEmpty(d) || !System.IO.Directory.Exists(d)) continue;
                try
                {
                    string f = System.IO.Path.Combine(d, "MiniCheat.exe");
                    if (System.IO.File.Exists(f)) { Settings.CheatExe = f; Settings.Save(); return f; }
                    foreach (var sub in System.IO.Directory.GetDirectories(d))
                    {
                        f = System.IO.Path.Combine(sub, "MiniCheat.exe");
                        if (System.IO.File.Exists(f)) { Settings.CheatExe = f; Settings.Save(); return f; }
                    }
                }
                catch { }
            }
            return null;
        }

        void RunCheat(string exe)
        {
            try
            {
                // 이미 켜져 있으면 앞으로
                foreach (var p in Process.GetProcessesByName("MiniCheat"))
                {
                    try { if (p.MainWindowHandle != IntPtr.Zero) { ShowWindow(p.MainWindowHandle, 9); ForceForeground(p.MainWindowHandle); return; } }
                    finally { p.Dispose(); }
                }
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = System.IO.Path.GetDirectoryName(exe), UseShellExecute = true });
                Log.Info("치트 에디터 실행: " + exe);
            }
            catch (Exception ex) { Log.Error("치트 에디터 실행", ex); ShowToast("치트 에디터를 켜지 못했어요: " + ex.Message, 5000); }
        }

        public void AfterDisplayChange()
        {
            // 해상도·회전이 바뀌면 창을 다시 꽉 채움
            BeginInvoke(new Action(() =>
            {
                WindowState = FormWindowState.Normal;
                Bring();
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

        // 초점이 머문 뒤에만 배경을 바꿈 — 목록을 빠르게 넘길 때마다 전체 화면 배경을 다시 만드는 게 렉의 큰 원인
        Timer bgDelay;
        void UpdateBg()
        {
            // 게임마다 뒷배경 바꾸기는 끔 — 기본 배경(또는 내 배경 사진) 고정 (렉 줄이기)
            if (bgCur != null || bgPrev != null) { bgPrev?.Dispose(); bgPrev = null; bgCur?.Dispose(); bgCur = null; bgFade = 1f; Invalidate(); }
            return;
#pragma warning disable CS0162
            if (bgDelay == null)
            {
                bgDelay = new Timer { Interval = 260 };
                bgDelay.Tick += (s, e) => { bgDelay.Stop(); UpdateBgNow(); };
            }
            bgDelay.Stop();
            bgDelay.Start();
        }

        void UpdateBgNow()
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
            int fq = bgFade >= 1f ? 100 : (int)(bgFade * 4);   // 바뀌는 동안 4번만 다시 만듦
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

        float bgKeyFadeValue() => Math.Min(1f, (int)(bgFade * 4) / 4f);

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
            if (bgFade < 1f) { bgFade = Math.Min(1f, bgFade + dt * 4f); more = true; }
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
        // ───────── 알림 영역(트레이) 상주 ─────────
        // 런처 '종료'는 화면만 닫고 알림 영역으로 내려감 (TDP·팬·AYA 버튼·오버레이는 계속 동작).
        // 진짜 종료는 알림 아이콘 메뉴의 '완전히 종료'.
        NotifyIcon tray;
        bool realExit, trayTipShown;
        public static bool StartInTray;

        void SetupTray()
        {
            if (tray != null) return;
            tray = new NotifyIcon { Text = "쯔꾸르 런처", Visible = true };
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { tray.Icon = SystemIcons.Application; }
            var menu = new ContextMenuStrip { Font = Theme.F(11f) };
            menu.Items.Add("런처 열기", null, (s, e) => { Bring(); OpenLibrary(); });
            menu.Items.Add("빠른 설정", null, (s, e) => { try { ShowPopup(); } catch (Exception ex) { Log.Error("트레이 빠른 설정", ex); } });
            var ov = new ToolStripMenuItem("성능 표시");
            ov.Click += (s, e) => OverlayForm.Toggle(!OverlayForm.IsOn);
            menu.Items.Add(ov);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("완전히 종료", null, (s, e) => ExitForReal());
            menu.Opening += (s, e) => ov.Checked = OverlayForm.IsOn;
            foreach (ToolStripItem it in menu.Items) it.Padding = new Padding(Theme.S(6), Theme.S(8), Theme.S(6), Theme.S(8));
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { Bring(); OpenLibrary(); } };
        }

        /// <summary>런처 화면 띄우기 (알림 영역에 숨어 있어도)</summary>
        public void Bring()
        {
            if (!Visible) { Show(); Log.Info("알림 영역에서 런처 열기"); }
            if (Opacity < 1) Opacity = 1;
            WindowState = FormWindowState.Maximized;
            try { Activate(); } catch { }
        }

        public void ToTray()
        {
            Hide();
            Log.Info("런처 화면 닫음 — 알림 영역에서 계속 실행");
            if (!trayTipShown && tray != null)
            {
                trayTipShown = true;
                try { tray.ShowBalloonTip(3000, "쯔꾸르 런처", "알림 영역에서 계속 돌아가요. AYA 버튼으로 빠른 설정, 완전히 끄려면 이 아이콘 메뉴에서 '완전히 종료'.", ToolTipIcon.Info); } catch { }
            }
        }

        public void ExitForReal()
        {
            CheatLink.Send("quit");
            realExit = true; TaskbarHide.Show();
            Log.Info("알림 아이콘에서 완전히 종료");
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (StartInTray) { StartInTray = false; BeginInvoke(new Action(() => { ToTray(); Opacity = 1; })); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!realExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                ToTray();
                return;
            }
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
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
            Hotkeys.Stop();
            try { ResForce.Restore(); } catch { }
            try { Scaler.Stop(); } catch { }
            try { VirtualPad.Stop(); } catch { }
            LoopGuard.Stop();
            Fps.Stop();
            Bgm.Close();
            Hardware.Shutdown();
            OverlayForm.Toggle(false, false);
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
            if (curtain && !CameFromGame) { PaintCurtain(g); return; }   // 게임 켜고 끌 때: 빈 배경 + 글자 하나
            Cur.Paint(g);
            PaintTopBar(g);
            menu.Paint(g, ClientRectangle);
            quick.Paint(g, ClientRectangle);
            PaintBottomBar(g);
            PaintToast(g);
        }

        // ───────── 게임 켜고 끌 때 가림막 ─────────
        // 런처를 내리지 않고 게임 뒤에 빈 배경으로 깔아 둠 → 켜는 동안·끄고 돌아오는 동안 바탕화면·작업 표시줄이 안 보임
        bool curtain, curtainSeen, curtainLeaving;
        string curtainTitle;
        IntPtr curtainGameWin;

        void ShowCurtain(string title)
        {
            curtain = true; curtainSeen = false; curtainLeaving = false; curtainTitle = title;
            menu.Open = false; quick.Open = false; quick.T = 0;
            if (!Visible) Show();
            WindowState = FormWindowState.Maximized;
            Invalidate(); Update();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);

        /// <summary>창 모드 게임은 검은 판 한가운데로 (꽉 찬 창·전체 화면은 그대로)</summary>
        void CenterGameWin(IntPtr h, bool any = false)
        {
            try
            {
                if (h == IntPtr.Zero || IsZoomed(h)) return;
                if (!any && (PlayingGame == null || GameData.Get(PlayingGame).WinMode != 0)) return;
                WRECT wr; if (!GetWindowRect(h, out wr)) return;
                var sb = System.Windows.Forms.Screen.FromHandle(h).Bounds;
                int w = wr.R - wr.L, ht = wr.B - wr.T;
                if (w >= sb.Width - 8 && ht >= sb.Height - 8) return;     // 전체 화면
                if (w > sb.Width || ht > sb.Height) return;
                SetWindowPos(h, IntPtr.Zero, sb.X + (sb.Width - w) / 2, sb.Y + (sb.Height - ht) / 2, 0, 0, 0x0001 | 0x0004 | 0x0010);
            }
            catch (Exception ex) { Log.Error("게임 창 가운데로", ex); }
        }

        void PaintCurtain(Graphics g)
        {
            var r = ClientRectangle;
            // 플레이 중엔 런처 안에서 게임이 뜬 것처럼 검은 판만 (게임 창이 그 위에)
            if (curtainSeen && !curtainLeaving) { g.Clear(Color.Black); return; }
            int cy = r.Height / 2;
            if (!string.IsNullOrEmpty(curtainTitle))
                Draw.Text(g, curtainTitle, Theme.Px(24, true), new Rectangle(r.X + Theme.S(40), cy - Theme.S(60), r.Width - Theme.S(80), Theme.S(44)), Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Draw.Text(g, curtainLeaving ? "돌아가는 중…" : "불러오는 중…", Draw.Menu, new Rectangle(r.X, cy, r.Width, Theme.S(36)), Theme.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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
            // 왼쪽: 런처가 뒤에서 하는 일
            float prog;
            string act = Activity.Line(out prog);
            if (act == null && Scanning) act = "게임 찾는 중";
            actRect = Rectangle.Empty;
            if (act != null)
            {
                bool idle = !Activity.Any && !Scanning;
                actRect = new Rectangle(0, r.Y, Math.Max(Theme.S(120), x - Theme.S(10)), h);
                int ax = Theme.S(24), aw = Math.Max(Theme.S(120), x - ax - Theme.S(20));
                // 도는 점 세 개 (쉬는 중이면 흐린 점)
                int t = idle ? -1 : (Environment.TickCount / 300) % 3;
                for (int i = 0; i < 3; i++)
                    using (var b = new SolidBrush(i == t ? Theme.Accent : Color.FromArgb(idle ? 50 : 90, 255, 255, 255)))
                        g.FillEllipse(b, ax + i * Theme.S(10), cy - Theme.S(3), Theme.S(6), Theme.S(6));
                int tx = ax + Theme.S(36);
                Draw.Text(g, act, Draw.Small, new Rectangle(tx, r.Y, aw - Theme.S(36), prog >= 0 ? h - Theme.S(10) : h), Theme.SubText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (prog >= 0)
                {
                    int bw = Math.Min(Theme.S(260), aw - Theme.S(36));
                    var bar = new Rectangle(tx, r.Bottom - Theme.S(12), bw, Theme.S(4));
                    Draw.Fill(g, bar, Theme.S(2), Color.FromArgb(50, 255, 255, 255));
                    Draw.Fill(g, new Rectangle(bar.X, bar.Y, Math.Max(Theme.S(4), (int)(bw * prog)), bar.Height), Theme.S(2), Theme.Accent);
                }
            }
        }

        Rectangle actRect;
        /// <summary>아래 왼쪽을 누르면: 지금 하는 일 + 지난 일 목록</summary>
        public void ShowActivityLog()
        {
            float pr;
            var l = new List<string>();
            if (Activity.Any) l.Add("▶ " + Activity.Line(out pr));
            l.AddRange(Activity.History());
            if (l.Count == 0) l.Add("아직 기록이 없어요");
            PickerScreen.Show(this, "런처가 한 일", l.ToArray(), -1, i => { }, "새로 넣은 게임·이름 확인·DLsite·중복 검사 등");
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
            try { Cur.DownAt = e.Location; } catch { }
        }

        bool quickScroll;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!isDown || menu.Open) return;
            int dx = e.X - down.X, dy = e.Y - down.Y;
            if (quick.Open)
            {
                // 위아래로 밀면 목록 밀기, 옆으로(슬라이더 위)면 슬라이더 끌기
                if (!dragging && (Math.Abs(dx) > Theme.S(12) || Math.Abs(dy) > Theme.S(12)))
                {
                    dragging = true;
                    quickScroll = Math.Abs(dy) > Math.Abs(dx) && !quick.OnSlider(down);
                    if (quickScroll) quick.ScrollDrag(0, true);
                }
                if (dragging) { if (quickScroll) quick.ScrollDrag(dy, false); else quick.OnDrag(e.Location); Invalidate(); }
                return;
            }
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
            if (actRect.Contains(p)) { ShowActivityLog(); return; }
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
                if (f.Changed || firstRun) { Rescan(); WatchFolders(); }
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

        // ───────── 게임 폴더 지켜보기: 새 게임을 넣거나 지우면 조용히 다시 찾기 ─────────
        readonly List<System.IO.FileSystemWatcher> watchers = new List<System.IO.FileSystemWatcher>();
        Timer rescanDelay;
        int knownCount = -1;

        public void WatchFolders()
        {
            foreach (var w in watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
            watchers.Clear();
            foreach (var root in Settings.GameFolders)
            {
                try
                {
                    if (!System.IO.Directory.Exists(root)) continue;
                    var w = new System.IO.FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = System.IO.NotifyFilters.DirectoryName | System.IO.NotifyFilters.FileName, InternalBufferSize = 65536 };
                    System.IO.FileSystemEventHandler h = (o, e) => SoonRescan(e.FullPath);
                    w.Created += h; w.Deleted += h;
                    w.Renamed += (o, e) => SoonRescan(e.FullPath);
                    w.Error += (o, e) => SoonRescan(null);
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (Exception ex) { Log.Error("게임 폴더 지켜보기 " + root, ex); }
            }
        }

        static readonly string[] GameFileExt = { ".exe", ".rgss3a", ".rgss2a", ".rgssad", ".ldb", ".json", ".ini" };

        /// <summary>압축 풀기·복사가 끝날 때까지 30초 조용하면 다시 찾기 (게임 중이면 끝난 뒤)</summary>
        void SoonRescan(string path)
        {
            if (path != null)
            {
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                // 세이브·로그 같은 잔 파일은 무시, 폴더·실행 파일·게임 데이터만
                if (ext.Length > 0 && Array.IndexOf(GameFileExt, ext) < 0) return;
                if (path.IndexOf("쯔꾸르 런처", StringComparison.Ordinal) >= 0) return;
            }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (rescanDelay == null)
                    {
                        rescanDelay = new Timer { Interval = 30000 };
                        rescanDelay.Tick += (o, e) =>
                        {
                            if (GameWatch.Running || Scanning || GameCapture.Finding) return;   // 게임 중엔 기다림
                            rescanDelay.Stop();
                            Activity.Done("watch");
                            Log.Info("게임 폴더가 바뀌어 다시 찾기");
                            Rescan();
                        };
                    }
                    rescanDelay.Stop(); rescanDelay.Start();
                    Activity.Set("watch", "게임 폴더가 바뀌어 곧 다시 찾아요");
                }));
            }
            catch { }
        }

        public void Rescan()
        {
            if (Scanning) return;
            Scanning = true;
            Activity.Set("scan", "게임 폴더 살펴보는 중");
            Invalidate();
            var roots = Settings.GameFolders.ToList();
            Task.Run(() =>
            {
                GameDoctor.RestoreRenamed(roots);   // 예전 버전이 바꿔 버린 폴더 이름 되돌리기
                List<GameInfo> found;
                try { found = GameScanner.ScanAll(roots); } catch { found = new List<GameInfo>(); }
                BeginInvoke(new Action(() =>
                {
                    Scanning = false;
                    Activity.Done("scan", "게임 " + found.Count + "개 찾음");
                    int before = knownCount;
                    SetGames(found);
                    knownCount = found.Count;
                    if (before < 0) ShowToast("게임 " + found.Count + "개를 찾았어요");
                    else if (found.Count > before) ShowToast("새 게임 " + (found.Count - before) + "개를 목록에 넣었어요", 4000);
                    else if (found.Count < before) ShowToast("없어진 게임 " + (before - found.Count) + "개를 목록에서 뺐어요", 4000);
                }));
            });
        }

        public void RefreshLists()
        {
            foreach (var s in stack) s.OnGamesChanged();
            FocusChanged();
        }

        public GameInfo PlayingGame;

        // ───────── 지금 하는 게임과 맞추기 (플레이 시간·끝남·오류) ─────────
        public bool TrackedExternal;          // 런처 밖에서 켠 게임을 알아서 붙잡은 경우
        public string GameProblem;            // 응답 없음·오류 창 등 (팝업 맨 위에 표시)
        GameStat playStat; DateTime playStart;
        Timer playTimer, healthTimer;
        int hungCount, noWinCount; bool healthBusy;

        /// <summary>게임이 끝난 뒤: 오류로 멈췄거나 튕겼으면 원인(윈도우 튕김 기록)을 보고 스스로 고침</summary>
        void CheckAfterEnd(GameInfo g, string dir, DateTime since, string problem)
        {
            new System.Threading.Thread(() =>
            {
                string crash = "";
                try { System.Threading.Thread.Sleep(1500); crash = Fixer.CrashInfo(dir, since); } catch { }
                string text = ((problem ?? "") + "\n" + crash + "\n" + lastBoxText).Trim();
                lastBoxText = "";
                if (crash.Length == 0 && problem == null) return;
                if (Fixer.CanFix(g, text))
                {
                    try { BeginInvoke(new Action(() => ShowToast("게임이 오류로 꺼져서 원인을 고치는 중이에요", 5000))); } catch { }
                    string r = Fixer.FixNow(g, text);
                    try { BeginInvoke(new Action(() => ShowToast("고쳤어요: " + r + " — 다시 켜 보세요", 7000))); } catch { }
                }
                else
                {
                    Fixer.NoteUnknown(g, text);
                    if (crash.Length > 0) try { BeginInvoke(new Action(() => ShowToast("게임이 튕겼어요 (" + crash + ") — '런처가 한 일'에 적어 뒀어요", 7000))); } catch { }
                }
            }) { IsBackground = true }.Start();
        }
        volatile string lastBoxText = "";

        /// <summary>메시지 상자 제목 + 안 글</summary>
        static string BoxText(IntPtr h, string title)
        {
            var sb = new System.Text.StringBuilder(title + "\n");
            try { EnumChildWindows(h, (c, l) => { var t = new System.Text.StringBuilder(512); GetWindowText(c, t, 512); if (t.Length > 2) sb.Append(t).Append('\n'); return true; }, IntPtr.Zero); } catch { }
            return sb.ToString();
        }

        public TimeSpan PlayingFor => PlayingGame != null ? DateTime.Now - sessionStart : TimeSpan.Zero;
        DateTime sessionStart;

        void Track(GameInfo g, Process p, bool ours)
        {
            var st = GameData.Get(g);
            st.LastPlayed = DateTime.Now.Ticks;
            GameData.Save();
            playStat = st; playStart = sessionStart = DateTime.Now;
            PlayingGame = g; TrackedExternal = !ours; GameProblem = null; hungCount = 0; noWinCount = 0;
            Log.Info((ours ? "게임 시작: " : "런처 밖에서 켠 게임 확인: ") + g.Title);
            playTimer?.Dispose();
            playTimer = new Timer { Interval = 60000 };   // 1분마다 플레이 시간 저장 (꺼져도 안 잃게)
            playTimer.Tick += (o, e) => FlushPlayTime();
            playTimer.Start();
            string dir = g.InstallDir ?? g.Folder;
            GameWatch.Start(p, dir, () => BeginInvoke(new Action(() =>
            {
                FlushPlayTime();
                playTimer?.Dispose(); playTimer = null;
                Log.Info("게임 끝: " + g.Title + " (이번 " + (int)(DateTime.Now - sessionStart).TotalMinutes + "분, 총 " + st.TotalSeconds / 60 + "분)");
                string hadProblem = GameProblem; var startedAt = sessionStart; var endedGame = g;
                playStat = null; PlayingGame = null; TrackedExternal = false; GameProblem = null;
                CheckAfterEnd(endedGame, dir, startedAt, hadProblem);
                if (curtain && ours) { curtainLeaving = true; Invalidate(); Update(); System.Threading.Thread.Sleep(450); }
                curtain = false; curtainLeaving = false; TaskbarHide.Show();
                if (coverFor != null) EndManualCover();
                CameFromGame = false;
                quick.Open = false; quick.T = 0;
                if (PopupOpen) ClosePopup(false);
                Gamepad.MapKeys = false;
                ResForce.Restore();
                if (Updater.RestartPending) { Updater.Restart(); return; }
                foreach (var sc in stack) if (!(sc is DetailScreen)) sc.OnGamesChanged();
                if (ours) { Bring(); ForceForeground(Handle); }   // 런처로 켠 게임이 끝나면 라이브러리 다시 올림
                Invalidate();
            })));
        }

        void FlushPlayTime()
        {
            if (playStat == null) return;
            var now = DateTime.Now;
            playStat.TotalSeconds += (long)(now - playStart).TotalSeconds;
            playStart = now;
            playStat.LastPlayed = now.Ticks;
            GameData.Save();
        }

        delegate bool EnumWinProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct WRECT { public int L, T, R, B; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out WRECT r);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumWinProc f, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumWinProc f, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);

        /// <summary>3초마다: 런처 밖에서 켠 게임 붙잡기 + 하는 게임이 멈췄는지·오류 창이 떴는지 보기</summary>
        void StartHealth()
        {
            healthTimer = new Timer { Interval = 3000 };
            healthTimer.Tick += (o, e) =>
            {
                if (healthBusy) return;
                healthBusy = true;
                var g = PlayingGame;
                string dir = g != null ? (g.InstallDir ?? g.Folder) : null;
                bool front = LauncherFront;
                IntPtr fg = IntPtr.Zero;
                try { fg = GetForegroundWindow(); } catch { healthBusy = false; return; }
                var lib = Games;
                bool tk = g != null && g.IsTkool;
                if (lib == null) { healthBusy = false; return; }
                Task.Run(() =>
                {
                    GameInfo attach = null; string problem = null, boxText = ""; bool hung = false, anyWin = false;
                    IntPtr bigWin = IntPtr.Zero; int bigArea = 0;
                    try
                    {
                        if (g == null && !front && !GameWatch.Running)
                        {
                            GetWindowThreadProcessId(fg, out uint pid);
                            string path = GameWatch.PathOf((int)pid);
                            if (path != null && (int)pid != Process.GetCurrentProcess().Id)
                                attach = lib.FirstOrDefault(x =>
                                {
                                    string d = (x.InstallDir ?? x.Folder ?? "").TrimEnd('\\');
                                    return d.Length > 8 && path.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase);
                                });
                        }
                        else if (g != null)
                        {
                            var pids = new Dictionary<uint, bool>();
                            EnumWindows((h, l) =>
                            {
                                if (!IsWindowVisible(h)) return true;
                                GetWindowThreadProcessId(h, out uint wp);
                                bool mine;
                                if (!pids.TryGetValue(wp, out mine)) { mine = GameWatch.InDir((int)wp, dir); pids[wp] = mine; }
                                var cls = new System.Text.StringBuilder(64); GetClassName(h, cls, 64);
                                var tt = new System.Text.StringBuilder(256); GetWindowText(h, tt, 256);
                                if (mine)
                                {
                                    anyWin = true;
                                    WRECT wr;
                                    if (GetWindowRect(h, out wr)) { int ar = (wr.R - wr.L) * (wr.B - wr.T); if (ar > bigArea) { bigArea = ar; bigWin = h; } }
                                    if (IsHungAppWindow(h)) hung = true;
                                    if (cls.ToString() == "#32770" && tt.Length > 0)
                                    {
                                        string bt = BoxText(h, tt.ToString());
                                        if (!tk || Fixer.CanFix(g, bt)) { problem = "게임에 오류 창이 떴어요: " + tt; boxText = bt; }
                                    }
                                }
                                else
                                {
                                    // 윈도우 '프로그램이 멈췄어요' 창 (WerFault)
                                    if (cls.ToString() == "#32770" && Fixer.IsGameSysErr(tt.ToString(), dir)) { boxText = BoxText(h, tt.ToString()); problem = "게임에 오류 창이 떴어요: " + tt; }
                                    string pp = GameWatch.PathOf((int)wp) ?? "";
                                    if (pp.EndsWith("\\WerFault.exe", StringComparison.OrdinalIgnoreCase) && tt.Length > 0)
                                        problem = "게임이 오류로 멈췄어요 (" + tt + ")";
                                }
                                return true;
                            }, IntPtr.Zero);
                        }
                    }
                    catch { }
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            healthBusy = false;
                            if (attach != null && PlayingGame == null && !GameWatch.Running && !GameCapture.Finding) { SmartTdp.Reset(); Track(attach, null, false); ResForce.Apply(attach); return; }
                            if (PlayingGame == null) return;
                            // 런처로 켠 게임인데 게임 창이 15초 넘게 하나도 없으면(뒤에 남은 프로그램 때문에 안 끝나는 경우) 끝난 걸로
                            noWinCount = anyWin ? 0 : noWinCount + 1;
                            if (anyWin && curtain)
                            {
                                if (!curtainSeen) { curtainSeen = true; Invalidate(); CenterGameWin(bigWin); if (!Settings.TaskbarInGame) TaskbarHide.Hide(); }
                                // 게임 창이 가림막 뒤에 숨어 있으면 앞으로 (스팀으로 켠 게임 등)
                                if (!CameFromGame && Form.ActiveForm == this && bigWin != IntPtr.Zero && !PopupOpen) ForceForeground(bigWin);
                            }
                            if (!TrackedExternal && noWinCount >= 5 && (DateTime.Now - sessionStart).TotalSeconds > 25 && !GameWatch.ForceEnd)
                            {
                                Log.Info("게임 창이 사라졌는데 프로그램이 남아 있어 끝난 걸로 봄: " + PlayingGame.Title);
                                GameWatch.ForceEnd = true;
                                return;
                            }
                            hungCount = hung ? hungCount + 1 : 0;
                            if (problem == null && hungCount >= 4) problem = "게임이 응답하지 않아요 (12초 넘게 멈춤)";
                            if (boxText.Length > 0) lastBoxText = boxText;
                            // 고칠 수 있는 오류 창(dll 없음 등)이면 묻지 않고: 게임 끄고 → 고치고 → 다시 켬
                            if (boxText.Length > 0 && !TrackedExternal && Fixer.CanFix(PlayingGame, boxText) && !Fixer.Busy)
                            {
                                var fgame = PlayingGame; string fdir = fgame.InstallDir ?? fgame.Folder;
                                Log.Info("플레이 중 오류 창 → 스스로 고침: " + boxText.Replace("\n", " / "));
                                lastBoxText = "";
                                GameCapture.KillDir(fdir);
                                ShowToast("오류 원인을 찾았어요 — 런처가 고치는 중이에요 (끝나면 다시 켤게요)", 6000);
                                Fixer.FixLater(fgame, boxText, r => { try { BeginInvoke(new Action(() => { ShowToast("고쳤어요: " + r + " — 다시 켤게요", 5000); if (!GameWatch.Running) Launch(fgame); })); } catch { } });
                                return;
                            }
                            if (problem != null && problem != GameProblem)
                            {
                                GameProblem = problem;
                                Log.Info("게임 문제 감지: " + PlayingGame.Title + " — " + problem);
                                try { tray?.ShowBalloonTip(5000, PlayingGame.Title, problem + "\nAYA 버튼 → 게임 끝내기", ToolTipIcon.Warning); } catch { }
                                if (!PopupOpen && !LauncherFront) { QuickPanel.GoShortcuts(); ShowPopup(); }   // 바로 끝낼 수 있게 팝업을 띄움
                            }
                            else if (problem == null && hungCount == 0 && GameProblem != null && GameProblem.StartsWith("게임이 응답하지")) GameProblem = null;
                        }));
                    }
                    catch { healthBusy = false; }
                });
            };
            healthTimer.Start();
        }

        // ───────── 표지 직접 찍기: 게임을 켜고, 원하는 화면에서 Y를 빠르게 두 번 ─────────
        GameInfo coverFor;
        OverlayTip coverTip;

        public void ManualCover(GameInfo g)
        {
            if (GameWatch.Running && PlayingGame != g) { ShowToast("다른 게임을 끈 다음에 해 주세요", 3000); return; }
            coverFor = g;
            coverTip?.Close();
            coverTip = new OverlayTip();
            coverTip.SetText("표지로 쓸 화면에서 Y를 빠르게 두 번 누르세요  ·  그만두기: AYA 버튼");
            coverTip.Show();
            Gamepad.DoubleY = () => { try { BeginInvoke(new Action(TakeManualCover)); } catch { } };
            Log.Info("표지 직접 찍기 시작: " + g.Title);
            if (!GameWatch.Running) Launch(g);
            else ReturnToGame();
        }

        Action retakeDone;   // 표지 확인 화면의 "다시 찍기" (찍은 그림은 확인 목록으로)

        /// <summary>표지 확인 목록에서 한 게임만 다시 찍기: 켜고 Y 두 번 → 확인 목록 그림을 바꾸고 게임 끄고 돌아옴</summary>
        public void RetakeCover(GameInfo g, Action done)
        {
            retakeDone = done;
            ManualCover(g);
            coverTip?.SetText("다시 찍기: 표지로 쓸 화면에서 Y를 빠르게 두 번  ·  그만두기: AYA 버튼");
        }

        void TakeManualCover()
        {
            var g = coverFor;
            if (g == null) return;
            if (retakeDone != null)
            {
                bool ok2 = false;
                try { ok2 = GameCapture.ShotNow(g, true); } catch (Exception ex) { Log.Error("다시 찍기", ex); }
                if (!ok2) { coverTip?.SetText("찍지 못했어요 — 게임 화면이 앞에 있을 때 다시 Y 두 번"); return; }
                var d = retakeDone; retakeDone = null;
                EndManualCover("다시 찍었어요 — 확인 목록으로 돌아가요");
                GameCapture.KillDir(g.InstallDir ?? g.Folder);   // 게임 끄고 확인 목록으로
                d?.Invoke();
                return;
            }
            bool ok = false;
            try { ok = GameCapture.ShotNow(g); } catch (Exception ex) { Log.Error("표지 찍기", ex); }
            if (!ok) { coverTip?.SetText("찍지 못했어요 — 게임 화면이 앞에 있을 때 다시 Y 두 번"); return; }
            Log.Info("표지 직접 찍음: " + g.Title);
            EndManualCover("표지로 저장했어요 — 화면 글자로 이름도 찾아볼게요");
            RefreshLists();
            // 같은 화면에서 게임 이름 글자 읽기 (이름이 뜻 없음·외국어면 넣음, 후보는 이름 바꾸기 메뉴에)
            string shot = CoverArt.AutoShotPath(g);
            new System.Threading.Thread(() =>
            {
                Activity.Set("ocr", "타이틀 화면에서 이름 읽는 중 · " + g.Title, 0, 0);
                string n = Ocr.NameFromShot(g, shot, true);
                Activity.Done("ocr", n != null ? "화면 글자로 이름 넣음 · " + n : "화면 글자 읽기 끝 · " + g.Title);
                try { BeginInvoke(new Action(() => { if (n != null) { ShowToast("이름도 넣었어요: " + n, 4000); CoverArt.Invalidate(g); RefreshLists(); } })); } catch { }
            }) { IsBackground = true }.Start();
        }

        public void EndManualCover(string msg = null)
        {
            if (msg == null) retakeDone = null;   // 그만두면 다시 찍기도 취소
            Gamepad.DoubleY = null;
            coverFor = null;
            var tip = coverTip; coverTip = null;
            if (tip == null) return;
            if (msg == null) { tip.Close(); return; }
            tip.SetText(msg);
            var t = new Timer { Interval = 1800 };
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); tip.Close(); };
            t.Start();
        }

        /// <summary>표지·이름 없는 게임을 하나씩 잠깐 켜서 채우기</summary>
        public void FindCovers(List<GameInfo> only = null)
        {
            if (GameWatch.Running) { ShowToast("게임을 끈 다음에 해 주세요", 3000); return; }
            var list = only ?? GameCapture.Candidates(Games);   // 표지 없음 확인은 찍기 시작하면서 하나씩
            if (list.Count == 0) { ShowToast("찍어 볼 게임이 없어요", 3000); return; }
            Gamepad.MapKeys = false;
            WindowState = FormWindowState.Minimized;
            var tip = new OverlayTip();
            tip.Show();
            GameCapture.FindAll(list,
                msg => { try { BeginInvoke(new Action(() => tip.SetText("표지 찾는 중 " + msg))); } catch { } },
                n => BeginInvoke(new Action(() =>
                {
                    tip.Close();
                    Bring();
                    Activate();
                    RefreshLists();
                    int pend = GameCapture.Pending(Games).Count;
                    if (pend > 0) { Push(new CoverReviewScreen()); ShowToast("찍은 표지 " + pend + "개 — 아닌 것만 눌러 취소하고 적용하세요", 5000); }
                    else ShowToast(list.Count + "개 중 " + n + "개의 이름을 채웠어요", 5000);
                })));
        }

        Timer dupTimer;
        int dupShown = -1;
        /// <summary>썸네일이 다 읽힌 뒤(20초 뒤) 중복 게임 찾기</summary>
        void ScheduleDupScan()
        {
            dupTimer?.Dispose();
            dupTimer = new Timer { Interval = 20000 };
            dupTimer.Tick += (o, e) =>
            {
                dupTimer.Stop();
                DupFinder.Scan(Games, () =>
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            int n = DupFinder.Groups.Count;
                            if (n > 0 && n != dupShown && Visible) ShowToast("중복으로 보이는 게임 " + n + "묶음 — 쯔꾸르 탭 '중복 확인'", 5000);
                            dupShown = n;
                            Invalidate();
                        }));
                    }
                    catch { }
                });
            };
            dupTimer.Start();
        }

        public void SetGames(List<GameInfo> found)
        {
            GameDoctor.Library = found;
            GameData.Reconcile(found);   // 폴더를 옮기거나 이름을 바꿔도 예전 기록 그대로
            // 게임 창 제목에서 가져와 저장해 둔 진짜 이름 적용
            foreach (var g in found)
            {
                string t = GameData.Get(g).Title;
                bool fixedByUser = t != null && t.StartsWith("=");   // 정리 모드에서 직접 고른 이름은 그대로
                if (fixedByUser) t = t.Substring(1);
                g.RawTitle = g.Title;
                if (!string.IsNullOrEmpty(t)) g.Title = t;
                // 쯔꾸르: 버전·날짜·[꼬리표]·_kr 같은 걸 떼고 깔끔한 이름으로 (떼어 낸 건 꼬리표로)
                g.Tags.Clear();
                if (g.FromFolder)
                {
                    string cleaned = TitleTidy.Clean(g.Title, g.Tags);
                    if (!fixedByUser) g.Title = cleaned;
                }
            }
            Games = found;
            CoverArt.Preload(found);   // 목록 썸네일을 미리 메모리에
            ScheduleDupScan();
            // 뜻 없는 이름(RJ번호·Game·my…)이나 한글판인데 영어 이름인 게임: 읽어보기·DLsite에서 진짜 이름 찾기 (한 번만)
            // RJ 번호가 이미 있는 게임은 DLsite 정보부터 바로 (이름 확인과 같이 돎)
            // 이름 확인(오래 걸림)을 안 기다리고: DLsite(번호 있는 것) → 끝나면 바로 스팀·ふりーむ(정보 없는 것)
            DlsiteInfo.RunAll(found, m =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (m > 0) RefreshLists();
                        WebInfo.RunAll(Games, w => { if (w > 0) try { BeginInvoke(new Action(() => { RefreshLists(); ShowToast("스팀·ふりーむ에서 게임 " + w + "개의 정보·그림을 채웠어요", 4000); })); } catch { } });
                    }));
                }
                catch { }
            });
            // 모든 쯔꾸르 게임 한 번씩 확인(흔적 남김): 이름 찾기·외국어 이름 번역·DLsite 검색 → 그다음 DLsite 정보·그림
            NameFinder.RunAll(found, true, n =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (n > 0) { RefreshLists(); ShowToast("게임 " + n + "개의 이름을 찾거나 한글로 바꿨어요 (상세 화면에 원래 이름)", 5000); }
                        DlsiteInfo.RunAll(Games, m =>
                        {
                            try
                            {
                                BeginInvoke(new Action(() =>
                                {
                                    if (m > 0) { RefreshLists(); ShowToast("DLsite에서 게임 " + m + "개의 정보·그림을 채웠어요", 4000); }
                                    // DLsite에 없는 게임은 스팀·ふりーむ에서
                                    WebInfo.RunAll(Games, w => { if (w > 0) try { BeginInvoke(new Action(() => { RefreshLists(); ShowToast("스팀·ふりーむ에서 게임 " + w + "개의 정보를 채웠어요", 4000); })); } catch { } });
                                }));
                            }
                            catch { }
                        });
                    }));
                }
                catch { }
            });
            LayoutScreens();
            foreach (var s in stack) s.OnGamesChanged();
            FocusChanged();
        }

        public void Launch(GameInfo g)
        {
            if (GameWatch.Running) { ShowToast("다른 게임이 아직 실행 중이에요", 3000); return; }
            // 쯔꾸르: 켜기 전에 확실한 문제(빠진 dll·RTP·막힌 파일 등)가 보이면 먼저 물어봄
            if (g.IsTkool)
            {
                List<GameDoctor.Issue> pre = null;
                try { pre = GameDoctor.Check(g).Where(i => i.Sure).ToList(); } catch { }
                if (pre != null && pre.Count > 0)
                {
                    Log.Info("실행 전 문제: " + g.Title + " — " + string.Join(" / ", pre.Select(i => i.Title)));
                    using (var d = new FixDialog(g, pre, true, ""))
                    {
                        d.ShowDialog(this);
                        if (d.Result != "") ShowToast(d.Result, 5000);
                        if (!d.RunAfter) return;
                    }
                }
            }
            Log.Info("게임 실행: " + g.Title + " (" + g.Kind + " " + g.VersionLabel + ") " + (g.LaunchUri ?? (g.Launcher != null ? g.Launcher + " " + g.Args : g.Exe)));
            ResForce.Apply(g);   // 이 게임에 정해 둔 해상도가 있으면 먼저 바꿈
            Process p = null;
            try
            {
                if (g.LaunchUri != null)
                    Process.Start(new ProcessStartInfo(g.LaunchUri) { UseShellExecute = true });
                else if (g.Launcher != null)
                    Process.Start(new ProcessStartInfo(g.Launcher, g.Args ?? "") { WorkingDirectory = Path.GetDirectoryName(g.Launcher), UseShellExecute = true });
                else
                {
                    p = GameCapture.StartGame(g);   // 방화벽 허용 · 연결 경로(# % ?·영문) · 일본어 환경
                }
            }
            catch (Exception ex)
            {
                Log.Error("게임 실행 " + g.Title, ex);
                ShowToast("실행하지 못했어요: " + ex.Message, 5000);
                return;
            }

            // 켠 뒤: 금방 꺼지거나 오류 창·폴더 목록이 뜨면 원인 찾아서 물어봄
            GameDoctor.Watch(g, p, (seen, alive) =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (Fixer.CanFix(g, seen))
                        {
                            try { if (alive && !p.HasExited) p.Kill(); } catch { }
                            GameCapture.KillDir(g.InstallDir ?? g.Folder);
                            ShowToast("오류 원인을 찾았어요 — 런처가 고치는 중이에요 (끝나면 다시 켤게요)", 6000);
                            Fixer.FixLater(g, seen, r => { try { BeginInvoke(new Action(() => { ShowToast("고쳤어요: " + r + " — 다시 켤게요", 5000); if (!GameWatch.Running) Launch(g); })); } catch { } });
                            return;
                        }
                        var t = new Timer { Interval = alive ? 300 : 1500 };   // 런처가 다시 앞에 나온 뒤에
                        t.Tick += (o, e) =>
                        {
                            t.Stop(); t.Dispose();
                            var issues = GameDoctor.Check(g, seen);
                            if (alive) { Bring(); Activate(); }
                            using (var d = new FixDialog(g, issues, false, seen))
                            {
                                d.ShowDialog(this);
                                if (d.DialogResult == DialogResult.OK && alive) { try { if (!p.HasExited) p.Kill(); } catch { } }
                                if (d.Result != "") ShowToast(d.Result + " — 다시 실행해 보세요", 6000);
                            }
                        };
                        t.Start();
                    }));
                }
                catch { }
            });

            ShowCurtain(g.Title);   // 게임하는 동안 런처는 빈 배경으로 게임 뒤에

            // 키보드 변환은 패드를 못 읽는 쯔꾸르(2000~VX Ace)만
            Gamepad.MapKeys = g.IsTkool && g.Version != TkoolVersion.MV && g.Version != TkoolVersion.MZ;
            CameFromGame = false;
            SmartTdp.Reset();
            Track(g, p, true);
            int winMode = GameData.Get(g).WinMode;
            if (winMode == 1) WinFit.Apply(g.InstallDir ?? g.Folder);   // 꽉 찬 창 (작업 표시줄 보임)
            if (Settings.AutoCover) GameCapture.Watch(this, g);
            if (Settings.AutoScaleTkool && Gamepad.MapKeys && winMode == 0)
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
        }
    }
}
