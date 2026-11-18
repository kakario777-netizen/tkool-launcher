using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TkoolLauncher
{
    /// <summary>
    /// "꽉 찬 창": 게임 창의 테두리·제목줄을 없애고 작업 표시줄을 뺀 화면 전체(작업 영역)에 맞춤.
    /// 흔히 '테두리 없는 창(보더리스) 최대화'라 부름. 작업 표시줄은 그대로 보임.
    /// </summary>
    static class WinFit
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        const int GWL_STYLE = -16;
        const int WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;

        /// <summary>게임 폴더의 큰 창이 뜨면 꽉 찬 창으로 (20초까지 기다림, 3초 뒤 한 번 더)</summary>
        public static void Apply(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            var th = new Thread(() =>
            {
                try
                {
                    IntPtr w = IntPtr.Zero;
                    for (int i = 0; i < 40 && w == IntPtr.Zero; i++) { Thread.Sleep(500); w = FindMain(dir); }
                    if (w == IntPtr.Zero) { Log.Info("꽉 찬 창: 게임 창을 못 찾음"); return; }
                    Fit(w);
                    Thread.Sleep(3000);   // 게임이 처음에 창 크기를 다시 정하는 경우가 있어 한 번 더
                    Fit(w);
                    Log.Info("꽉 찬 창 적용");
                }
                catch (Exception ex) { Log.Error("꽉 찬 창", ex); }
            }) { IsBackground = true };
            th.Start();
        }

        static IntPtr FindMain(string dir)
        {
            IntPtr best = IntPtr.Zero; int bestArea = 0;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;   // 소유 창(대화상자) 제외
                GetWindowThreadProcessId(h, out uint pid);
                if (!GameWatch.InDir((int)pid, dir)) return true;
                if (!GetWindowRect(h, out RECT r)) return true;
                int area = (r.R - r.L) * (r.B - r.T);
                if (area > bestArea && r.R - r.L > 200) { bestArea = area; best = h; }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        static void Fit(IntPtr w)
        {
            int st = GetWindowLong(w, GWL_STYLE);
            st &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            SetWindowLong(w, GWL_STYLE, st);
            var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;   // 작업 표시줄 뺀 영역
            SetWindowPos(w, IntPtr.Zero, wa.X, wa.Y, wa.Width, wa.Height, 0x0020 | 0x0040 | 0x0004);   // FRAMECHANGED | SHOWWINDOW | NOZORDER
        }
    }
}
