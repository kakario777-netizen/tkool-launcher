using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임 위 팝업을 여는 동안 게임 프로그램을 잠깐 멈춤(일시 정지) — 패드를 직접 읽는 게임(유니티·MV/MZ 등)이
    /// 팝업 조작에 같이 움직이던 것. (HidHide로 숨겨도 이미 켜진 게임은 패드를 계속 읽어서 안 막혔음)
    /// 닫으면 다시 움직임. 게임 창을 건드리는 일(화면 크기·전체 화면) 전에는 먼저 풀어 줌.
    /// </summary>
    static class GameFreeze
    {
        [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr h);

        static readonly List<Process> frozen = new List<Process>();
        public static bool Frozen { get { lock (frozen) return frozen.Count > 0; } }

        public static void Freeze()
        {
            if (!Settings.PopupFreeze) return;
            string dir = GameWatch.Dir;
            if (string.IsNullOrEmpty(dir) || !GameWatch.Running) return;
            lock (frozen)
            {
                if (frozen.Count > 0) return;
                int me = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == me || !GameWatch.InDir(p.Id, dir)) { p.Dispose(); continue; }
                        if (NtSuspendProcess(p.Handle) == 0) frozen.Add(p); else p.Dispose();
                    }
                    catch { try { p.Dispose(); } catch { } }
                }
                if (frozen.Count > 0) Log.Info("팝업 여는 동안 게임 멈춤: 프로그램 " + frozen.Count + "개");
            }
        }

        public static void Resume()
        {
            lock (frozen)
            {
                foreach (var p in frozen)
                {
                    try { NtResumeProcess(p.Handle); } catch { }
                    try { p.Dispose(); } catch { }
                }
                frozen.Clear();
            }
        }
    }
}
