using System;
using System.Runtime.InteropServices;

namespace TkoolLauncher
{
    /// <summary>게임 중 작업 표시줄(시작 줄) 숨기기 — 끝나면·런처 종료·다음 시작 때 꼭 다시 보이게</summary>
    static class TaskbarHide
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        public static bool Hidden;

        static void Apply(int cmd)
        {
            try
            {
                ShowWindow(FindWindow("Shell_TrayWnd", null), cmd);
                IntPtr h = IntPtr.Zero;
                while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) ShowWindow(h, cmd);
                IntPtr start = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Button", "Start");   // 윈도우 10 시작 단추
                if (start != IntPtr.Zero) ShowWindow(start, cmd);
            }
            catch { }
        }
        public static void Hide() { Apply(0); Hidden = true; }
        public static void Show() { Apply(5); Hidden = false; }
    }
}
