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
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);

        /// <summary>게임이 띄운 오류 창 글 (게임 폴더 프로그램의 메시지 상자, 또는 "Game.exe - 시스템 오류")</summary>
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        static readonly HashSet<IntPtr> errSeen = new HashSet<IntPtr>();   // 이번 게임 전부터 떠 있던 오류 창 (다른 게임 것)
        static readonly List<IntPtr> errNow = new List<IntPtr>();

        /// <summary>떠 있는 오류 창 닫기 (윈도우가 띄운 "시스템 오류" 창은 게임을 꺼도 남아서 다음 게임까지 막음)</summary>
        static void CloseErrors()
        {
            foreach (var h in errNow) { try { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); } catch { } }   // WM_CLOSE
            errNow.Clear();
        }

        static string ErrorText(string dir, string exe)
        {
            var sb = new StringBuilder();
            errNow.Clear();
            string exeName = Path.GetFileName(exe ?? "").ToLowerInvariant();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                var cls = new StringBuilder(64); GetClassName(h, cls, 64);
                if (cls.ToString() != "#32770") return true;
                var tt = new StringBuilder(256); GetWindowText(h, tt, 256);
                GetWindowThreadProcessId(h, out uint pid);
                bool mine = GameWatch.InDir((int)pid, dir) || (exeName.Length > 0 && tt.ToString().ToLowerInvariant().StartsWith(exeName)) || Fixer.IsGameSysErr(tt.ToString(), dir);
                if (!mine || errSeen.Contains(h)) return true;
                errNow.Add(h);
                sb.Append(tt).Append('\n');
                EnumChildWindows(h, (c, l2) => { var t = new StringBuilder(512); GetWindowText(c, t, 512); if (t.Length > 2) sb.Append(t).Append('\n'); return true; }, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
            return sb.ToString();
        }
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
            // 표지가 없는 게임만 (이름은 이름 찾기가 따로 함 — 이름 때문에 켰다 바로 끄던 것 없앰)
            games.Where(g => (g.Kind == GameKind.Tkool || g.Kind == GameKind.Pc) && !GameData.Get(g).Hidden && !File.Exists(PendingPath(g)) &&
                             (CoverArt.Needs(g) || CoverArt.HasNoHero(g)) && !File.Exists(CoverArt.AutoShotPath(g))).ToList();

        /// <summary>찍어 볼 후보 전부 (표지 없음 여부는 FindAll 안에서 하나씩 정확히 확인)</summary>
        public static List<GameInfo> Candidates(IEnumerable<GameInfo> games) =>
            games.Where(g => (g.Kind == GameKind.Tkool || g.Kind == GameKind.Pc) && g.Exe != null && !GameData.Get(g).Hidden && !File.Exists(PendingPath(g))).ToList();

        /// <summary>찍었지만 아직 확인 안 한 표지 (covers\폴더.pending.png) — 표지 확인 화면에서 적용/취소</summary>
        public static string PendingPath(GameInfo g) => Path.Combine(CoverArt.CoverDir, Path.GetFileName(g.Folder ?? "") + ".pending.png");
        public static List<GameInfo> Pending(IEnumerable<GameInfo> games) => games.Where(g => g.Folder != null && File.Exists(PendingPath(g))).ToList();

        static int pendCount; static long pendAt;
        /// <summary>목록 화면용 (5초에 한 번만 셈)</summary>
        public static int PendingCount(List<GameInfo> games)
        {
            if (Environment.TickCount - pendAt > 5000 || pendAt == 0) { pendAt = Environment.TickCount; try { pendCount = Pending(games).Count; } catch { } }
            return pendCount;
        }

        public static void Accept(GameInfo g)
        {
            CoverArt.MarkCoverOk(g);   // 확인하고 넣은 표지 → 다시 안 찍음
            try
            {
                string auto = CoverArt.AutoShotPath(g);
                if (File.Exists(auto)) File.Delete(auto);
                File.Move(PendingPath(g), auto);
                CoverArt.Invalidate(g);
            }
            catch (Exception ex) { Log.Error("표지 적용", ex); }
            pendAt = 0;
        }
        public static void Reject(GameInfo g) { try { File.Delete(PendingPath(g)); } catch { } pendAt = 0; }

        // ───────── 타이틀 화면 알아보기 ─────────
        class Look { public double Mean, Sd, Color; public byte[] Sig; }

        static Look Analyze(Bitmap b)
        {
            var lk = new Look { Sig = new byte[24 * 18] };
            double sum = 0, sq = 0, col = 0; int n = 0;
            for (int y = 0; y < 18; y++)
                for (int x = 0; x < 24; x++)
                {
                    var c = b.GetPixel(Math.Min(b.Width - 1, b.Width * x / 24 + b.Width / 48), Math.Min(b.Height - 1, b.Height * y / 18 + b.Height / 36));
                    double l = 0.3 * c.R + 0.59 * c.G + 0.11 * c.B;
                    sum += l; sq += l * l; n++;
                    col += Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
                    lk.Sig[y * 24 + x] = (byte)l;
                }
            lk.Mean = sum / n; lk.Sd = Math.Sqrt(Math.Max(0, sq / n - lk.Mean * lk.Mean)); lk.Color = col / n;
            return lk;
        }

        static double Diff(Look a, Look b)
        {
            if (a == null || b == null) return 999;
            double d = 0;
            for (int i = 0; i < a.Sig.Length; i++) d += Math.Abs(a.Sig[i] - b.Sig[i]);
            return d / a.Sig.Length;
        }

        /// <summary>까만 바탕에 흰 글자만 있는 화면 (안내·경고·로고 글)</summary>
        static bool TextOnly(Look l) => l.Mean < 45 && l.Color < 14 && l.Sd >= 10;
        /// <summary>그림이 있는 화면 (타이틀 후보)</summary>
        static bool Picture(Look l) => l.Sd >= 18 && (l.Color >= 16 || l.Mean >= 60);

        /// <summary>게임 창을 0.5초마다 보며 타이틀 화면을 찾아 pending으로 저장. 글만 있는 화면은 Enter로 넘김</summary>
        static bool FindTitle(GameInfo g, IntPtr w)
        {
            Look prev = null; int stable = 0, textStable = 0, presses = 0;
            Bitmap best = null, firstText = null;   // 그림을 못 찾으면 처음 본 글 화면이라도 (어두운 타이틀일 수도)
            try
            {
                for (int t = 0; t < 80 && !Cancel; t++)   // 최대 40초
                {
                    System.Threading.Thread.Sleep(500);
                    using (var bmp = Grab(w))
                    {
                        if (bmp == null) continue;
                        var lk = Analyze(bmp);
                        bool same = Diff(lk, prev) < 3;
                        prev = lk;
                        if (lk.Sd < 10) { stable = textStable = 0; continue; }          // 검은 화면·로딩
                        if (TextOnly(lk))
                        {
                            stable = 0;
                            textStable = same ? textStable + 1 : 0;
                            if (textStable >= 3 && presses < 8 && GetForegroundWindow() == w)   // 1.5초 그대로면 넘김
                            {
                                if (firstText == null) firstText = (Bitmap)bmp.Clone(); KeySender.Combo(KeySender.Enter); presses++; textStable = 0; }
                            continue;
                        }
                        if (!Picture(lk)) { stable = 0; continue; }
                        stable = same ? stable + 1 : 0;
                        best?.Dispose(); best = (Bitmap)bmp.Clone();
                        if (stable >= 4) break;        // 그림이 2초 그대로 = 타이틀
                    }
                }
                if (best == null) { best = firstText; firstText = null; }
                if (best == null) return false;
                Directory.CreateDirectory(CoverArt.CoverDir);
                best.Save(PendingPath(g), ImageFormat.Png); pendAt = 0;
                return true;
            }
            catch (Exception ex) { Log.Error("타이틀 화면 찾기", ex); return false; }
            finally { best?.Dispose(); firstText?.Dispose(); }
        }

        /// <summary>게임 켜기 (런처·표지 찍기 같은 길): 방화벽 허용 → # % ?·영문 연결 경로 → 일본어 환경</summary>
        public static Process StartGame(GameInfo g)
        {
            Firewall.Allow(g);
            string runDir = GameDoctor.SafeRunDir(g);
            string exe = g.Exe, wd = g.Folder;
            if (runDir != null && g.Exe.StartsWith(g.Folder, StringComparison.OrdinalIgnoreCase))
            { exe = runDir + g.Exe.Substring(g.Folder.TrimEnd('\\').Length); wd = runDir; }
            return JpLocale.Start(g, exe, wd);
        }

        public static void KillDir(string dir)
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
                // 화면에 안 나온 게임도 빠짐없이: 표지가 정말 없는지 하나씩 확인 (목록이 많으면 잠깐 걸림)
                var all = list;
                list = new List<GameInfo>();
                var nameOnly = new HashSet<GameInfo>();   // 표지는 있고 이름만 (인터넷에서 못 찾은 것) → 창 제목만 보고 끔
                var needName = new HashSet<GameInfo>();
                for (int c = 0; c < all.Count && !Cancel; c++)
                {
                    if (c % 10 == 0) { Activity.Set("cover", "표지 없는 게임 고르는 중", c, all.Count); progress("표지 없는 게임 고르는 중 " + c + "/" + all.Count); }
                    bool nc = false, nn = false;
                    try { nc = CoverArt.NoCover(all[c]); } catch { }
                    try { nn = NameFinder.NeedsWinName(all[c]); } catch { }
                    if (nc || nn) list.Add(all[c]);
                    if (!nc && nn) nameOnly.Add(all[c]);
                    if (nn) needName.Add(all[c]);
                }
                Log.Info("표지 찍기: 후보 " + all.Count + "개 중 " + list.Count + "개 (이름만 " + nameOnly.Count + "개)");
                int okCount = 0, skipped = 0, nameCount = 0;
                var fixedKinds = new HashSet<string>();   // 같은 부품은 한 번만 설치
                bool retry = false; int retries = 0;
                for (int i = 0; i < list.Count && !Cancel; i++)
                {
                    var g = list[i];
                    progress((i + 1) + "/" + list.Count + " · " + g.Title + " — 홈 버튼을 누르면 멈춰요");
                    Activity.Set("cover", "표지 찍는 중 · " + g.Title, i + 1, list.Count);
                    // 다른 곳에서 오류를 고치는 중이면 끝날 때까지 멈췄다가 이어서
                    while (Fixer.Busy && !Cancel) { progress("오류 고치는 중 — 끝나면 이어서 찍어요"); System.Threading.Thread.Sleep(1000); }
                    string dir = g.InstallDir ?? g.Folder;
                    string err = "";
                    // 앞 게임 때부터 남아 있는 오류 창은 이 게임 것이 아님 → 닫고, 못 닫은 건 무시 목록에
                    errSeen.Clear();
                    EnumWindows((h, l) => { if (IsWindowVisible(h)) { var c = new StringBuilder(16); GetClassName(h, c, 16); if (c.ToString() == "#32770") errSeen.Add(h); } return true; }, IntPtr.Zero);
                    DateTime started = DateTime.Now;
                    // 이름만 필요하면 먼저 가진 그림(표지·타이틀 그림)의 글자로 — 되면 안 켬
                    if (nameOnly.Contains(g) && !retry)
                    {
                        string pn = null;
                        try { pn = Ocr.NameFromPictures(g); } catch { }
                        if (pn != null) { nameCount++; Activity.Note("타이틀 그림 글자로 이름 찾음 · " + pn); continue; }
                    }
                    try
                    {
                        started = DateTime.Now;
                        StartGame(g);
                        IntPtr w = IntPtr.Zero;
                        for (int k = 0; k < 25 && !Cancel && w == IntPtr.Zero; k++)
                        {
                            System.Threading.Thread.Sleep(1000);
                            err = ErrorText(dir, g.Exe);
                            if (err.Length > 0) break;          // 오류 창 → 아래에서 고침
                            IntPtr fg = GetForegroundWindow();
                            GetWindowThreadProcessId(fg, out uint pid);
                            if (GameWatch.InDir((int)pid, dir)) w = fg;
                        }
                        if (err.Length == 0 && w != IntPtr.Zero)
                        {
                            System.Threading.Thread.Sleep(1500);
                            string fl = GameDoctor.FolderListTitle(dir);
                            if (fl != null) err = "폴더 목록 화면: " + fl;
                        }
                        if (err.Length == 0 && w != IntPtr.Zero && needName.Contains(g))
                        {
                            // 창 제목은 켜고 조금 뒤에 바뀌는 게임이 많아 몇 번 봄
                            string wt = "";
                            for (int k = 0; k < 4 && !Cancel; k++)
                            {
                                System.Threading.Thread.Sleep(1500);
                                var sb = new StringBuilder(256); GetWindowText(w, sb, 256);
                                wt = CleanTitle(sb.ToString());
                                if (!NameFinder.Meaningless(g, wt)) break;
                            }
                            if (!Cancel && NameFinder.ApplyWinTitle(g, wt)) { nameCount++; Activity.Note("창 제목으로 이름 찾음 · " + g.Title); }
                        }
                        if (err.Length == 0 && w != IntPtr.Zero && !nameOnly.Contains(g))
                        {
                            if (!Cancel && FindTitle(g, w))
                            {
                                okCount++;
                                Activity.Detail("cover", "✓ " + g.Title + " — 타이틀 화면 찍음 (확인 목록에)");
                                if (needName.Contains(g) && NameFinder.NeedsWinName(g) || needName.Contains(g) && GameData.Get(g).NameFrom == NameFinder.WinTried)
                                {
                                    string n = Ocr.NameFromShot(g, PendingPath(g), true);
                                    if (n != null) { nameCount++; Activity.Note("타이틀 화면 글자로 이름 찾음 · " + n); }
                                }
                            }   // 타이틀 화면까지 지켜봄 (확인 화면에서 적용)
                            if (!Cancel) err = ErrorText(dir, g.Exe);       // 지켜보는 중에 뜬 오류 창
                        }
                    }
                    catch (Exception ex) { Log.Info("표지 찍기 실행 " + g.Title + ": " + ex.Message); }
                    KillDir(dir);
                    if (err.Length > 0) { CloseErrors(); System.Threading.Thread.Sleep(300); }
                    if (err.Length == 0 && !Cancel)
                    {
                        // 오류 창 없이 튕겼으면 윈도우 기록에서 원인 모듈 (msvcr120.dll·xaudio2_7.dll 등)
                        string crash = "";
                        try { System.Threading.Thread.Sleep(800); crash = Fixer.CrashInfo(dir, started); } catch { }
                        if (crash.Length > 0) err = crash;
                    }

                    // 오류 창이 떴으면: 고칠 수 있는 부품(DirectX·Visual C++)은 설치하고 이 게임을 한 번 더
                    if (err.Length > 0 && !Cancel)
                    {
                        if (retries < 3 && Fixer.CanFix(g, err))   // 같은 게임에 다른 오류가 이어 나오면 그것도 (부품마다 한 번)
                        {
                            Activity.Set("cover", "오류 먼저 고치는 중 · " + g.Title, i + 1, list.Count);
                            progress("표지 찍기 잠깐 멈춤 · 오류 고치는 중 — " + g.Title + " (끝나면 이 게임부터 이어서)");
                            string r = Fixer.FixNow(g, err);
                            progress("고쳤어요 — 다시 켜 봐요: " + r);
                            retry = true; retries++; i--;          // 같은 게임 다시
                            System.Threading.Thread.Sleep(1500);
                            continue;
                        }
                        Fixer.NoteUnknown(g, err);
                        skipped++;
                        Activity.Detail("cover", "✕ " + g.Title + " — 오류로 건너뜀: " + err.Replace("\n", " ").Trim());
                        Log.Info("표지 찍기 오류 창: " + g.Title + " — " + err.Replace("\n", " / "));
                    }
                    retry = false; retries = 0;
                    System.Threading.Thread.Sleep(1500);
                }
                Finding = false;
                Activity.Done("cover", "표지 찍기 끝 · " + list.Count + "개 중 " + okCount + "개" + (nameCount > 0 ? ", 이름 " + nameCount + "개" : "") + (skipped > 0 ? ", 오류로 " + skipped + "개 건너뜀" : ""));
                done(okCount);
            }) { IsBackground = true };
            th.SetApartmentState(System.Threading.ApartmentState.STA);
            th.Start();
        }

        /// <summary>지금 앞에 있는 게임 화면을 표지로 (빠른 설정 버튼)</summary>
        public static bool ShotNow(GameInfo g, bool pending = false)
        {
            IntPtr w = GameWindow();
            bool ok = w != IntPtr.Zero && Shot(g, w, true, pending);
            if (ok && !pending) CoverArt.MarkCoverOk(g);   // 사용자가 직접 찍은 표지 → 다시 안 찍음
            return ok;
        }

        /// <summary>게임 창 안쪽을 그림으로 (못 찍으면 null)</summary>
        static Bitmap Grab(IntPtr w)
        {
            Bitmap bmp = null;
            try
            {
                if (!GetClientRect(w, out RECT rc)) return null;
                int cw = rc.R - rc.L, ch = rc.B - rc.T;
                if (cw < 64 || ch < 64) return null;
                bmp = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
                using (var gr = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = gr.GetHdc();
                    bool ok = PrintWindow(w, hdc, 1 | 2);
                    gr.ReleaseHdc(hdc);
                    if (!ok || Plain(bmp))
                    {
                        var p = new POINT();
                        ClientToScreen(w, ref p);
                        OverlayTip.HideAll(true);
                        try { gr.CopyFromScreen(p.X, p.Y, 0, 0, new Size(cw, ch)); }
                        finally { OverlayTip.HideAll(false); }
                    }
                }
                return bmp;
            }
            catch { bmp?.Dispose(); return null; }
        }

        static bool Shot(GameInfo g, IntPtr w, bool force = false, bool pending = false)
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
                bmp.Save(pending ? PendingPath(g) : CoverArt.AutoShotPath(g), ImageFormat.Png);   // 다시 찍기는 확인 목록으로
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
