using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace TkoolLauncher
{
    /// <summary>윈도우 터치 키보드 띄우기·내리기, 키보드 위쪽 끝(가려지지 않게 화면을 그 위에 그림)</summary>
    static class TouchKeyboard
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITipInvocation { void Toggle(IntPtr hwnd); }

        static IntPtr Win10Win() => FindWindow("IPTip_Main_Window", null);

        /// <summary>윈도우 10식 키보드 창이 떠 있으면 그 위 끝(화면 좌표), 모르면 -1</summary>
        static int DetectTop()
        {
            try
            {
                var h = Win10Win();
                if (h != IntPtr.Zero && IsWindowVisible(h) && (GetWindowLong(h, -16) & 0x08000000) == 0)
                {
                    RECT r; if (GetWindowRect(h, out r) && r.B - r.T > 50) return r.T;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>키보드가 차지할 수 있는 화면 위 끝 — 찾으면 그 값, 못 찾으면 화면 높이의 55% 지점(그 아래는 키보드 자리로 비워 둠)</summary>
        public static int SafeBottom(Rectangle screen)
        {
            int t = DetectTop();
            int guess = screen.Top + (int)(screen.Height * 0.55f);
            if (t > screen.Top + screen.Height / 4 && t < screen.Bottom) return Math.Min(t, screen.Bottom);
            return guess;
        }

        public static void Show()
        {
            try
            {
                string tip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), @"microsoft shared\ink\TabTip.exe");
                bool running = Process.GetProcessesByName("TabTip").Length > 0;
                if (!running) { if (File.Exists(tip)) Process.Start(new ProcessStartInfo(tip) { UseShellExecute = true }); return; }
                if (DetectTop() >= 0) return;   // 이미 떠 있음
                // 이미 켜진 키보드 프로그램은 다시 실행해도 안 뜨는 경우가 있어 COM으로 띄움
                var t = Type.GetTypeFromCLSID(new Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376"));
                var inv = (ITipInvocation)Activator.CreateInstance(t);
                inv.Toggle(IntPtr.Zero);
                Marshal.ReleaseComObject(inv);
            }
            catch (Exception ex) { Log.Info("터치 키보드 띄우기 실패: " + ex.Message); }
        }

        public static void Hide()
        {
            try
            {
                var h = Win10Win();
                if (h != IntPtr.Zero && IsWindowVisible(h)) PostMessage(h, 0x0112, (IntPtr)0xF060, IntPtr.Zero);   // WM_SYSCOMMAND SC_CLOSE
            }
            catch { }
        }
    }
}
