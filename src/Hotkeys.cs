using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// AYA 버튼처럼 키보드 신호를 보내는 기기 버튼을 '배워서' 홈 버튼으로 씀.
    /// 배우기 모드에서 버튼을 한 번 누르면 그 신호(예: Win+Ctrl+F16)를 기억.
    /// </summary>
    static class Hotkeys
    {
        delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        [StructLayout(LayoutKind.Sequential)]
        struct KBD { public int vk, scan, flags, time; public IntPtr extra; }

        static IntPtr hook;
        static HookProc proc;      // 가비지 수집 안 되게 붙잡아 둠
        public static Action Home;
        public static Action HomeLong;           // AYA 큰 버튼 길게 누름
        static int pendingVk = -1, pendingAt;
        static System.Threading.Timer longTimer;
        public static Action Small;
        public static Action<int> Button;     // 위쪽 LC(1)/RC(2) 버튼
        public static string LastCombo = "";
        public static long LastComboTime;
        public const string LcCombo = "Win+Ctrl+126", RcCombo = "Win+Ctrl+127";   // Ctrl+Win+F15 / F16
        public static Action<string> Learned;
        public static bool Learning;
        public static int SmallBlockedUntil;

        public static void Start()
        {
            if (hook != IntPtr.Zero) return;
            proc = Callback;
            using (var p = Process.GetCurrentProcess())
                hook = SetWindowsHookEx(13, proc, GetModuleHandle(p.MainModule.ModuleName), 0); // WH_KEYBOARD_LL
        }

        public static void Stop()
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        static bool IsModifier(int vk) => vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;

        public static string Describe(string combo)
        {
            if (string.IsNullOrEmpty(combo)) return "없음";
            var parts = combo.Split('+');
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (sb.Length > 0) sb.Append(" + ");
                if (int.TryParse(p, out int vk)) sb.Append(((Keys)vk).ToString());
                else sb.Append(p);
            }
            return sb.ToString();
        }

        static IntPtr Callback(int code, IntPtr w, IntPtr l)
        {
            try { return Callback2(code, w, l); }
            catch (Exception ex) { Log.Error("키 신호", ex); return CallNextHookEx(hook, code, w, l); }
        }

        static IntPtr Callback2(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && (w.ToInt32() == 0x100 || w.ToInt32() == 0x104)) // KEYDOWN / SYSKEYDOWN
            {
                var k = (KBD)Marshal.PtrToStructure(l, typeof(KBD));
                if (!IsModifier(k.vk))
                {
                    string combo = (Down(0x5B) || Down(0x5C) ? "Win+" : "") + (Down(0x11) ? "Ctrl+" : "") +
                                   (Down(0x12) ? "Alt+" : "") + (Down(0x10) ? "Shift+" : "") + k.vk;
                    LastCombo = combo;
                    LastComboTime = Environment.TickCount;
                    if (Learning)
                    {
                        Learning = false;
                        Learned?.Invoke(combo);
                        return (IntPtr)1;
                    }
                    if (!string.IsNullOrEmpty(Settings.HomeKey) && combo == Settings.HomeKey)
                    {
                        // 짧게/길게 구분: 뗄 때까지 기다림. 0.6초 넘게 누르고 있으면 '길게'
                        if (pendingVk == k.vk) return (IntPtr)1;      // 누르고 있는 동안 반복 신호는 무시
                        if (HomeLong == null) { Home?.Invoke(); return (IntPtr)1; }
                        pendingVk = k.vk; pendingAt = Environment.TickCount;
                        int mine = pendingAt;
                        longTimer?.Dispose();
                        longTimer = new System.Threading.Timer(_ =>
                        {
                            if (pendingVk >= 0 && pendingAt == mine) { pendingVk = -1; Log.Info("AYA 큰 버튼 길게"); HomeLong?.Invoke(); }
                        }, null, 600, System.Threading.Timeout.Infinite);
                        return (IntPtr)1; // 다른 프로그램엔 안 넘김
                    }
                    if (!string.IsNullOrEmpty(Settings.HomeLongKey) && combo == Settings.HomeLongKey && HomeLong != null)
                    {
                        HomeLong();   // 길게 누를 때 다른 신호를 보내는 기기용 (설정에서 배운 키)
                        return (IntPtr)1;
                    }
                    if (!string.IsNullOrEmpty(Settings.SmallKey) && combo == Settings.SmallKey && Small != null)
                    {
                        if (Environment.TickCount - SmallBlockedUntil < 0) return (IntPtr)1; // 최소화 반복 감시가 막아둔 동안은 무시
                        Small();
                        return (IntPtr)1;
                    }
                    if (Button != null && (combo == LcCombo || combo == RcCombo))
                    {
                        Button(combo == LcCombo ? 1 : 2);
                        return (IntPtr)1;
                    }
                }
            }
            if (code >= 0 && (w.ToInt32() == 0x101 || w.ToInt32() == 0x105) && pendingVk >= 0)   // KEYUP
            {
                var k = (KBD)Marshal.PtrToStructure(l, typeof(KBD));
                if (k.vk == pendingVk)
                {
                    pendingVk = -1;
                    longTimer?.Dispose(); longTimer = null;
                    Home?.Invoke();
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(hook, code, w, l);
        }
    }
}
