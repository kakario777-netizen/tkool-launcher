using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임 위에 떠 있는 작은 성능 표시줄 (CPU·GPU·메모리·전력·배터리·시간).
    /// 클릭이 그대로 게임으로 통과하고, 켜 둔 동안만 1초마다 갱신.
    /// </summary>
    class OverlayForm : Form
    {
        static OverlayForm inst;
        readonly Timer timer = new Timer { Interval = 1000 };
        PerformanceCounter cpu;
        string text = "측정 중...";
        bool busy;

        public static bool IsOn => inst != null;

        public static void Toggle(bool on)
        {
            if (on && inst == null) { inst = new OverlayForm(); inst.Show(); }
            else if (!on && inst != null) { inst.Close(); inst = null; }
        }

        OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Opacity = 0.78;
            Location = new Point(Theme.S(8), Theme.S(8));
            Size = new Size(Theme.S(560), Theme.S(30));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            try { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); } catch { }
            timer.Tick += (s, e) => Refresh2();
            timer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000; // LAYERED | TRANSPARENT(클릭 통과) | TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        void Refresh2()
        {
            if (busy) return;
            busy = true;
            Task.Run(() =>
            {
                string t;
                try
                {
                    float c = cpu != null ? cpu.NextValue() : 0;
                    float gpu = Gpu3D();
                    var m = SystemControls.Memory();
                    double used = (m.Total - m.Avail) / 1073741824.0, total = m.Total / 1073741824.0;
                    double w = SystemControls.DischargeWatts();
                    string bat = "";
                    try { bat = "  배터리 " + (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100) + "%"; } catch { }
                    float temp = Hardware.TempReady ? Hardware.CpuTemp() : 0;
                    float fps = Fps.Value;
                    string fpsText = fps > 0 ? "FPS " + (int)Math.Round(fps) + "   " : (Fps.Problem != "" && GameWatch.Running ? "FPS -   " : "");
                    int tdpNow = Settings.SmartFps > 0 && GameWatch.Running ? SmartTdp.Current : Settings.Tdp;
                    t = fpsText + "CPU " + (int)c + "%" + (temp > 0 ? " " + (int)temp + "°C" : "") + (tdpNow > 0 ? " " + tdpNow + "W" : "") + "   GPU " + (gpu < 0 ? "-" : ((int)gpu).ToString()) + "%   RAM " + used.ToString("0.0") + "/" + total.ToString("0.0") + "GB" +
                        (w > 0 ? "   " + w.ToString("0.0") + "W" : "") + bat + "   " + DateTime.Now.ToString("HH:mm");
                }
                catch { t = "측정 실패"; }
                try { BeginInvoke(new Action(() => { text = t; busy = false; Invalidate(); })); } catch { busy = false; }
            });
        }

        // GPU 3D 엔진 사용률 합 (윈도우 10 1709 이상)
        static float Gpu3D()
        {
            try
            {
                var cat = new PerformanceCounterCategory("GPU Engine");
                var names = cat.GetInstanceNames().Where(n => n.EndsWith("engtype_3D")).ToArray();
                float sum = 0;
                var counters = names.Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n)).ToList();
                foreach (var c in counters) c.NextValue();
                System.Threading.Thread.Sleep(200);
                foreach (var c in counters) { sum += c.NextValue(); c.Dispose(); }
                return Math.Min(100, sum);
            }
            catch { return -1; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Black);
            using (var f = Theme.Px(14, true))
            {
                var sz = TextRenderer.MeasureText(text, f);
                if (Math.Abs(sz.Width + Theme.S(20) - Width) > 4) Width = sz.Width + Theme.S(20);
                TextRenderer.DrawText(e.Graphics, text, f, ClientRectangle, Color.FromArgb(150, 255, 120),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop(); timer.Dispose(); cpu?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
