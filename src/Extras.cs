using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TkoolLauncher
{
    /// <summary>버튼에 붙일 수 있는 기능 목록</summary>
    static class ButtonActions
    {
        public static readonly string[] Names =
        {
            "없음", "빠른 설정", "런처 홈", "스크린샷", "작업 전환", "ESC", "화상 키보드", "성능 표시 켜기/끄기", "미니 모드", "조이스틱 마우스 모드"
        };

        public static void Run(ShellForm shell, int a)
        {
            switch (a)
            {
                case 1: shell.HomeAction(); break;
                case 2: shell.SmallAction(); break;
                case 3: KeySender.Combo(KeySender.Win, KeySender.PrtSc); break;
                case 4: KeySender.Combo(KeySender.Win, KeySender.Tab); break;
                case 5: KeySender.Combo(KeySender.Esc); break;
                case 6: try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("osk.exe") { UseShellExecute = true }); } catch { } break;
                case 7: OverlayForm.Toggle(!OverlayForm.IsOn); break;
                case 8: shell.EnterMini(); break;
                case 9: Gamepad.MouseMode = !Gamepad.MouseMode; shell.ShowToast(Gamepad.MouseMode ? "조이스틱 마우스 모드 켬" : "조이스틱 마우스 모드 끔"); break;
            }
        }
    }

    /// <summary>화면 위쪽에 잠깐 떠 있는 안내 (표지 찾는 중)</summary>
    class OverlayTip : Form
    {
        string text = "";
        public OverlayTip()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(23, 29, 37); Opacity = 0.92;
            var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            Size = new Size(Math.Min(sc.Width - 40, Theme.S(900)), Theme.S(44));
            Location = new Point((sc.Width - Width) / 2, Theme.S(12));
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000 | 0x20 | 0x80000; return cp; } }
        protected override bool ShowWithoutActivation => true;
        public void SetText(string t) { text = t; Invalidate(); }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        Timer keepTop;
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 게임이 맨 위 창을 잡아도 안내 글이 다시 위로 (1초마다, 포커스는 안 가져감)
            keepTop = new Timer { Interval = 1000 };
            keepTop.Tick += (o, a) => { try { if (Visible) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); } catch { } };
            keepTop.Start();
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { keepTop?.Dispose(); base.OnFormClosed(e); }

        // 화면을 찍을 때 안내 글이 같이 찍히지 않게 잠깐 숨기기 (다른 스레드에서도 부를 수 있게 창 핸들로)
        static readonly System.Collections.Generic.List<IntPtr> all = new System.Collections.Generic.List<IntPtr>();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); lock (all) all.Add(Handle); }
        protected override void OnHandleDestroyed(EventArgs e) { lock (all) all.Remove(Handle); base.OnHandleDestroyed(e); }
        public static void HideAll(bool hide)
        {
            lock (all) foreach (var h in all) ShowWindow(h, hide ? 0 : 4);
            if (hide) System.Threading.Thread.Sleep(120);   // 화면에서 사라질 시간
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using (var f = Theme.Px(15, true))
                TextRenderer.DrawText(e.Graphics, text, f, ClientRectangle, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>미니 모드: 런처를 숨기고 화면 가장자리에 작은 동그라미만 남김. 누르면 런처가 돌아옴</summary>
    class MiniBubble : Form
    {
        readonly ShellForm shell;
        Point down, startLoc;
        bool moved;

        public MiniBubble(ShellForm s)
        {
            shell = s;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            int d = Theme.S(64);
            Size = new Size(d, d);
            var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - d - Theme.S(12), wa.Top + wa.Height / 3);
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Opacity = 0.9;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000; return cp; } // TOOLWINDOW | NOACTIVATE
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Magenta);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(2, 2, Width - 4, Height - 4);
            using (var b = new SolidBrush(Color.FromArgb(23, 29, 37))) g.FillEllipse(b, r);
            using (var p = new Pen(Theme.Accent, Theme.S(3))) g.DrawEllipse(p, Rectangle.Inflate(r, -Theme.S(2), -Theme.S(2)));
            // 가운데 2x2 격자
            int c = Theme.S(9), gap = Theme.S(4), x0 = Width / 2 - c - gap / 2, y0 = Height / 2 - c - gap / 2;
            using (var b = new SolidBrush(Color.White))
                for (int i = 0; i < 4; i++) g.FillRectangle(b, x0 + (i % 2) * (c + gap), y0 + (i / 2) * (c + gap), c, c);
        }

        protected override void OnMouseDown(MouseEventArgs e) { down = Cursor.Position; startLoc = Location; moved = false; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var p = Cursor.Position;
            if (Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) > Theme.S(10)) moved = true;
            if (moved) Location = new Point(startLoc.X + p.X - down.X, startLoc.Y + p.Y - down.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!moved) shell.LeaveMini();
        }
    }

    /// <summary>배경음악: 내 문서\쯔꾸르 런처\bgm.mp3(또는 wav) 를 런처 화면에서만 반복 재생</summary>
    static class Bgm
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string cmd, StringBuilder ret, int len, IntPtr cb);

        static bool open, playing;

        public static string FilePath
        {
            get
            {
                foreach (var n in new[] { "bgm.mp3", "bgm.wav", "bgm.wma" })
                {
                    string p = Path.Combine(Settings.Dir, n);
                    if (File.Exists(p)) return p;
                }
                return null;
            }
        }

        public static void Update(bool shouldPlay)
        {
            try
            {
                shouldPlay &= Settings.Bgm && FilePath != null;
                if (shouldPlay && !playing)
                {
                    if (!open)
                    {
                        mciSendString("open \"" + FilePath + "\" type mpegvideo alias tklbgm", null, 0, IntPtr.Zero);
                        mciSendString("setaudio tklbgm volume to 350", null, 0, IntPtr.Zero);
                        open = true;
                    }
                    mciSendString("play tklbgm repeat", null, 0, IntPtr.Zero);
                    playing = true;
                }
                else if (!shouldPlay && playing)
                {
                    mciSendString("pause tklbgm", null, 0, IntPtr.Zero);
                    playing = false;
                }
            }
            catch { }
        }

        public static void Close()
        {
            try { mciSendString("close tklbgm", null, 0, IntPtr.Zero); } catch { }
            open = playing = false;
        }
    }

    /// <summary>
    /// 최소화 반복 감시: 창이 저절로 최소화·복원되는 일이 10초 안에 6번 넘게 생기면
    /// 직전에 들어온 입력을 기록하고, AYA 작은 버튼(Win+D) 신호를 1분 동안 막는다.
    /// </summary>
    static class LoopGuard
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);

        static IntPtr hook;
        static WinEventProc proc;
        static readonly System.Collections.Generic.Queue<int> times = new System.Collections.Generic.Queue<int>();
        static int lastAlert;
        public static Action<string> Alert;
        public static string LastReport = "";

        public static void Start()
        {
            if (hook != IntPtr.Zero) return;
            proc = OnEvent;
            hook = SetWinEventHook(0x16, 0x17, IntPtr.Zero, proc, 0, 0, 0); // MINIMIZESTART ~ MINIMIZEEND, OUTOFCONTEXT
        }

        public static void Stop()
        {
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            hook = IntPtr.Zero;
        }

        static void OnEvent(IntPtr h, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            try { OnEvent2(obj); } catch (Exception ex) { Log.Error("최소화 감시", ex); }
        }

        static void OnEvent2(int obj)
        {
            if (!Settings.LoopGuard || obj != 0) return;
            int now = Environment.TickCount;
            times.Enqueue(now);
            while (times.Count > 0 && now - times.Peek() > 10000) times.Dequeue();
            if (times.Count < 6 || now - lastAlert < 30000) return;
            lastAlert = now;
            times.Clear();

            string cause;
            int since = now - (int)Hotkeys.LastComboTime;
            if (Hotkeys.LastCombo != "" && since < 3000) cause = "키 신호 " + Hotkeys.Describe(Hotkeys.LastCombo);
            else if (Gamepad.LastPressAge() < 3000) cause = "컨트롤러 버튼";
            else cause = "직전 입력 없음 (유령 터치 또는 작업 표시줄 모서리 의심)";
            Hotkeys.SmallBlockedUntil = now + 60000;
            LastReport = DateTime.Now.ToString("HH:mm") + " · " + cause;
            Log.Info("최소화 반복 감지: " + cause);
            Alert?.Invoke("최소화 반복을 감지했어요 · 원인: " + cause + " · AYA 작은 버튼을 1분 막았어요");
        }

        // 작업 표시줄 맨 끝 모서리를 눌러 바탕화면 보기 (윈도우 11)
        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        public static bool CornerPeekOn
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(Adv)) return !(k?.GetValue("TaskbarSd") is int v && v == 0); }
                catch { return true; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(Adv, true)) k?.SetValue("TaskbarSd", value ? 1 : 0, RegistryValueKind.DWord);
                    SendNotifyMessage((IntPtr)0xFFFF, 0x1A, IntPtr.Zero, "TraySettings"); // 설정 바뀜 알리기
                }
                catch { }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SendNotifyMessage(IntPtr h, uint msg, IntPtr w, string l);
    }
}
