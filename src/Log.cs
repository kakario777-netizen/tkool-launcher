using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 기록: 내 문서\쯔꾸르 런처\logs
    /// - log.txt     : 켜고 끈 것·주요 동작·오류 요약 (최근 것만 유지)
    /// - errors.txt  : 오류 전체 내용 (어디서 났는지까지)
    /// - running.flag: 켜져 있는 동안만 있음 → 다음에 켤 때 남아 있으면 지난번에 갑자기 꺼졌다는 뜻
    /// </summary>
    static class Log
    {
        public static readonly string Dir = Path.Combine(Settings.Dir, "logs");
        static string LogPath => Path.Combine(Dir, "log.txt");
        public static string ErrPath => Path.Combine(Dir, "errors.txt");
        static string Flag => Path.Combine(Dir, "running.flag");
        static readonly object lk = new object();

        public static void Info(string msg) => Write("INFO", msg);

        static void Write(string level, string msg)
        {
            try
            {
                lock (lk)
                {
                    Directory.CreateDirectory(Dir);
                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        // 오래된 앞부분 버리고 뒤 절반만 남김
                        var all = File.ReadAllLines(LogPath, Encoding.UTF8);
                        File.WriteAllLines(LogPath, all.Skip(all.Length / 2), Encoding.UTF8);
                    }
                    File.AppendAllText(LogPath, DateTime.Now.ToString("MM-dd HH:mm:ss") + " [" + level + "] " + msg + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void Error(string where, Exception ex)
        {
            Write("ERROR", where + ": " + (ex?.GetType().Name ?? "") + " " + ex?.Message);
            try
            {
                lock (lk)
                {
                    Directory.CreateDirectory(Dir);
                    var fi = new FileInfo(ErrPath);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(ErrPath);
                    File.AppendAllText(ErrPath,
                        "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · v" + Updater.Current + " · " + where + Environment.NewLine +
                        ex + Environment.NewLine + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        // ───────── 켜고 끄기 ─────────
        public static string PreviousCrash;   // 지난번 비정상 종료 내용 (없으면 null)

        public static void Start()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(Flag))
                {
                    string last = "";
                    try
                    {
                        var lines = File.ReadAllLines(LogPath, Encoding.UTF8);
                        last = lines.LastOrDefault(l => l.Contains("[ERROR]") || l.Contains("[FATAL]")) ?? "";
                    }
                    catch { }
                    PreviousCrash = last.Length > 0 ? last : "기록 없이 꺼졌어요 (멈춤·강제 종료·전원 꺼짐 등)";
                }
                File.WriteAllText(Flag, DateTime.Now.ToString("o"));
            }
            catch { }
            Info("── 시작 v" + Updater.Current + " · " + Environment.OSVersion + " · " + (Environment.Is64BitProcess ? "64bit" : "32bit"));
        }

        public static void Stop()
        {
            Info("── 정상 종료");
            try { File.Delete(Flag); } catch { }
        }

        /// <summary>프로그램 전체 오류 잡기: 화면 쪽 오류는 팝업만 띄우고 계속, 그 외는 기록</summary>
        public static void Hook()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                Error("화면", e.Exception);
                ErrorPopup.ShowError(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Write("FATAL", "치명적 오류: " + ex?.Message);
                Error("치명적(꺼짐)", ex);
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Error("뒤 작업", e.Exception);
                e.SetObserved();
            };
        }

        public static string Tail(int maxChars)
        {
            try
            {
                if (!File.Exists(ErrPath)) return "";
                string t = File.ReadAllText(ErrPath, Encoding.UTF8);
                return t.Length > maxChars ? t.Substring(t.Length - maxChars) : t;
            }
            catch { return ""; }
        }
    }

    /// <summary>오류 알림 팝업 (터치·패드로 닫기). 한 번에 하나만, 같은 오류가 쏟아지면 10초에 한 번만</summary>
    class ErrorPopup : Form, IPadTarget
    {
        static ErrorPopup cur;
        static int lastShown;
        readonly string title, body;
        Rectangle okBtn;

        public static void ShowError(Exception ex)
        {
            if (cur != null || Environment.TickCount - lastShown < 10000) return;
            lastShown = Environment.TickCount;
            string where = "";
            try
            {
                var st = new System.Diagnostics.StackTrace(ex, false);
                var f = st.GetFrames()?.FirstOrDefault(fr => fr.GetMethod()?.DeclaringType?.Namespace == "TkoolLauncher");
                if (f != null) where = f.GetMethod().DeclaringType.Name + "." + f.GetMethod().Name;
            }
            catch { }
            Show2("오류가 났지만 런처는 계속 켜져 있어요",
                (ex?.GetType().Name ?? "") + ": " + ex?.Message + (where != "" ? "\n위치: " + where : "") +
                "\n\n기록: 내 문서\\쯔꾸르 런처\\logs\\errors.txt\n이 파일을 Claude에게 보여주면 고칠 수 있어요.");
        }

        public static void Show2(string title, string body)
        {
            try
            {
                cur = new ErrorPopup(title, body);
                cur.FormClosed += (s, e) => cur = null;
                cur.Show();
                cur.Activate();
            }
            catch { cur = null; }
        }

        ErrorPopup(string t, string b)
        {
            title = t; body = b;
            FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(23, 29, 37);
            KeyPreview = true;
            var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            Size = new Size(Math.Min(sc.Width - 40, Theme.S(720)), Theme.S(320));
            Location = new Point((sc.Width - Width) / 2, (sc.Height - Height) / 2);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var p = new Pen(Color.FromArgb(214, 72, 72), Theme.S(3))) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
                using (var f = Theme.Px(20, true))
                    TextRenderer.DrawText(g, title, f, new Rectangle(Theme.S(24), Theme.S(18), Width - Theme.S(48), Theme.S(34)), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                using (var f = Theme.Px(14))
                    TextRenderer.DrawText(g, body, f, new Rectangle(Theme.S(24), Theme.S(62), Width - Theme.S(48), Height - Theme.S(140)), Color.FromArgb(200, 206, 214),
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                okBtn = new Rectangle(Width - Theme.S(184), Height - Theme.S(70), Theme.S(160), Theme.S(50));
                Theme.FillRound(g, okBtn, Theme.S(6), Theme.Accent);
                using (var f = Theme.Px(16, true))
                    TextRenderer.DrawText(g, "확인 (A)", f, okBtn, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            catch { }
        }

        public void OnPad(Pad b) { if (b == Pad.A || b == Pad.B || b == Pad.Start) Close(); }
        protected override void OnMouseUp(MouseEventArgs e) => Close();
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space) Close(); }
    }
}
