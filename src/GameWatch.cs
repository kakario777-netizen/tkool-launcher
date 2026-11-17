using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TkoolLauncher
{
    /// <summary>
    /// 실행한 게임이 아직 돌고 있는지 지켜봄.
    /// 스팀·에픽처럼 주소로 켜거나, 실행 파일이 다른 프로그램을 띄우고 꺼지는 게임도 있어서
    /// '게임 폴더 안에 있는 프로그램이 하나라도 돌고 있으면 실행 중'으로 판단한다.
    /// </summary>
    static class GameWatch
    {
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        public static volatile string Dir;      // 지금 지켜보는 게임 폴더
        public static volatile bool Running;

        public static string PathOf(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
            }
            finally { CloseHandle(h); }
        }

        public static bool InDir(int pid, string dir)
        {
            if (dir == null) return false;
            string p = PathOf(pid);
            return p != null && p.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static bool AnyInDir(string dir)
        {
            foreach (var p in Process.GetProcesses())
            {
                try { if (p.Id > 4 && InDir(p.Id, dir)) return true; }
                catch { }
                finally { p.Dispose(); }
            }
            return false;
        }

        /// <summary>게임 하나를 지켜보다가 끝나면 onExit (다른 스레드에서 부름)</summary>
        public static void Start(Process first, string dir, Action onExit)
        {
            Dir = dir;
            Running = true;
            var th = new Thread(() =>
            {
                try
                {
                    if (first != null) { try { first.WaitForExit(); } catch { } }
                    // 스토어 실행이면 게임이 뜰 때까지 최대 2분 기다림
                    bool seen = first != null;
                    for (int i = 0; i < 60 && !seen; i++)
                    {
                        if (AnyInDir(dir)) seen = true;
                        else Thread.Sleep(2000);
                    }
                    // 폴더 안 프로그램이 모두 꺼질 때까지
                    int quiet = 0;
                    while (quiet < 2)
                    {
                        Thread.Sleep(3000);
                        if (dir != null && AnyInDir(dir)) quiet = 0; else quiet++;
                    }
                }
                catch { }
                Running = false;
                Dir = null;
                onExit();
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            th.Start();
        }
    }
}
