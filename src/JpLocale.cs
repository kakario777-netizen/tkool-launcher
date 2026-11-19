using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TkoolLauncher
{
    /// <summary>일본어 환경으로 실행 (Locale Emulator) — 한국어 윈도우에서 일본 게임이 일본어 파일 이름·글을 못 읽어
    /// "긁갉긁" 같은 깨진 글자 오류(파일 못 찾음)를 낼 때. 게임별로 켜 두면 다음부터 늘 일본어 환경으로 켬.</summary>
    static class JpLocale
    {
        const string Url = "https://github.com/xupefei/Locale-Emulator/releases/download/v2.5.0.1/Locale.Emulator.2.5.0.1.zip";
        static string Dir => Path.Combine(Settings.Dir, "tools", "LocaleEmulator");
        public static string Proc => Path.Combine(Dir, "LEProc.exe");
        static string ListPath => Path.Combine(Settings.Dir, "cache", "jplocale.txt");
        static HashSet<string> ids;

        static HashSet<string> Ids()
        {
            if (ids != null) return ids;
            ids = new HashSet<string>();
            try { if (File.Exists(ListPath)) foreach (var l in File.ReadAllLines(ListPath, Encoding.UTF8)) if (l.Trim().Length > 0) ids.Add(l.Trim()); } catch { }
            return ids;
        }

        public static bool Has(GameInfo g) { lock (typeof(JpLocale)) return g != null && Ids().Contains(GameData.IdOf(g)); }

        public static void Set(GameInfo g, bool on)
        {
            lock (typeof(JpLocale))
            {
                var s = Ids(); string id = GameData.IdOf(g);
                if (on) s.Add(id); else s.Remove(id);
                try { Directory.CreateDirectory(Path.GetDirectoryName(ListPath)); File.WriteAllLines(ListPath, s.ToArray(), Encoding.UTF8); } catch { }
            }
        }

        /// <summary>Locale Emulator 받아 두기 (없으면). 성공하면 true</summary>
        public static bool Ensure()
        {
            if (File.Exists(Proc) && File.Exists(Path.Combine(Dir, "LoaderDll.dll"))) return true;
            try
            {
                Directory.CreateDirectory(Dir);
                string zip = Path.Combine(Dir, "le.zip");
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                using (var wc = new System.Net.WebClient()) { wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "Mozilla/5.0"; wc.DownloadFile(Url, zip); }
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '" + zip.Replace("'", "''") + "' -DestinationPath '" + Dir.Replace("'", "''") + "' -Force\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) p.WaitForExit(60000);
                try { File.Delete(zip); } catch { }
                Log.Info("Locale Emulator 받음: " + File.Exists(Proc));
                return File.Exists(Proc);
            }
            catch (Exception ex) { Log.Error("Locale Emulator 받기", ex); return false; }
        }

        /// <summary>게임 켜기 — 일본어 환경이 켜진 게임이면 LEProc로 켜고, 실제 게임 프로세스를 돌려줌</summary>
        public static Process Start(GameInfo g, string exe, string wd)
        {
            if (!Has(g) || !Ensure()) return Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = wd, UseShellExecute = true });
            Log.Info("일본어 환경으로 실행: " + g.Title);
            var le = Process.Start(new ProcessStartInfo(Proc, "-run \"" + exe + "\"") { WorkingDirectory = wd, UseShellExecute = false, CreateNoWindow = true });
            // LEProc는 게임을 켜고 바로 끝남 → 게임 폴더 프로그램을 찾아 그걸 지켜봄
            string dir = g.InstallDir ?? g.Folder, wdir = wd;
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(150);
                foreach (var p in Process.GetProcesses())
                {
                    try { if (p.Id != le.Id && (GameWatch.InDir(p.Id, dir) || GameWatch.InDir(p.Id, wdir))) return p; } catch { }
                }
            }
            return le;
        }

        static Encoding Cp949, Sjis;
        static readonly Regex Hangul = new Regex(@"[\uAC00-\uD7A3]"), Kana = new Regex(@"[\u3040-\u30FF]"), Kanji = new Regex(@"[\u4E00-\u9FFF]"), Half = new Regex(@"[\uFF61-\uFF9F]");

        /// <summary>일본어(Shift-JIS)를 한국어로 잘못 읽어 깨진 글이면 원래 일본어로 되돌림, 아니면 null</summary>
        public static string Unmangle(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return null;
                if (Cp949 == null) { Cp949 = Encoding.GetEncoding(949); Sjis = Encoding.GetEncoding(932); }
                int hg = Hangul.Matches(text).Count;
                if (hg < 2) return null;
                string jp = Sjis.GetString(Cp949.GetBytes(text));
                int kana = Kana.Matches(jp).Count + Kanji.Matches(jp).Count, half = Half.Matches(jp).Count;
                // 깨진 한글이 일본어로 돌아가면 가나·한자가 생기고 한글은 사라짐 (진짜 한국어는 반각 가타카나 범벅이 됨)
                if (kana >= 2 && half < kana && Hangul.Matches(jp).Count < hg / 3 + 1) return jp;
            }
            catch { }
            return null;
        }
    }
}
