using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TkoolLauncher
{
    /// <summary>
    /// 시리즈일 수도 있는 폴더 찾기: "[시리즈]"가 안 붙은 폴더 안에 게임이 2~8개 있으면 알림.
    /// 알린 폴더는 cache\series_seen.txt, "시리즈 아님"은 cache\series_ignore.txt
    /// </summary>
    static class SeriesHint
    {
        public class Cand { public string Dir; public List<GameInfo> Games; public string Name => Path.GetFileName(Dir); }

        static string SeenPath => Path.Combine(Settings.Dir, "cache", "series_seen.txt");
        static string IgnorePath => Path.Combine(Settings.Dir, "cache", "series_ignore.txt");
        static HashSet<string> Load(string p)
        {
            try { if (File.Exists(p)) return new HashSet<string>(File.ReadAllLines(p).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase); } catch { }
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        static void Add(string p, string dir)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(p)); File.AppendAllText(p, dir + "\r\n"); } catch { }
        }

        public static List<Cand> Current = new List<Cand>();

        /// <summary>게임 폴더(2중 폴더면 위 폴더)</summary>
        static string GameDir(GameInfo g)
        {
            string d = (g.Folder ?? "").TrimEnd('\\', '/');
            string want = GameScanner.FolderName(d);
            for (int i = 0; i < 3 && !string.IsNullOrEmpty(d) && Path.GetFileName(d) != want; i++) d = Path.GetDirectoryName(d);
            return d;
        }

        public static List<Cand> Find(IEnumerable<GameInfo> games)
        {
            var ign = Load(IgnorePath);
            var roots = new HashSet<string>(Settings.GameFolders.Select(x => x.TrimEnd('\\', '/')), StringComparer.OrdinalIgnoreCase);
            var by = new Dictionary<string, List<GameInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in games)
            {
                if (!g.FromFolder || g.Folder == null || GameScanner.Series(g.Folder) != null) continue;
                try
                {
                    string gd = GameDir(g);
                    string p = Path.GetDirectoryName(gd);
                    if (string.IsNullOrEmpty(p) || Path.GetPathRoot(p) == p) continue;
                    if (roots.Contains(p) || roots.Any(r => r.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase))) continue;   // 게임 폴더 자체·그 위
                    string n = Path.GetFileName(p);
                    if (n.StartsWith("_") || n.StartsWith("#")) continue;   // 정리용 분류 폴더
                    List<GameInfo> l;
                    if (!by.TryGetValue(p, out l)) by[p] = l = new List<GameInfo>();
                    if (!l.Contains(g)) l.Add(g);
                }
                catch { }
            }
            var res = by.Where(kv => kv.Value.Count >= 2 && kv.Value.Count <= 8 && !ign.Contains(kv.Key))
                        .Select(kv => new Cand { Dir = kv.Key, Games = kv.Value }).OrderBy(c => c.Name).ToList();
            Current = res;
            return res;
        }

        /// <summary>아직 안 알린 것만 (알렸다고 표시)</summary>
        public static List<Cand> TakeNew()
        {
            var seen = Load(SeenPath);
            var l = Current.Where(c => !seen.Contains(c.Dir)).ToList();
            foreach (var c in l) Add(SeenPath, c.Dir);
            return l;
        }

        public static void Ignore(Cand c) { Add(IgnorePath, c.Dir); Current.Remove(c); }

        public static void OpenFolder(string dir)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); } catch (Exception ex) { Log.Error("폴더 열기", ex); }
        }
    }
}
