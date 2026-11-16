using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace TkoolLauncher
{
    /// <summary>
    /// FPS 재기: 인텔 PresentMon(MIT, res/PresentMon.exe.bin 내장)을 뒤에서 돌려 앞에 있는 게임 창의 프레임 간격을 읽는다.
    /// 관리자 권한이 있어야 동작. 성능 표시나 스마트 TDP가 켜져 있고 게임이 돌 때만 켬.
    /// </summary>
    static class Fps
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        static Process pm;
        static Thread reader;
        static readonly object lk = new object();
        static readonly Dictionary<uint, List<double>> frames = new Dictionary<uint, List<double>>();
        public static volatile float Value = -1;   // -1 = 모름
        public static string Problem = "";

        public static bool Running => pm != null && !pm.HasExited;

        static string ExtractTool()
        {
            string dir = Path.Combine(Settings.Dir, "tools");
            string exe = Path.Combine(dir, "PresentMon.exe");
            try
            {
                Directory.CreateDirectory(dir);
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TkoolLauncher.res.PresentMon.exe.bin"))
                {
                    if (s == null) return null;
                    if (!File.Exists(exe) || new FileInfo(exe).Length != s.Length)
                        using (var f = File.Create(exe)) s.CopyTo(f);
                }
                return exe;
            }
            catch { return File.Exists(exe) ? exe : null; }
        }

        public static void Start()
        {
            if (Running) return;
            if (!Hardware.IsAdmin) { Problem = "FPS는 관리자 권한이 필요해요 (고급 하드웨어 제어 켜기)"; Value = -1; return; }
            string exe = ExtractTool();
            if (exe == null) { Problem = "FPS 도구를 못 풀었어요"; return; }
            try
            {
                pm = Process.Start(new ProcessStartInfo(exe,
                    "--output_stdout --v1_metrics --no_track_display --no_console_stats --stop_existing_session --session_name TkoolLauncherFps")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                });
                Problem = "";
                reader = new Thread(Read) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                reader.Start();
            }
            catch (Exception ex) { Problem = "FPS 측정 실패: " + ex.Message; pm = null; }
        }

        public static void Stop()
        {
            try { if (Running) pm.Kill(); } catch { }
            pm = null;
            Value = -1;
        }

        static void Read()
        {
            var p = pm;
            int pidCol = -1, msCol = -1;
            try
            {
                string line;
                while (p != null && (line = p.StandardOutput.ReadLine()) != null)
                {
                    var c = line.Split(',');
                    if (pidCol < 0)
                    {
                        pidCol = Array.IndexOf(c, "ProcessID");
                        msCol = Array.IndexOf(c, "MsBetweenPresents");
                        if (msCol < 0) msCol = Array.IndexOf(c, "FrameTime");
                        if (pidCol < 0 || msCol < 0) { pidCol = -1; continue; }
                        continue;
                    }
                    if (c.Length <= Math.Max(pidCol, msCol)) continue;
                    if (!uint.TryParse(c[pidCol], out uint pid)) continue;
                    if (!double.TryParse(c[msCol], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ms)) continue;
                    lock (lk)
                    {
                        if (!frames.TryGetValue(pid, out var list)) frames[pid] = list = new List<double>();
                        list.Add(ms);
                    }
                }
            }
            catch { }
        }

        /// <summary>1초마다 불러서 앞에 있는 창의 FPS를 계산</summary>
        public static void Tick()
        {
            if (!Running) { Value = -1; return; }
            uint fg;
            GetWindowThreadProcessId(GetForegroundWindow(), out fg);
            lock (lk)
            {
                if (frames.TryGetValue(fg, out var list) && list.Count > 0)
                {
                    double sum = 0;
                    foreach (var m in list) sum += m;
                    Value = sum > 0 ? (float)(list.Count * 1000.0 / sum) : -1;
                }
                else Value = 0;
                frames.Clear();
            }
        }
    }

    /// <summary>
    /// 스마트 TDP: 목표 FPS를 지키는 선에서 TDP를 최대한 낮춰 배터리를 아낌.
    /// 3초마다 FPS를 보고 1W씩 올리거나 내림.
    /// </summary>
    static class SmartTdp
    {
        static int current = 12;
        static int calm;

        public static void Tick()
        {
            if (Settings.SmartFps <= 0 || !Hardware.SmuReady || !GameWatch.Running) return;
            float f = Fps.Value;
            if (f <= 0) return;
            int target = Settings.SmartFps;
            if (f < target - 2 && current < Hardware.TdpMax) { current++; Hardware.SetTdp(current); calm = 0; }
            else if (f > target + 4 && current > Hardware.TdpMin)
            {
                // 내릴 때는 천천히 (두 번 연속 여유가 있을 때만)
                if (++calm >= 2) { current--; Hardware.SetTdp(current); calm = 0; }
            }
            else calm = 0;
        }

        public static int Current => current;
        public static void Reset() { current = Settings.Tdp > 0 ? Settings.Tdp : 12; calm = 0; }
    }
}
