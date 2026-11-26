using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace TkoolLauncher
{
    /// <summary>
    /// 화면(UI) 스레드가 멈추지 않게: 디스크를 오래 뒤지는 일(게임 폴더 속 파일 살피기 등)을 화면 그리다 처음 만나면
    /// 바로 "아직 모름"으로 돌려주고 뒤에서 계산 → 다 되면 화면을 한 번 새로 그림.
    /// 그리고 화면이 8초 넘게 멈추면 어디서 멈췄는지 기록(UiGuard).
    /// </summary>
    static class UiThread
    {
        static int uiId = -1;
        public static Action Refresh;   // 뒤에서 계산이 끝났을 때 (ShellForm이 목록 다시 그리기로 연결)
        public static bool IsUi => uiId >= 0 && slow == 0 && Thread.CurrentThread.ManagedThreadId == uiId;
        static int slow;
        /// <summary>게임 켤 때처럼 꼭 지금 답이 필요하면 이 안에서 (화면 스레드여도 바로 계산)</summary>
        public static void Now(Action a) { slow++; try { a(); } finally { slow--; } }
        static Thread ui;

        static readonly Queue<KeyValuePair<string, Action>> q = new Queue<KeyValuePair<string, Action>>();
        static readonly HashSet<string> queued = new HashSet<string>();
        static Thread worker;
        static DateTime lastRefresh;
        static volatile bool dirty;

        public static void Init()
        {
            uiId = Thread.CurrentThread.ManagedThreadId; ui = Thread.CurrentThread;
            StartGuard();
        }

        /// <summary>같은 key는 한 번만 줄 세움</summary>
        public static void Later(string key, Action a)
        {
            lock (q)
            {
                if (!queued.Add(key)) return;
                q.Enqueue(new KeyValuePair<string, Action>(key, a));
                if (worker == null)
                {
                    worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "ui-later" };
                    worker.Start();
                }
                Monitor.Pulse(q);
            }
        }

        static void Work()
        {
            while (true)
            {
                KeyValuePair<string, Action> it;
                lock (q)
                {
                    while (q.Count == 0)
                    {
                        if (dirty) { dirty = false; Fire(); }
                        Monitor.Wait(q);
                    }
                    it = q.Dequeue();
                }
                try { it.Value(); } catch { }
                lock (q) queued.Remove(it.Key);
                dirty = true;
                if ((DateTime.Now - lastRefresh).TotalSeconds > 2) { dirty = false; Fire(); }
            }
        }

        static void Fire()
        {
            lastRefresh = DateTime.Now;
            try { Refresh?.Invoke(); } catch { }
        }

        // ───── 화면 멈춤 지켜보기 ─────
        static long lastPong = DateTime.Now.Ticks;
        public static Action<Action> Post;   // ShellForm.BeginInvoke

        static void StartGuard()
        {
            var t = new Thread(() =>
            {
                bool logged = false;
                while (true)
                {
                    Thread.Sleep(1000);
                    try { Post?.Invoke(() => Interlocked.Exchange(ref lastPong, DateTime.Now.Ticks)); } catch { }
                    double stall = (DateTime.Now - new DateTime(Interlocked.Read(ref lastPong))).TotalSeconds;
                    if (stall < 3) { if (logged) Log.Info("화면 멈춤 풀림"); logged = false; continue; }
                    if (stall > 8 && !logged)
                    {
                        logged = true;
                        Log.Info("화면이 " + (int)stall + "초 멈춤 — 어디서: " + UiStack());
                    }
                }
            }) { IsBackground = true, Name = "ui-guard" };
            t.Start();
        }

        static string UiStack()
        {
#pragma warning disable 618
            bool suspended = false;
            try
            {
                ui.Suspend(); suspended = true;
                var st = new StackTrace(ui, false);
                ui.Resume(); suspended = false;
                var l = new List<string>();
                foreach (var f in st.GetFrames() ?? new StackFrame[0])
                {
                    var m = f.GetMethod();
                    if (m == null || m.DeclaringType == null) continue;
                    string n = m.DeclaringType.Name + "." + m.Name;
                    if (m.DeclaringType.Namespace != null && m.DeclaringType.Namespace.StartsWith("TkoolLauncher")) l.Add(n);
                    else if (l.Count == 0) l.Add(n);
                    if (l.Count >= 10) break;
                }
                return string.Join(" ← ", l);
            }
            catch (Exception ex) { return "(못 읽음: " + ex.Message + ")"; }
            finally { if (suspended) try { ui.Resume(); } catch { } }
#pragma warning restore 618
        }
    }
}
