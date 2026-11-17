using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TkoolLauncher
{
    /// <summary>키 조합 보내기 (빠른 버튼: ESC, 전체 화면, 스크린샷, 작업 전환 등)</summary>
    static class KeySender
    {
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public int type; public U u; }
        [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MI mi; [FieldOffset(0)] public KI ki; }
        [StructLayout(LayoutKind.Sequential)] struct MI { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int size);

        static INPUT K(ushort vk, bool up)
        {
            uint f = up ? 2u : 0u;
            if (vk == 0x2C || (vk >= 0x21 && vk <= 0x28) || vk == 0x5B) f |= 1; // 확장 키
            return new INPUT { type = 1, u = new U { ki = new KI { vk = vk, flags = f } } };
        }

        public static void Combo(params ushort[] vks)
        {
            var list = new INPUT[vks.Length * 2];
            for (int i = 0; i < vks.Length; i++) list[i] = K(vks[i], false);
            for (int i = 0; i < vks.Length; i++) list[vks.Length + i] = K(vks[vks.Length - 1 - i], true);
            SendInput((uint)list.Length, list, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void MouseMove(int dx, int dy)
        {
            var i = new INPUT { type = 0, u = new U { mi = new MI { dx = dx, dy = dy, flags = 0x0001 } } };
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
        }

        public const ushort Esc = 0x1B, Enter = 0x0D, Alt = 0x12, Win = 0x5B, Tab = 0x09, PrtSc = 0x2C, Ctrl = 0x11, Shift = 0x10;
    }
}
