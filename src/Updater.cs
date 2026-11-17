using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 켤 때 GitHub 릴리스를 확인해 새 버전이 있으면 받아서 exe를 바꿔 끼우고 다시 켠다.
    /// 설치·확인 창 없음. 실행 중인 exe는 이름 바꾸기가 되므로 .old 로 밀어두고 새 파일을 그 자리에 둔다.
    /// </summary>
    static class Updater
    {
        const string Api = "https://api.github.com/repos/kakario777-netizen/tkool-launcher/releases/latest";

        public static bool RestartPending;

        static string ExePath => Application.ExecutablePath;

        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static void CleanupOld()
        {
            try { File.Delete(ExePath + ".old"); } catch { }
            try { File.Delete(ExePath + ".new"); } catch { }
        }

        /// <summary>status: 화면에 잠깐 띄울 글, ready: 교체 끝났을 때 (UI 스레드에서 호출)</summary>
        public static bool Checking;

        /// <summary>manual = 설정에서 직접 누른 경우 (결과를 꼭 알려줌)</summary>
        public static void CheckAsync(Control ui, Action<string> status, Action ready, bool manual = false)
        {
            if (Checking || RestartPending) { if (manual && RestartPending) status("새 버전이 준비됐어요. 게임을 끄면 바뀌어요"); return; }
            Checking = true;
            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.UserAgent] = "TkoolLauncher";
                        string json = wc.DownloadString(Api);

                        string tag = TextDecode.JsonString(json, "tag_name");
                        if (tag == null) return;
                        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version latest)) return;
                        latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
                        if (latest <= Current)
                        {
                            if (manual) ui.BeginInvoke(new Action(() => status("최신 버전이에요 (v" + Current + ")")));
                            return;
                        }

                        string url = TextDecode.JsonString(json, "browser_download_url");
                        if (url == null || !url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;

                        ui.BeginInvoke(new Action(() => status("새 버전 " + tag + " 받는 중...")));

                        string tmp = ExePath + ".new";
                        wc.Headers[HttpRequestHeader.UserAgent] = "TkoolLauncher";
                        wc.DownloadFile(url, tmp);

                        // 받은 파일이 제대로 된 exe인지 확인
                        var fi = new FileInfo(tmp);
                        if (fi.Length < 10000) { File.Delete(tmp); return; }
                        using (var fs = File.OpenRead(tmp))
                        {
                            if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') { fs.Close(); File.Delete(tmp); return; }
                        }

                        string old = ExePath + ".old";
                        try { File.Delete(old); } catch { }
                        File.Move(ExePath, old);
                        try { File.Move(tmp, ExePath); }
                        catch { File.Move(old, ExePath); throw; }

                        RestartPending = true;
                        ui.BeginInvoke(ready);
                    }
                }
                catch
                {
                    // 인터넷이 없거나 폴더에 쓰기 권한이 없으면 조용히 넘어감 (직접 눌렀을 때만 알림)
                    if (manual) try { ui.BeginInvoke(new Action(() => status("업데이트 확인 실패 — 인터넷 연결을 확인해 주세요"))); } catch { }
                }
                finally { Checking = false; }
            });
        }

        public static void Restart()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ExePath, "--updated") { UseShellExecute = false });
            }
            catch { return; }
            Log.Info("업데이트로 다시 켬");
            Application.Exit();
        }
    }
}
