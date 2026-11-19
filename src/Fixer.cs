using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TkoolLauncher
{
    /// <summary>오류 먼저: 게임 오류 창·튕김을 보면 원인을 찾아 런처가 스스로 고침 (표지 찾기·이름 찾기보다 우선)</summary>
    static class Fixer
    {
        static readonly HashSet<string> tried = new HashSet<string>();   // 이번 실행에서 이미 설치한 부품 (같은 것 반복 안 함)
        public static volatile bool Busy;

        static readonly Regex SysErr = new Regex(@"^\s*(.+?\.exe)\s*-\s*", RegexOptions.IgnoreCase);

        /// <summary>"어떤.exe - 시스템 오류" 창이 이 게임 폴더의 프로그램 것인지 (게임이 다른 exe를 켜는 경우까지)</summary>
        public static bool IsGameSysErr(string title, string dir)
        {
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(dir)) return false;
            var m = SysErr.Match(title);
            if (!m.Success) return false;
            try { return Directory.EnumerateFiles(dir, Path.GetFileName(m.Groups[1].Value), SearchOption.AllDirectories).Any(); }
            catch { return false; }
        }

        /// <summary>윈도우 이벤트 기록에서 이 게임 폴더 프로그램이 튕긴 원인 (튕긴 모듈·코드) — 오류 창 없이 꺼질 때</summary>
        public static string CrashInfo(string dir, DateTime since)
        {
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { foreach (var f in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories).Take(200)) names.Add(Path.GetFileName(f)); } catch { }
                using (var log = new EventLog("Application"))
                {
                    var ents = log.Entries;
                    for (int i = ents.Count - 1, n = 0; i >= 0 && n < 300; i--, n++)
                    {
                        var e = ents[i];
                        if (e.TimeGenerated < since.AddSeconds(-5)) break;
                        if (e.InstanceId != 1000 && e.InstanceId != 1002 && e.InstanceId != 1026) continue;
                        var r = e.ReplacementStrings;
                        if (r == null || r.Length < 1 || !names.Contains(r[0])) continue;
                        string mod = r.Length > 3 ? r[3] : "", code = r.Length > 6 ? r[6] : "";
                        string txt = e.InstanceId == 1002 ? r[0] + " 응답 없음으로 끝남" : r[0] + " 튕김 · 원인 모듈 " + mod + (code.Length > 0 ? " · 코드 " + code : "");
                        Log.Info("튕김 기록: " + txt);
                        return txt;
                    }
                }
            }
            catch (Exception ex) { Log.Info("튕김 기록 읽기 실패: " + ex.Message); }
            return "";
        }

        /// <summary>이 글(오류 창 글·튕김 기록)로 스스로 고칠 수 있는 것들</summary>
        public static List<GameDoctor.Issue> Fixable(GameInfo g, string text)
        {
            var list = new List<GameDoctor.Issue>();
            string low = (text ?? "").ToLowerInvariant();
            if (low.Length == 0) return list;
            try { var d = GameDoctor.DxIssue(low); if (d != null) list.Add(d); } catch { }
            try { var v = GameDoctor.VcIssue(g, low); if (v != null) list.Add(v); } catch { }
            return list;
        }

        public static bool CanFix(GameInfo g, string text) => Fixable(g, text).Any(i => i.Fix != null && !tried.Contains(i.Title));

        /// <summary>고칠 수 있는 건 바로 고침 (이 스레드에서, 오래 걸릴 수 있음). 고친 게 있으면 결과 글, 없으면 null</summary>
        public static string FixNow(GameInfo g, string text)
        {
            var issues = Fixable(g, text).Where(i => i.Fix != null && !tried.Contains(i.Title)).ToList();
            if (issues.Count == 0) return null;
            Busy = true;
            var res = new List<string>();
            try
            {
                foreach (var i in issues)
                {
                    lock (tried) tried.Add(i.Title);
                    Activity.Set("fix", "오류 고치는 중 · " + i.Title, 0, 0);
                    string r;
                    try { r = i.Fix(); } catch (Exception ex) { r = ex.Message; }
                    res.Add(r);
                    Activity.Note("오류 고침 · " + (g != null ? g.Title + " · " : "") + i.Title + " → " + r);
                }
            }
            finally { Busy = false; Activity.Done("fix", "오류 고치기 끝 · " + string.Join(", ", res)); }
            return string.Join(" / ", res);
        }

        /// <summary>뒤에서 고치고 끝나면 알려 줌</summary>
        public static void FixLater(GameInfo g, string text, Action<string> after)
        {
            if (!CanFix(g, text)) { after?.Invoke(null); return; }
            new Thread(() => { string r = null; try { r = FixNow(g, text); } catch { } after?.Invoke(r); }) { IsBackground = true }.Start();
        }

        /// <summary>고칠 방법을 모르는 오류는 기록만 (런처가 한 일 + 로그) — 사용자가 보여주면 고칠 수 있게</summary>
        public static void NoteUnknown(GameInfo g, string text)
        {
            text = (text ?? "").Replace("\r", "").Replace("\n", " / ").Trim();
            if (text.Length == 0) return;
            Activity.Note("모르는 오류 · " + (g != null ? g.Title : "") + " — " + text);
            Log.Info("모르는 오류: " + (g != null ? g.Title + " (" + g.Folder + ")" : "") + " — " + text);
        }
    }
}
