using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임 화면 강제로 키우기 (게임의 전체 화면 기능이 안 먹거나 검은 화면이 될 때):
    /// 1) 창 테두리를 없애고 비율 그대로 화면에 꽉 맞춤 — 쯔꾸르 XP·VX·VX Ace 등 창 크기에 맞춰 늘려 그리는 게임
    /// 2) 게임이 크기를 원래대로 되돌리면(고정 크기) 원래 크기로 두고 윈도우 확대 기능(Scaler)으로 키움
    /// MV·MZ는 게임 자체 전체 화면(F4)이 잘 되니 그걸 씀.
    /// </summary>
    static class ScreenFit
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out R r);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct R { public int L, T, Rr, B; }

        public static bool On => win != IntPtr.Zero;

        // 게임별로 고른 크기 기억 (cache\screenfit.txt id=모드) → 다음에 켜면 창이 뜰 때 알아서
        static readonly string MemFile = System.IO.Path.Combine(Settings.Dir, "cache", "screenfit.txt");
        public static int Remembered(GameInfo g)
        {
            try
            {
                string id = GameData.IdOf(g);
                if (System.IO.File.Exists(MemFile))
                    foreach (var l in System.IO.File.ReadAllLines(MemFile))
                        if (l.StartsWith(id + "=")) { int m; if (int.TryParse(l.Substring(id.Length + 1), out m)) return m; }
            }
            catch { }
            return 0;
        }
        /// <summary>게임 중 작업 표시줄: -1 기본(설정), 0 숨기기, 1 보이기</summary>
        public static int GetTaskbar(GameInfo g)
        {
            try
            {
                string k = "tb:" + GameData.IdOf(g) + "=";
                if (System.IO.File.Exists(MemFile))
                    foreach (var l in System.IO.File.ReadAllLines(MemFile))
                        if (l.StartsWith(k)) { int m; if (int.TryParse(l.Substring(k.Length), out m)) return m; }
            }
            catch { }
            return -1;
        }
        public static void SetTaskbar(GameInfo g, int v)
        {
            try
            {
                string k = "tb:" + GameData.IdOf(g) + "=";
                var lines = System.IO.File.Exists(MemFile) ? System.IO.File.ReadAllLines(MemFile).Where(l => !l.StartsWith(k)).ToList() : new System.Collections.Generic.List<string>();
                if (v >= 0) lines.Add(k + v);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MemFile));
                System.IO.File.WriteAllLines(MemFile, lines);
            }
            catch { }
        }

        public static void Remember(GameInfo g, int mode)
        {
            try
            {
                string id = GameData.IdOf(g);
                var lines = System.IO.File.Exists(MemFile) ? System.IO.File.ReadAllLines(MemFile).Where(l => !l.StartsWith(id + "=")).ToList() : new System.Collections.Generic.List<string>();
                if (mode > 0) lines.Add(id + "=" + mode);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MemFile));
                System.IO.File.WriteAllLines(MemFile, lines);
            }
            catch { }
        }
        static IntPtr win;
        static int oldStyle;
        static R oldRect;
        static R target;
        static int reverts;
        static Timer lockT;
        public static string LastWay = "";

        public static readonly string[] ModeNames = { "원래 크기", "꽉 채우기", "도트 선명", "2배", "3배" };
        public static int Mode;   // 지금 걸린 방식

        public static void Toggle(IntPtr h, int scalerMode)
        {
            if (On) { Restore(); return; }
            Apply(h, 1, scalerMode);
        }

        /// <summary>게임 화면 크기 강제: 0 원래 / 1 꽉 채우기 / 2 정수배 최대 / 3 2배 / 4 3배 (비율은 언제나 그대로)</summary>
        public static void Apply(IntPtr h, int mode, int scalerMode = 2)
        {
            if (On) Restore();
            Mode = 0;
            if (mode <= 0 || h == IntPtr.Zero || !IsWindow(h)) return;
            win = h;
            oldStyle = GetWindowLong(h, -16);
            GetWindowRect(h, out oldRect);
            GetClientRect(h, out R c);
            int cw = Math.Max(1, c.Rr - c.L), ch = Math.Max(1, c.B - c.T);
            var sb = TaskbarHide.Hidden ? System.Windows.Forms.Screen.FromHandle(h).Bounds : System.Windows.Forms.Screen.FromHandle(h).WorkingArea;   // 작업 표시줄 보이면 그 위 칸까지만
            double fit = Math.Min(sb.Width / (double)cw, sb.Height / (double)ch);
            double k = mode == 2 ? Math.Max(1, Math.Floor(fit)) : mode == 3 ? Math.Min(2, fit) : mode == 4 ? Math.Min(3, fit) : fit;
            Mode = mode;
            int nw = (int)(cw * k), nh = (int)(ch * k);
            // 테두리·제목 막대 없앰 → 게임 그림만 화면 가운데 꽉 (남는 곳은 런처 검은 판)
            SetWindowLong(h, -16, (oldStyle & ~0x00C00000 & ~0x00040000 & ~0x00080000 & ~0x00020000 & ~0x00010000) | unchecked((int)0x80000000));
            target = new R { L = sb.X + (sb.Width - nw) / 2, T = sb.Y + (sb.Height - nh) / 2 };
            target.Rr = target.L + nw; target.B = target.T + nh;
            SetWindowPos(h, IntPtr.Zero, target.L, target.T, nw, nh, 0x0020 | 0x0004 | 0x0040);   // FRAMECHANGED | NOZORDER | SHOWWINDOW
            reverts = 0;
            LastWay = "창 늘리기";
            Log.Info("화면 키우기(창): " + cw + "x" + ch + " → " + nw + "x" + nh);
            // 게임이 크기를 되돌리는지 지켜봄 — 두 번 되돌리면 확대 기능으로
            lockT?.Dispose();
            lockT = new Timer { Interval = 700 };
            lockT.Tick += (s, e) =>
            {
                if (!IsWindow(win)) { Stop(); return; }
                if (Scaler.Mode != 0) return;
                GetWindowRect(win, out R r);
                if (Math.Abs(r.Rr - r.L - (target.Rr - target.L)) > 8 || Math.Abs(r.B - r.T - (target.B - target.T)) > 8)
                {
                    if (++reverts >= 2) { ToMagnifier(scalerMode); return; }
                    SetWindowPos(win, IntPtr.Zero, target.L, target.T, target.Rr - target.L, target.B - target.T, 0x0004 | 0x0010);   // 다시 (잠금)
                }
            };
            lockT.Start();
        }

        static void ToMagnifier(int scalerMode)
        {
            // 게임이 창 크기를 고정함 → 원래 크기·테두리로 되돌리고 화면 확대로 키움
            IntPtr h = win;
            SetWindowLong(h, -16, oldStyle);
            var sb = System.Windows.Forms.Screen.FromHandle(h).Bounds;
            int w = oldRect.Rr - oldRect.L, hh = oldRect.B - oldRect.T;
            SetWindowPos(h, IntPtr.Zero, sb.X + (sb.Width - w) / 2, sb.Y + (sb.Height - hh) / 2, w, hh, 0x0020 | 0x0004);
            bool ok = Scaler.Start(scalerMode <= 0 ? 2 : scalerMode, h);
            LastWay = ok ? "화면 확대" : "실패: " + Scaler.Problem;
            Log.Info("화면 키우기: 게임이 크기를 되돌려서 확대 기능으로 → " + LastWay);
        }

        public static void Restore()
        {
            if (!On) return;
            IntPtr h = win;
            Stop();
            try
            {
                if (Scaler.Mode != 0) Scaler.Stop();
                if (IsWindow(h))
                {
                    SetWindowLong(h, -16, oldStyle);
                    var sb = System.Windows.Forms.Screen.FromHandle(h).WorkingArea;
                    int w = oldRect.Rr - oldRect.L, hh = oldRect.B - oldRect.T;
                    SetWindowPos(h, IntPtr.Zero, sb.X + (sb.Width - w) / 2, sb.Y + (sb.Height - hh) / 2, w, hh, 0x0020 | 0x0004);
                }
            }
            catch { }
            LastWay = ""; Mode = 0;
        }

        /// <summary>게임이 끝나면 정리만</summary>
        public static void Stop()
        {
            lockT?.Stop(); lockT?.Dispose(); lockT = null;
            win = IntPtr.Zero; Mode = 0;
        }
    }
}
