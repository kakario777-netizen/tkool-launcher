using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임을 켰을 때 표지·이름 채우기
    /// - 표지가 없는 게임: 켜고 12초쯤 뒤(타이틀 화면) 게임 창을 찍어 표지로 씀
    /// - 이름이 "Game"처럼 의미 없는 게임: 게임 창 제목에서 진짜 이름을 가져옴
    /// - 게임 중 빠른 설정의 [지금 화면을 표지로]로 원하는 장면을 직접 찍을 수도 있음
    /// </summary>
    static class GameCapture
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        static readonly string[] Generic = { "game", "rpg_rt", "rpgmv", "rpgmz", "rpg maker", "rpg maker mv", "rpg maker mz", "project1", "project", "new game", "nw.js", "untitled" };

        public static bool IsGenericTitle(GameInfo g, string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return true;
            string l = t.Trim().ToLowerInvariant();
            foreach (var x in Generic) if (l == x) return true;
            return l == Path.GetFileNameWithoutExtension(g.Exe ?? "").ToLowerInvariant();
        }

        static IntPtr GameWindow()
        {
            IntPtr fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out uint pid);
            return GameWatch.InDir((int)pid, GameWatch.Dir) ? fg : IntPtr.Zero;
        }

        /// <summary>게임 켠 뒤 불러줌: 필요할 때만 몇 번 시도</summary>
        public static void Watch(ShellForm shell, GameInfo g)
        {
            bool needCover = false;   // 표지는 아무 화면이나 자동으로 찍지 않음 — 사용자가 직접 (Y 두 번)
            bool needName = IsGenericTitle(g, g.Title) || g.Title == Path.GetFileName(g.Folder);
            if (!needCover && !needName) return;
            int tries = 0;
            var t = new Timer { Interval = 12000 };
            t.Tick += (s, e) =>
            {
                if (!GameWatch.Running || ++tries > 4) { t.Stop(); t.Dispose(); return; }
                t.Interval = 8000;
                IntPtr w = GameWindow();
                if (w == IntPtr.Zero) return; // 게임이 앞에 없으면 다음 기회에
                if (needName)
                {
                    var sb = new StringBuilder(256);
                    GetWindowText(w, sb, 256);
                    string title = CleanTitle(sb.ToString());
                    if (!IsGenericTitle(g, title))
                    {
                        GameData.Get(g).Title = title;
                        GameData.Save();
                        g.Title = title;
                        needName = false;
                        if (!needCover) CoverArt.Invalidate(g); // 표지 글씨도 새 이름으로
                    }
                }
                if (needCover && Shot(g, w)) needCover = false;
                if (!needCover && !needName) { t.Stop(); t.Dispose(); shell.RefreshLists(); }
            };
            t.Start();
        }

        static string CleanTitle(string s)
        {
            s = (s ?? "").Trim();
            // "제목 - RPG Maker VX Ace" 같은 꼬리 떼기
            foreach (var tail in new[] { " - RPG Maker", " - RPGツクール", " | RPG Maker" })
            {
                int i = s.IndexOf(tail, StringComparison.OrdinalIgnoreCase);
                if (i > 0) s = s.Substring(0, i).Trim();
            }
            return s;
        }

        // ───────── 표지·이름 한 번에 찾기 (플레이 전에) ─────────
        public static volatile bool Finding, Cancel;

        public static List<GameInfo> Missing(IEnumerable<GameInfo> games) =>
            games.Where(g => (g.Kind == GameKind.Tkool || g.Kind == GameKind.Pc) && !GameData.Get(g).Hidden &&
                             ((CoverArt.Needs(g) && !File.Exists(CoverArt.AutoShotPath(g))) || IsGenericTitle(g, g.Title))).ToList();

        static void KillDir(string dir)
        {
            foreach (var p in Process.GetProcesses())
            {
                try { if (p.Id != Process.GetCurrentProcess().Id && GameWatch.InDir(p.Id, dir)) p.Kill(); }
                catch { }
                finally { p.Dispose(); }
            }
        }

        /// <summary>게임을 하나씩 잠깐 켜서(약 15초) 타이틀 화면을 찍고 창 제목을 가져온 뒤 끔</summary>
        public static void FindAll(List<GameInfo> list, Action<string> progress, Action<int> done)
        {
            if (Finding) return;
            Finding = true; Cancel = false;
            var th = new System.Threading.Thread(() =>
            {
                int okCount = 0;
                for (int i = 0; i < list.Count && !Cancel; i++)
                {
                    var g = list[i];
                    progress((i + 1) + "/" + list.Count + " · " + g.Title + " — 홈 버튼을 누르면 멈춰요");
                    string dir = g.InstallDir ?? g.Folder;
                    try
                    {
                        Process.Start(new ProcessStartInfo(g.Exe) { WorkingDirectory = g.Folder, UseShellExecute = true });
                        IntPtr w = IntPtr.Zero;
                        for (int k = 0; k < 25 && !Cancel && w == IntPtr.Zero; k++)
                        {
                            System.Threading.Thread.Sleep(1000);
                            IntPtr fg = GetForegroundWindow();
                            GetWindowThreadProcessId(fg, out uint pid);
                            if (GameWatch.InDir((int)pid, dir)) w = fg;
                        }
                        if (w != IntPtr.Zero)
                        {
                            for (int k = 0; k < 10 && !Cancel; k++) System.Threading.Thread.Sleep(1000); // 타이틀 화면까지 기다림
                            bool got = false;
                            if (!Cancel)
                            {
                                var sb = new StringBuilder(256);
                                GetWindowText(w, sb, 256);
                                string title = CleanTitle(sb.ToString());
                                if (IsGenericTitle(g, g.Title) && !IsGenericTitle(g, title))
                                {
                                    GameData.Get(g).Title = title; GameData.Save(); g.Title = title; got = true;
                                    CoverArt.Invalidate(g);
                                }
                                if (CoverArt.Needs(g) && Shot(g, w)) got = true;
                            }
                            if (got) okCount++;
                        }
                    }
                    catch { }
                    KillDir(dir);
                    System.Threading.Thread.Sleep(1500);
                }
                Finding = false;
                done(okCount);
            }) { IsBackground = true };
            th.SetApartmentState(System.Threading.ApartmentState.STA);
            th.Start();
        }

        /// <summary>지금 앞에 있는 게임 화면을 표지로 (빠른 설정 버튼)</summary>
        public static bool ShotNow(GameInfo g)
        {
            IntPtr w = GameWindow();
            return w != IntPtr.Zero && Shot(g, w, true);
        }

        static bool Shot(GameInfo g, IntPtr w, bool force = false)
        {
            Bitmap bmp = null;
            try
            {
                if (Scaler.Mode != 0)
                {
                    // 화면 확대 중이면 확대된 전체 화면을 찍음
                    var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                    bmp = new Bitmap(sc.Width, sc.Height);
                    OverlayTip.HideAll(true);
                    try { using (var gr = Graphics.FromImage(bmp)) gr.CopyFromScreen(sc.Location, Point.Empty, sc.Size); }
                    finally { OverlayTip.HideAll(false); }
                }
                else
                {
                    if (!GetClientRect(w, out RECT rc)) return false;
                    int cw = rc.R - rc.L, ch = rc.B - rc.T;
                    if (cw < 64 || ch < 64) return false;
                    bmp = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
                    using (var gr = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = gr.GetHdc();
                        bool ok = PrintWindow(w, hdc, 1 | 2); // 안쪽만, 그래픽 가속 화면까지
                        gr.ReleaseHdc(hdc);
                        if (!ok || Plain(bmp))
                        {
                            var p = new POINT();
                            ClientToScreen(w, ref p);
                            OverlayTip.HideAll(true);   // 안내 글이 같이 찍히지 않게
                            try { gr.CopyFromScreen(p.X, p.Y, 0, 0, new Size(cw, ch)); }
                            finally { OverlayTip.HideAll(false); }
                        }
                    }
                }
                if (!force && Plain(bmp)) return false; // 검은 화면·로딩 화면이면 다음에
                Directory.CreateDirectory(CoverArt.CoverDir);
                bmp.Save(CoverArt.AutoShotPath(g), ImageFormat.Png);
                CoverArt.Invalidate(g);
                return true;
            }
            catch { return false; }
            finally { bmp?.Dispose(); }
        }

        /// <summary>거의 한 색인지 (검은 화면·흰 화면·로딩)</summary>
        static bool Plain(Bitmap b)
        {
            double sum = 0, sq = 0;
            int n = 0;
            for (int y = 0; y < 20; y++)
                for (int x = 0; x < 20; x++)
                {
                    var c = b.GetPixel(b.Width * x / 20, b.Height * y / 20);
                    double l = 0.3 * c.R + 0.59 * c.G + 0.11 * c.B;
                    sum += l; sq += l * l; n++;
                }
            double mean = sum / n, sd = Math.Sqrt(Math.Max(0, sq / n - mean * mean));
            return sd < 10;
        }
    }
}
