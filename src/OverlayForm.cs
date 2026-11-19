using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임 위에 떠 있는 성능 표시 (FPS·CPU·온도·TDP·GPU·RAM·전력·전압·배터리·시간).
    /// 클릭이 그대로 게임으로 통과. 픽셀 단위 투명(UpdateLayeredWindow)이라 배경만 흐리고 글자는 또렷하게.
    /// 모양·위치·크기·색·배경/글자 진하기·보일 항목은 Settings.Ov* 로 조절.
    /// </summary>
    class OverlayForm : Form
    {
        static OverlayForm inst;
        readonly Timer timer = new Timer { Interval = 1000 };
        PerformanceCounter cpu;
        bool busy;

        // 항목 (Settings.OvItems 비트 순서)
        public static readonly string[] ItemNames = { "FPS", "CPU 사용률", "CPU 온도", "TDP", "GPU", "메모리", "소모 전력", "전압", "배터리", "시간", "충전 전력" };
        public static readonly string[] StyleNames = { "가로 한 줄", "세로 목록", "FPS만 크게" };
        public static readonly string[] PosNames = { "왼쪽 위", "가운데 위", "오른쪽 위", "왼쪽 아래", "오른쪽 아래", "왼쪽 가운데", "오른쪽 가운데", "가운데 아래", "직접 옮긴 곳" };
        public const int PosCustom = 8;
        public static readonly string[] MarginNames = { "바짝 붙이기", "조금 띄우기", "넉넉히 띄우기" };
        public static readonly string[] SizeNames = { "작게", "보통", "크게" };
        public static readonly string[] ColorNames = { "초록", "흰색", "노랑", "하늘", "분홍", "주황", "빨강" };
        static readonly Color[] Colors =
        {
            Color.FromArgb(150, 255, 120), Color.White, Color.FromArgb(255, 230, 90), Color.FromArgb(110, 210, 255),
            Color.FromArgb(255, 140, 210), Color.FromArgb(255, 170, 70), Color.FromArgb(255, 95, 95)
        };

        // 마지막 측정값: (라벨, 값) 목록 — 항목 번호와 같은 순서, 값이 없으면 null
        string[] vals = new string[11];
        bool measured;

        public static bool IsOn => inst != null;

        // ── 끌어서 옮기기 모드 ──
        // 평소엔 클릭이 게임으로 통과(WS_EX_TRANSPARENT). 옮기기 모드에서만 마우스·터치로 잡을 수 있음.
        bool editing, dragging, moved;
        Point dragPos, grabOff, downAt;
        readonly Timer editTimer = new Timer { Interval = 15000 };
        public static bool Editing => inst != null && inst.editing;

        /// <summary>옮기기 모드 켜기 (꺼져 있으면 켜고 시작). 놓은 뒤 한 번 톡 누르거나 15초 지나면 끝.</summary>
        public static void StartMove()
        {
            if (inst == null) Toggle(true);
            inst.SetEditing(true);
        }

        public static void StopMove() { inst?.SetEditing(false); }

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        void SetEditing(bool on)
        {
            editing = on;
            int ex = GetWindowLong(Handle, -20);
            ex = on ? ex & ~0x20 : ex | 0x20;
            SetWindowLong(Handle, -20, ex);
            editTimer.Stop();
            if (on) editTimer.Start();
            Render();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!editing) return;
            editTimer.Stop();
            dragging = true; moved = false;
            downAt = Cursor.Position;
            grabOff = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
            dragPos = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            var c = Cursor.Position;
            if (Math.Abs(c.X - downAt.X) + Math.Abs(c.Y - downAt.Y) > Theme.S(6)) moved = true;
            dragPos = new Point(c.X - grabOff.X, c.Y - grabOff.Y);
            Render();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            Capture = false;
            if (moved)
            {
                var wa = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                Settings.OvPos = PosCustom;
                Settings.OvX = (float)(Left - wa.X) / wa.Width;
                Settings.OvY = (float)(Top - wa.Y) / wa.Height;
                Settings.Save();
                editTimer.Start();
                Render();
            }
            else SetEditing(false);   // 움직이지 않고 톡 누르면 고정
        }

        public static void Toggle(bool on, bool remember = true)
        {
            if (remember && Settings.OvOn != on) { Settings.OvOn = on; Settings.Save(); }   // 다음에 켤 때도 그대로
            if (on && inst == null) { inst = new OverlayForm(); inst.Show(); inst.Render(); }
            else if (!on && inst != null) { inst.Close(); inst = null; }
        }

        /// <summary>설정이 바뀌었을 때 바로 다시 그림</summary>
        public static void ApplyStyle() { try { inst?.Render(); } catch { } }

        OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Theme.S(8), Theme.S(8));
            Size = new Size(Theme.S(100), Theme.S(30));
            try { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); } catch { }
            timer.Tick += (s, e) => Measure();
            editTimer.Tick += (s, e) => SetEditing(false);
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
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        static bool Want(int i) => (Settings.OvItems & (1 << i)) != 0;

        void Measure()
        {
            if (busy) return;
            busy = true;
            Task.Run(() =>
            {
                var v = new string[11];
                try
                {
                    float fps = Fps.Value;
                    if (fps > 0) v[0] = ((int)Math.Round(fps)).ToString();
                    else if (Fps.Problem != "" && GameWatch.Running) v[0] = "-";
                    if (Want(1)) v[1] = (cpu != null ? (int)cpu.NextValue() : 0) + "%";
                    if (Want(2)) { float t = Hardware.TempReady ? Hardware.CpuTemp() : 0; if (t > 0) v[2] = (int)t + "°C"; }
                    if (Want(3)) { int tdp = Settings.SmartFps > 0 && GameWatch.Running ? SmartTdp.Current : PerfMode.Mode != PerfMode.Custom ? PerfMode.BaseTdp : Settings.Tdp; if (tdp > 0) v[3] = tdp + "W"; }
                    if (Want(4)) { float g = Gpu3D(); v[4] = g < 0 ? "-" : (int)g + "%"; }
                    if (Want(5))
                    {
                        var m = SystemControls.Memory();
                        v[5] = ((m.Total - m.Avail) / 1073741824.0).ToString("0.0") + "/" + (m.Total / 1073741824.0).ToString("0.0") + "GB";
                    }
                    if (Want(6)) { double w = SystemControls.DischargeWatts(); if (w > 0) v[6] = w.ToString("0.0") + "W"; }
                    if (Want(7)) { double volt = SystemControls.BatteryVolts(); if (volt > 0) v[7] = volt.ToString("0.00") + "V"; }
                    if (Want(8)) try { v[8] = (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100) + "%"; } catch { }
                    if (Want(9)) v[9] = DateTime.Now.ToString("HH:mm");
                    if (Want(10)) { double cw = SystemControls.ChargeWatts(); if (cw > 0) v[10] = "+" + cw.ToString("0.0") + "W"; }   // 충전 중일 때만
                }
                catch { }
                try { BeginInvoke(new Action(() => { vals = v; measured = true; busy = false; Render(); })); } catch { busy = false; }
            });
        }

        // GPU 3D 엔진 사용률 합 (윈도우 10 1709 이상)
        // 카운터 목록 만들기가 무거워서 매초 새로 만들면 게임이 1초마다 끊김 → 10초에 한 번만 목록 갱신, 값은 그냥 읽기
        static System.Collections.Generic.List<PerformanceCounter> gpuCounters;
        static int gpuListAt = -100000;
        static float gpuLast = -1;
        static float Gpu3D()
        {
            try
            {
                if (gpuCounters == null || Environment.TickCount - gpuListAt > 10000)
                {
                    gpuListAt = Environment.TickCount;
                    if (gpuCounters != null) foreach (var c in gpuCounters) c.Dispose();
                    var cat = new PerformanceCounterCategory("GPU Engine");
                    gpuCounters = cat.GetInstanceNames().Where(n => n.EndsWith("engtype_3D"))
                        .Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n)).ToList();
                    foreach (var c in gpuCounters) c.NextValue();
                    return gpuLast;
                }
                float sum = 0;
                foreach (var c in gpuCounters) { try { sum += c.NextValue(); } catch { } }
                return gpuLast = Math.Min(100, sum);
            }
            catch { return -1; }
        }

        static readonly string[] Short = { "FPS", "CPU", "CPU", "TDP", "GPU", "RAM", "전력", "전압", "배터리", "", "충전" };

        /// <summary>현재 설정으로 그림을 만들어 창에 올림</summary>
        public void Render()
        {
            if (IsDisposed) return;
            using (var bmp = Paint2())
            {
                var wa = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                int m = Settings.OvMargin <= 0 ? 0 : Settings.OvMargin == 2 ? Theme.S(28) : Theme.S(10), x, y;
                int cx = wa.X + (wa.Width - bmp.Width) / 2, cy = wa.Y + (wa.Height - bmp.Height) / 2;
                int l = wa.X + m, r = wa.Right - bmp.Width - m, t = wa.Y + m, b = wa.Bottom - bmp.Height - m;
                switch (Settings.OvPos)
                {
                    case 1: x = cx; y = t; break;
                    case 2: x = r; y = t; break;
                    case 3: x = l; y = b; break;
                    case 4: x = r; y = b; break;
                    case 5: x = l; y = cy; break;
                    case 6: x = r; y = cy; break;
                    case 7: x = cx; y = b; break;
                    case PosCustom:
                        if (Settings.OvX < 0) goto default;
                        x = wa.X + (int)(Settings.OvX * wa.Width); y = wa.Y + (int)(Settings.OvY * wa.Height);
                        break;
                    default: x = l; y = t; break;
                }
                if (dragging) { x = dragPos.X; y = dragPos.Y; }
                // 화면 밖으로 안 나가게
                x = Math.Max(wa.X, Math.Min(wa.Right - bmp.Width, x));
                y = Math.Max(wa.Y, Math.Min(wa.Bottom - bmp.Height, y));
                Push(bmp, new Point(x, y));
            }
        }

        Bitmap Paint2()
        {
            float k = Settings.OvSize == 0 ? 0.8f : Settings.OvSize == 2 ? 1.35f : 1f;
            var col = Colors[Math.Max(0, Math.Min(Colors.Length - 1, Settings.OvColor))];
            int ta = (int)(255 * Math.Max(20, Math.Min(100, Settings.OvText)) / 100f);
            var txt = Color.FromArgb(ta, col);
            var lab = Color.FromArgb((int)(ta * 0.62f), Color.White);
            int bga = (int)(255 * Math.Max(0, Math.Min(100, Settings.OvBg)) / 100f);
            if (editing) bga = Math.Max(bga, 170);   // 옮기는 동안은 잡을 수 있게 배경을 깔아 둠 (투명한 곳은 눌리지 않음)
            bool outline = bga < 90;   // 배경이 옅으면 글자에 검은 테두리 (밝은 화면에서도 보이게)

            // 보일 항목 (FPS는 측정 전이면 "-")
            var items = new List<int>();
            for (int i = 0; i < 11; i++) if (Want(i) && (vals[i] != null || (i == 0 && measured == false))) items.Add(i);
            if (Settings.OvStyle == 2) { items = new List<int> { 0 }; }

            using (var fV = new Font(Theme.FontName, Theme.S(14) * k, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fL = new Font(Theme.FontName, Theme.S(12) * k, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fBig = new Font(Theme.FontName, Theme.S(34) * k, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var mb = new Bitmap(1, 1))
            using (var mg = Graphics.FromImage(mb))
            {
                mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var fmt = StringFormat.GenericTypographic; fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                Func<string, Font, float> W = (s, f) => s == "" ? 0 : mg.MeasureString(s, f, 2000, fmt).Width;
                int pad = (int)(Theme.S(10) * k), gap = (int)(Theme.S(14) * k), rowH = (int)(Theme.S(22) * k);

                // 칸 목록 만들기: (라벨, 값)
                var cells = new List<string[]>();
                foreach (int i in items)
                {
                    string v = vals[i] ?? "-";
                    string l = Short[i];
                    if (i == 2 && Want(1) && Settings.OvStyle == 0) l = "";   // 가로 줄: CPU 45% 62°C 처럼 붙여 쓰기
                    if (i == 2 && Settings.OvStyle == 1) l = "온도";
                    if (i == 9 && Settings.OvStyle == 1) l = "시간";
                    cells.Add(new[] { l, v });
                }
                if (cells.Count == 0) cells.Add(new[] { "", "표시할 항목 없음" });

                int bw, bh;
                if (Settings.OvStyle == 2)
                {
                    string v = cells[0][1];
                    bw = (int)Math.Ceiling(W(v, fBig) + W("FPS", fL) + pad * 2 + Theme.S(6) * k);
                    bh = (int)(Theme.S(48) * k);
                }
                else if (Settings.OvStyle == 1)
                {
                    float lw = cells.Max(c => W(c[0], fL)), vw = cells.Max(c => W(c[1], fV));
                    bw = (int)Math.Ceiling(lw + vw + pad * 2 + gap);
                    bh = rowH * cells.Count + pad * 2 - (int)(Theme.S(4) * k);
                }
                else
                {
                    float tw = 0;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        tw += W(cells[i][0], fL) + (cells[i][0] == "" ? 0 : Theme.S(4) * k) + W(cells[i][1], fV);
                        if (i > 0) tw += cells[i][0] == "" ? Theme.S(5) * k : gap;
                    }
                    bw = (int)Math.Ceiling(tw + pad * 2);
                    bh = (int)(Theme.S(30) * k);
                }
                bw = Math.Max(bw, Theme.S(40));

                var bmp = new Bitmap(bw, bh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    if (bga > 0)
                        using (var path = Round(new Rectangle(0, 0, bw - 1, bh - 1), (int)(Theme.S(8) * k)))
                        using (var b = new SolidBrush(Color.FromArgb(bga, 0, 0, 0)))
                            g.FillPath(b, path);

                    Action<string, Font, Color, float, float> Txt = (s, f, c, x, y) =>
                    {
                        if (string.IsNullOrEmpty(s)) return;
                        if (outline)
                        {
                            using (var path = new GraphicsPath())
                            {
                                path.AddString(s, f.FontFamily, (int)f.Style, f.Size, new PointF(x, y), fmt);
                                using (var pen = new Pen(Color.FromArgb(Math.Min(220, (int)c.A), 0, 0, 0), Math.Max(2f, f.Size / 6f)) { LineJoin = LineJoin.Round })
                                    g.DrawPath(pen, path);
                                using (var b = new SolidBrush(c)) g.FillPath(b, path);
                            }
                        }
                        else using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y, fmt);
                    };

                    if (Settings.OvStyle == 2)
                    {
                        string v = cells[0][1];
                        float vw = W(v, fBig);
                        float y0 = (bh - fBig.Size * 1.1f) / 2;
                        Txt(v, fBig, txt, pad, y0);
                        Txt("FPS", fL, lab, pad + vw + Theme.S(6) * k, bh - pad - fL.Size * 1.3f);
                    }
                    else if (Settings.OvStyle == 1)
                    {
                        float lw = cells.Max(c => W(c[0], fL));
                        for (int i = 0; i < cells.Count; i++)
                        {
                            float y = pad + i * rowH;
                            Txt(cells[i][0], fL, lab, pad, y + (fV.Size - fL.Size) * 0.6f);
                            Txt(cells[i][1], fV, txt, pad + lw + gap, y);
                        }
                    }
                    else
                    {
                        float x = pad, y = (bh - fV.Size * 1.15f) / 2;
                        for (int i = 0; i < cells.Count; i++)
                        {
                            if (i > 0) x += cells[i][0] == "" ? Theme.S(5) * k : gap;
                            if (cells[i][0] != "")
                            {
                                Txt(cells[i][0], fL, lab, x, y + (fV.Size - fL.Size) * 0.6f);
                                x += W(cells[i][0], fL) + Theme.S(4) * k;
                            }
                            Txt(cells[i][1], fV, txt, x, y);
                            x += W(cells[i][1], fV);
                        }
                    }
                    if (editing)
                    {
                        using (var pen = new Pen(Color.FromArgb(255, 26, 159, 255), Math.Max(2f, Theme.S(2.5f))) { DashStyle = DashStyle.Dash })
                        using (var path = Round(new Rectangle(1, 1, bw - 3, bh - 3), (int)(Theme.S(8) * k)))
                            g.DrawPath(pen, path);
                    }
                }
                return bmp;
            }
        }

        static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ── 픽셀 단위 투명 창 올리기 ──
        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PT pptDst, ref SZ psize, IntPtr hdcSrc, ref PT pprSrc, int crKey, ref BLEND pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

        void Push(Bitmap bmp, Point at)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hb = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hb);
                var size = new SZ { W = bmp.Width, H = bmp.Height };
                var src = new PT();
                var dst = new PT { X = at.X, Y = at.Y };
                var bl = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
                UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref bl, 2);
                Bounds = new Rectangle(at, bmp.Size);
            }
            catch { }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
                if (hb != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hb); }
                DeleteDC(mem);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop(); timer.Dispose(); editTimer.Stop(); editTimer.Dispose(); cpu?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
