using System;
using System.Collections.Generic;
using System.Linq;

namespace TkoolLauncher
{
    /// <summary>
    /// 런처가 뒤에서 하는 일 (아래 줄 왼쪽에 보임): 게임 찾기·이름 확인·DLsite·중복 검사·새 게임 기다림 등.
    /// 어느 스레드에서나 Set/Done 불러도 됨.
    /// </summary>
    static class Activity
    {
        class Job { public string Text; public int Cur, Total; public DateTime Started; }
        static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        static readonly List<string> order = new List<string>();
        public static Action Changed;

        public static void Set(string key, string text, int cur = 0, int total = 0)
        {
            lock (jobs)
            {
                Job j;
                if (!jobs.TryGetValue(key, out j)) { jobs[key] = j = new Job { Started = DateTime.Now }; order.Add(key); }
                j.Text = text; j.Cur = cur; j.Total = total;
            }
            try { Changed?.Invoke(); } catch { }
        }

        public static void Done(string key, string summary = null)
        {
            lock (jobs) { if (jobs.Remove(key)) order.Remove(key); }
            List<string> d;
            lock (details) { if (details.TryGetValue(key, out d)) details.Remove(key); }
            if (summary != null) Note(summary, d != null && d.Count > 0 ? string.Join("\n", d) : null);
            try { Changed?.Invoke(); } catch { }
        }

        // ── 일마다 자세한 내용 모으기 (끝날 때 기록에 붙음 → 기록을 누르면 보임) ──
        static readonly Dictionary<string, List<string>> details = new Dictionary<string, List<string>>();
        public static void Detail(string key, string line)
        {
            lock (details)
            {
                List<string> l;
                if (!details.TryGetValue(key, out l)) details[key] = l = new List<string>();
                if (l.Count < 2000) l.Add(line);
            }
        }

        // ── 지난 일 기록 (아래 왼쪽을 누르면 목록, 누르면 자세히) — 게임은 계속 늘어나니 파일에 남김(logs\activity.txt, 최근 300개) ──
        public class Entry { public DateTime Time; public string Text, Detail; }
        static List<Entry> history;
        static string FilePath => System.IO.Path.Combine(Settings.Dir, "logs", "activity.txt");
        static string Esc(string v) => (v ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "");
        static string Unesc(string v)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length) { char c = v[++i]; sb.Append(c == 't' ? '\t' : c == 'n' ? '\n' : c); }
                else sb.Append(v[i]);
            }
            return sb.ToString();
        }
        static List<Entry> H()
        {
            if (history != null) return history;
            history = new List<Entry>();
            try
            {
                if (System.IO.File.Exists(FilePath))
                    foreach (var l in System.IO.File.ReadAllLines(FilePath, System.Text.Encoding.UTF8))
                    {
                        var c = l.Split('\t');
                        if (c.Length < 2) continue;
                        DateTime t; if (!DateTime.TryParse(c[0], out t)) continue;
                        history.Add(new Entry { Time = t, Text = Unesc(c[1]), Detail = c.Length > 2 && c[2].Length > 0 ? Unesc(c[2]) : null });
                    }
            }
            catch { }
            return history;
        }
        static void SaveFile()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                System.IO.File.WriteAllLines(FilePath, H().Select(e => e.Time.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + Esc(e.Text) + "\t" + Esc(e.Detail)).ToArray(), System.Text.Encoding.UTF8);
            }
            catch { }
        }

        public static void Note(string text, string detail = null)
        {
            lock (typeof(Activity))
            {
                H().Insert(0, new Entry { Time = DateTime.Now, Text = text, Detail = detail });
                while (history.Count > 300) history.RemoveAt(history.Count - 1);
                SaveFile();
            }
            try { Changed?.Invoke(); } catch { }
        }
        public static List<Entry> Entries() { lock (typeof(Activity)) return H().ToList(); }
        public static List<string> History() { lock (typeof(Activity)) return H().Select(e => (e.Time.Date == DateTime.Today ? e.Time.ToString("HH:mm") : e.Time.ToString("M/d HH:mm")) + "  " + e.Text + (e.Detail != null ? "  ›" : "")).ToList(); }

        public static bool Any { get { lock (jobs) return jobs.Count > 0; } }

        /// <summary>보일 줄 (가장 먼저 시작한 일 + 나머지 개수), 진행 0~1 (모르면 -1)</summary>
        public static string Line(out float progress)
        {
            progress = -1;
            lock (jobs)
            {
                if (order.Count == 0)
                {
                    lock (typeof(Activity)) return H().Count > 0 ? "✓ " + H()[0].Text + "  · 할 일 없음" : "✓ 할 일 없음";
                }
                var j = jobs[order[0]];
                string s = j.Text;
                if (j.Total > 0) { s += "  " + j.Cur + "/" + j.Total; progress = Math.Min(1f, j.Cur / (float)j.Total); }
                if (order.Count > 1) s += "  (+" + (order.Count - 1) + "개 더: " + string.Join(", ", order.Skip(1).Select(k => jobs[k].Text.Split(' ')[0]).Take(2)) + ")";
                return s;
            }
        }
    }
}
