using System;
using System.Threading;

namespace TkoolLauncher
{
    /// <summary>
    /// 성능 모드 (AYASpace처럼): 저소음 / 균형 / 고성능 / 직접 설정.
    /// 모드마다 TDP(전력 한계) + 팬 곡선(온도에 따라 팬 %) + 윈도우 전원 모드를 함께 맞춤.
    /// '직접 설정'은 사용자가 고른 TDP·팬 값을 그대로 씀.
    /// </summary>
    static class PerfMode
    {
        public static readonly string[] Names = { "저소음", "균형", "고성능", "직접 설정" };
        public const int Custom = 3;

        // 모드별 TDP (W)
        static readonly int[] Tdp = { 7, 11, 15 };
        // 모드별 팬 곡선: {온도, 팬%} — 사이 값은 이어서 계산
        static readonly int[][][] Curves =
        {
            new[] { new[] { 50, 20 }, new[] { 65, 30 }, new[] { 75, 45 }, new[] { 85, 70 }, new[] { 92, 100 } },   // 저소음
            new[] { new[] { 45, 25 }, new[] { 60, 40 }, new[] { 70, 55 }, new[] { 80, 80 }, new[] { 88, 100 } },   // 균형
            new[] { new[] { 40, 35 }, new[] { 55, 55 }, new[] { 65, 75 }, new[] { 75, 100 } },                     // 고성능
        };
        static readonly int[] PowerOverlay = { 0, 1, 2 };   // SystemControls.PowerNames: 배터리 절약·균형·최고 성능

        public static int FanNow = -1;      // 지금 팬 % (곡선이 정한 값, -1 = 기기 자동)
        public static float TempNow;        // 마지막으로 읽은 CPU 온도
        static Timer timer;

        public static int Mode => Settings.PerfMode < 0 || Settings.PerfMode > Custom ? Custom : Settings.PerfMode;

        /// <summary>모드 설명 (빠른 설정에 보여 줄 글)</summary>
        public static string Describe(int m)
        {
            if (m >= Custom) return "아래 TDP·팬 속도를 직접 고른 값으로";
            string fan = m == 0 ? "팬 조용히" : m == 1 ? "팬 보통" : "팬 세게";
            string win = m == 0 ? "윈도우 절약" : m == 1 ? "윈도우 균형" : "윈도우 최고 성능";
            return "TDP " + Tdp[m] + "W · " + fan + " · " + win;
        }

        /// <summary>지금 실제 값: TDP·팬·온도·소모 전력</summary>
        public static string Live()
        {
            string tdp;
            int t = Settings.SmartFps > 0 && SmartTdp.Current > 0 ? SmartTdp.Current : (Mode < Custom ? Tdp[Mode] : Settings.Tdp);
            tdp = t > 0 ? t + "W" : "기본";
            string fan = Mode < Custom ? (FanNow >= 0 ? FanNow + "%" : "-") : (Settings.Fan >= 0 ? Settings.Fan + "%" : "자동");
            string temp = TempNow > 0 ? (int)TempNow + "°C" : "-";
            double w = 0;
            try { w = SystemControls.DischargeWatts(); } catch { }
            return "TDP " + tdp + " · 팬 " + fan + " · " + temp + (w > 0 ? " · 소모 " + w.ToString("0.0") + "W" : " · 충전 중");
        }

        public static void Set(int m)
        {
            Settings.PerfMode = m;
            Settings.Save();
            Log.Info("성능 모드: " + Names[Math.Min(Custom, Math.Max(0, m))]);
            Apply();
        }

        /// <summary>켤 때·모드 바꿀 때: TDP·전원 모드 적용, 팬 곡선 시작/정지</summary>
        public static void Apply()
        {
            int m = Mode;
            if (m < Custom)
            {
                try { SystemControls.SetPowerMode(PowerOverlay[m]); } catch { }
                if (Hardware.SmuReady && Settings.SmartFps == 0) { int w = Tdp[m]; ThreadPool.QueueUserWorkItem(_ => Hardware.SetTdp(w)); }
                StartCurve();
            }
            else
            {
                StopCurve();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (Hardware.SmuReady && Settings.Tdp > 0) Hardware.SetTdp(Settings.Tdp);
                        if (Hardware.EcReady) Hardware.SetFan(Settings.Fan);
                    }
                    catch { }
                });
            }
        }

        /// <summary>스마트 TDP가 꺼질 때 모드의 TDP로 돌아가기</summary>
        public static int BaseTdp => Mode < Custom ? Tdp[Mode] : (Settings.Tdp > 0 ? Settings.Tdp : 12);

        static void StartCurve()
        {
            if (timer == null) timer = new Timer(_ => Step(), null, 0, 2000);
        }

        static void StopCurve()
        {
            FanNow = -1;
            StartCurve();   // 온도 읽기는 계속 (지금 상태 표시용)
        }

        static int Target(int m, float temp)
        {
            var c = Curves[m];
            if (temp <= c[0][0]) return c[0][1];
            for (int i = 1; i < c.Length; i++)
                if (temp <= c[i][0])
                {
                    float k = (temp - c[i - 1][0]) / (float)(c[i][0] - c[i - 1][0]);
                    return (int)Math.Round(c[i - 1][1] + k * (c[i][1] - c[i - 1][1]));
                }
            return c[c.Length - 1][1];
        }

        static void Step()
        {
            try
            {
                int m = Mode;
                float temp = Hardware.TempReady ? Hardware.CpuTemp() : 0;
                if (temp > 0) TempNow = temp;
                if (m >= Custom || !Hardware.EcReady || temp <= 0) return;
                int want = Target(m, temp);
                // 올릴 땐 바로, 내릴 땐 천천히 (2초에 5%씩) — 팬 소리가 들쭉날쭉하지 않게
                int next = FanNow < 0 ? want : want >= FanNow ? want : Math.Max(want, FanNow - 5);
                if (FanNow < 0 || Math.Abs(next - FanNow) >= 3 || next == 100)
                {
                    Hardware.SetFan(next);
                    FanNow = next;
                }
            }
            catch { }
        }
    }
}
