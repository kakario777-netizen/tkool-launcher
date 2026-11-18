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
            if (summary != null) Note(summary);
            try { Changed?.Invoke(); } catch { }
        }

        // ── 지난 일 기록 (아래 왼쪽을 누르면 목록) ──
        static readonly List<string> history = new List<string>();
        public static void Note(string text)
        {
            lock (history)
            {
                history.Insert(0, DateTime.Now.ToString("HH:mm") + "  " + text);
                if (history.Count > 40) history.RemoveAt(history.Count - 1);
            }
            try { Changed?.Invoke(); } catch { }
        }
        public static List<string> History() { lock (history) return history.ToList(); }

        public static bool Any { get { lock (jobs) return jobs.Count > 0; } }

        /// <summary>보일 줄 (가장 먼저 시작한 일 + 나머지 개수), 진행 0~1 (모르면 -1)</summary>
        public static string Line(out float progress)
        {
            progress = -1;
            lock (jobs)
            {
                if (order.Count == 0)
                {
                    lock (history) return history.Count > 0 ? "✓ " + history[0].Substring(7) + "  · 할 일 없음" : "✓ 할 일 없음";
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
