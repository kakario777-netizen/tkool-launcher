using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 화면 확대 (AYASpace FSR 같은 용도)
    /// 게임을 작은 창(예: 1280x720, 쯔꾸르 640x480)으로 돌리고, 윈도우 전체 화면 돋보기 기능으로 그 창을 화면에 꽉 차게 키움.
    /// - 부드럽게: 3D 게임용 (낮은 해상도로 돌려 FPS↑)
    /// - 도트 선명: 쯔꾸르용 (정수배·픽셀 그대로)
    /// 확대는 그래픽 카드(DWM)가 하므로 런처는 거의 일을 안 함.
    /// </summary>
    static class Scaler
    {
        [DllImport("Magnification.dll")] static extern bool MagInitialize();
        [DllImport("Magnification.dll")] static extern bool MagUninitialize();
        [DllImport("Magnification.dll")] static extern bool MagSetFullscreenTransform(float scale, int x, int y);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate bool BoolFn(bool b);

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool ShowCursor(bool show);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        public static readonly string[] ModeNames = { "끔", "부드럽게 (3D 게임)", "도트 선명 (쯔꾸르)" };
        public static int Mode;            // 지금 켜진 방식
        public static string Problem = "";
        static IntPtr game;
        static bool inited;
        static Form backdrop;
        static Timer follow;

        /// <summary>앞에 있는 게임 창을 화면에 꽉 차게</summary>
        public static bool Start(int mode, IntPtr gameWindow)
        {
            Stop();
            if (mode == 0) return true;
            game = gameWindow;
            if (game == IntPtr.Zero || !IsWindow(game)) { Problem = "확대할 게임 창을 못 찾았어요"; return false; }
            try
            {
                if (!MagInitialize()) { Problem = "윈도우 확대 기능을 못 켰어요"; return false; }
                inited = true;
                // 부드럽게/선명하게 (윈도우 10 1809 이상에 있는 함수, 없으면 기본값)
                IntPtr fn = GetProcAddress(GetModuleHandle("Magnification.dll"), "MagSetFullscreenUseBitmapSmoothing");
                if (fn != IntPtr.Zero)
                    ((BoolFn)Marshal.GetDelegateForFunctionPointer(fn, typeof(BoolFn)))(mode == 1);
            }
            catch (Exception ex) { Problem = "확대 기능 오류: " + ex.Message; return false; }

            // 게임 창 뒤에 검은 판을 깔아 남는 테두리를 가림
            backdrop = new Form
            {
                FormBorderStyle = FormBorderStyle.None, BackColor = Color.Black, ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds
            };
            backdrop.Show();
            SetWindowPos(backdrop.Handle, game, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // 게임 바로 뒤, 크기·위치 그대로, 활성화 안 함

            Mode = mode;
            Problem = "";
            Apply();
            // 게임 창이 움직이거나 크기가 바뀌면 따라감, 게임이 꺼지면 해제
            follow = new Timer { Interval = 500 };
            follow.Tick += (s, e) => { if (!IsWindow(game) || !GameWatch.Running) Stop(); else Apply(); };
            follow.Start();
            return true;
        }

        static void Apply()
        {
            if (!GetClientRect(game, out RECT rc)) return;
            int w = rc.R - rc.L, h = rc.B - rc.T;
            if (w < 64 || h < 64) return;
            var p = new POINT();
            ClientToScreen(game, ref p);
            var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            float scale = Math.Min((float)sc.Width / w, (float)sc.Height / h);
            if (Mode == 2) scale = Math.Max(1, (float)Math.Floor(scale)); // 도트는 정수배
            if (scale <= 1.01f) { MagSetFullscreenTransform(1, 0, 0); return; }
            // 확대 후 게임 창 가운데가 화면 가운데에 오도록 보여줄 시작점 계산
            float cx = p.X + w / 2f, cy = p.Y + h / 2f;
            int ox = (int)Math.Round(cx - sc.Width / (2 * scale));
            int oy = (int)Math.Round(cy - sc.Height / (2 * scale));
            MagSetFullscreenTransform(scale, ox, oy);
        }

        public static void Stop()
        {
            follow?.Stop(); follow?.Dispose(); follow = null;
            if (inited)
            {
                try { MagSetFullscreenTransform(1, 0, 0); MagUninitialize(); } catch { }
                inited = false;
            }
            try { backdrop?.Close(); } catch { }
            backdrop = null;
            Mode = 0;
        }
    }
}
