using System;
using System.Runtime.InteropServices;

namespace TkoolLauncher
{
    /// <summary>미니 치트 팝업 모드(창 제목 MiniCheatPanel)에 명령 보내기 — show/hide/pad/quit (WM_COPYDATA)</summary>
    static class CheatLink
    {
        const string Title = "MiniCheatPanel";
        [StructLayout(LayoutKind.Sequential)] struct COPYDATA { public IntPtr Data; public int Size; public IntPtr Ptr; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref COPYDATA l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);

        public static bool Visible()
        {
            IntPtr h = FindWindow(null, Title);
            return h != IntPtr.Zero && IsWindowVisible(h);
        }

        public static bool Send(string cmd)
        {
            IntPtr h = FindWindow(null, Title);
            if (h == IntPtr.Zero) return false;
            IntPtr mem = Marshal.StringToHGlobalUni(cmd);
            try
            {
                var cd = new COPYDATA { Data = IntPtr.Zero, Size = (cmd.Length + 1) * 2, Ptr = mem };
                SendMessage(h, 0x4A, IntPtr.Zero, ref cd);
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(mem); }
            return true;
        }
    }
}
