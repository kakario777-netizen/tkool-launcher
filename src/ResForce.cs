using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임별 해상도 강제 (GameStat.Res, 예: "1280×720").
    /// 런처로 켜면 켜기 전에 바꾸고, 런처 밖(배틀넷·스팀 등)에서 켜도 앞 창이 그 게임 폴더면 바꿈.
    /// 게임 폴더 프로그램이 다 꺼지면 원래 해상도로 되돌림.
    /// </summary>
    static class ResForce
    {
        static string original;      // 바꾸기 전 해상도
        static string activeDir;     // 지금 해상도를 바꾼 게임 폴더
        static Timer timer;
        static bool busy;

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        public static string CurrentRes()
        {
            try { var l = SystemControls.Resolutions(out int i); return i >= 0 && i < l.Count ? l[i] : null; }
            catch { return null; }
        }

        /// <summary>이 게임에 정해 둔 해상도로 바꿈 (이미 바꾼 상태면 그대로)</summary>
        public static void Apply(GameInfo g)
        {
            var st = GameData.Get(g);
            if (string.IsNullOrEmpty(st.Res)) return;
            string dir = g.InstallDir ?? g.Folder;
            string cur = CurrentRes();
            if (cur == st.Res) { if (activeDir == null && original == null) return; activeDir = dir; return; }
            if (original == null) original = cur;
            activeDir = dir;
            Log.Info("해상도 강제: " + g.Title + " → " + st.Res + " (원래 " + original + ")");
            try { SystemControls.SetResolution(st.Res); } catch (Exception ex) { Log.Error("해상도 강제", ex); }
        }

        /// <summary>원래 해상도로 되돌림</summary>
        public static void Restore()
        {
            if (original == null) { activeDir = null; return; }
            Log.Info("해상도 되돌림 → " + original);
            try { SystemControls.SetResolution(original); } catch (Exception ex) { Log.Error("해상도 되돌림", ex); }
            original = null; activeDir = null;
        }

        /// <summary>런처 밖에서 켠 게임도 잡기 위해 3초마다 앞 창 확인</summary>
        public static void Start(Func<IEnumerable<GameInfo>> games)
        {
            if (timer != null) return;
            timer = new Timer { Interval = 3000 };
            timer.Tick += (s, e) =>
            {
                if (busy) return;
                var list = games().Where(x => !string.IsNullOrEmpty(GameData.Get(x).Res)).ToList();
                if (list.Count == 0 && activeDir == null) return;
                busy = true;
                IntPtr fg = GetForegroundWindow();
                GetWindowThreadProcessId(fg, out uint pid);
                string dir = activeDir;
                Task.Run(() =>
                {
                    GameInfo hit = null;
                    bool alive = true;
                    try
                    {
                        if (dir == null) hit = list.FirstOrDefault(x => GameWatch.InDir((int)pid, x.InstallDir ?? x.Folder));
                        else alive = GameWatch.AnyInDir(dir);
                    }
                    catch { }
                    try
                    {
                        timer.Tag = null;
                        Application.OpenForms[0]?.BeginInvoke(new Action(() =>
                        {
                            if (hit != null) Apply(hit);
                            else if (dir != null && !alive && activeDir == dir) Restore();
                            busy = false;
                        }));
                    }
                    catch { busy = false; }
                });
            };
            timer.Start();
        }
    }
}
