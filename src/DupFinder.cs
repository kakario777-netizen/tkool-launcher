using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TkoolLauncher
{
    /// <summary>
    /// 쯔꾸르 폴더 안 중복 게임 찾기. 같은 게임으로 보는 단서:
    ///  - 정리한 이름이 같음
    ///  - 게임 데이터 파일(rgss 묶음·System.json·RPG_RT.ldb)의 크기+내용 일부가 같음
    ///  - 타이틀 그림이 거의 같음 (8×8 평균 해시 차이 4 이하)
    /// 버전은 이름의 ver/v 숫자, 없으면 데이터 파일 날짜.
    /// </summary>
    static class DupFinder
    {
        public class Group { public List<GameInfo> Games = new List<GameInfo>(); public List<string> Why = new List<string>(); }
        public static List<Group> Groups = new List<Group>();
        static volatile bool busy;

        public static void Scan(List<GameInfo> all, Action done)
        {
            if (busy) return;
            busy = true;
            var games = all.Where(g => g.FromFolder).ToList();
            var th = new Thread(() =>
            {
                try { Groups = Find(games); }
                catch (Exception ex) { Log.Error("중복 찾기", ex); }
                busy = false;
                try { done?.Invoke(); } catch { }
            }) { IsBackground = true, Priority = ThreadPriority.Lowest };
            th.Start();
        }

        static List<Group> Find(List<GameInfo> games)
        {
            int n = games.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            var why = new Dictionary<int, HashSet<string>>();
            Func<int, int> root = null;
            root = i => parent[i] == i ? i : (parent[i] = root(parent[i]));
            var ignore = new HashSet<string>((Settings.DupIgnore ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            Action<int, int, string> join = (a, b, reason) =>
            {
                if (ignore.Contains(PairKey(games[a], games[b]))) return;
                int ra = root(a), rb = root(b);
                if (ra != rb) parent[ra] = rb;
                int r = root(a);
                if (!why.TryGetValue(r, out var w)) why[r] = w = new HashSet<string>();
                w.Add(reason);
            };

            // 1) 이름
            var byName = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                if (GameCapture.IsGenericTitle(games[i], games[i].Title)) continue;
                string k = NameKey(TitleTidy.Clean(games[i].RawTitle ?? games[i].Title, new List<string>()));
                if (k.Length < 2) continue;
                if (byName.TryGetValue(k, out int j)) join(j, i, "이름이 같아요"); else byName[k] = i;
            }
            // 2) 게임 데이터
            var byData = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                string k = DataKey(games[i]);
                if (k == null) continue;
                if (byData.TryGetValue(k, out int j)) join(j, i, "게임 데이터가 같아요"); else byData[k] = i;
            }
            // 3) 타이틀 그림
            var hashes = new List<KeyValuePair<int, ulong>>();
            for (int i = 0; i < n; i++) { var h = CoverArt.ThumbHash(games[i]); if (h != 0) hashes.Add(new KeyValuePair<int, ulong>(i, h)); }
            for (int a = 0; a < hashes.Count; a++)
                for (int b = a + 1; b < hashes.Count; b++)
                    if (Bits(hashes[a].Value ^ hashes[b].Value) <= 4) join(hashes[a].Key, hashes[b].Key, "타이틀 그림이 비슷해요");

            var groups = new Dictionary<int, Group>();
            for (int i = 0; i < n; i++)
            {
                int r = root(i);
                if (!groups.TryGetValue(r, out var gr)) groups[r] = gr = new Group();
                gr.Games.Add(games[i]);
            }
            var res = new List<Group>();
            foreach (var kv in groups)
            {
                if (kv.Value.Games.Count < 2) continue;
                if (why.TryGetValue(kv.Key, out var w)) kv.Value.Why = w.ToList();
                kv.Value.Games = kv.Value.Games.OrderByDescending(VersionSortKey).ToList();
                res.Add(kv.Value);
            }
            return res.OrderBy(g => g.Games[0].Title, StringComparer.CurrentCulture).ToList();
        }

        public static string PairKey(GameInfo a, GameInfo b)
        {
            string x = GameData.IdOf(a), y = GameData.IdOf(b);
            return string.CompareOrdinal(x, y) < 0 ? x + "+" + y : y + "+" + x;
        }

        /// <summary>이 묶음은 중복이 아님 → 다음부터 안 묶음</summary>
        public static void IgnoreGroup(Group g)
        {
            var set = new HashSet<string>((Settings.DupIgnore ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            for (int a = 0; a < g.Games.Count; a++)
                for (int b = a + 1; b < g.Games.Count; b++) set.Add(PairKey(g.Games[a], g.Games[b]));
            Settings.DupIgnore = string.Join(",", set);
            Settings.Save();
            Groups.Remove(g);
        }

        static string NameKey(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        static int Bits(ulong v) { int c = 0; while (v != 0) { v &= v - 1; c++; } return c; }

        /// <summary>게임 데이터 대표 파일</summary>
        public static string DataFile(GameInfo g)
        {
            string d = g.Folder;
            if (d == null) return null;
            string[] cands;
            switch (g.IsTkool ? g.Version : (TkoolVersion)(-1))
            {
                case TkoolVersion.RM2000: cands = new[] { "RPG_RT.ldb" }; break;
                case TkoolVersion.XP: cands = new[] { "Game.rgssad", @"Data\System.rxdata" }; break;
                case TkoolVersion.VX: cands = new[] { "Game.rgss2a", @"Data\System.rvdata" }; break;
                case TkoolVersion.VXAce: cands = new[] { "Game.rgss3a", @"Data\System.rvdata2" }; break;
                case TkoolVersion.MV: cands = new[] { @"www\data\System.json", @"data\System.json" }; break;
                case TkoolVersion.MZ: cands = new[] { @"data\System.json", @"www\data\System.json" }; break;
                default: return g.Exe;
            }
            foreach (var c in cands) { string p = Path.Combine(d, c); if (File.Exists(p)) return p; }
            return null;
        }

        static string DataKey(GameInfo g)
        {
            try
            {
                string f = DataFile(g);
                if (f == null) return null;
                var fi = new FileInfo(f);
                if (fi.Length < 512) return null;
                // 앞뒤 64KB만 읽어 빠르게
                using (var fs = File.OpenRead(f))
                {
                    var buf = new byte[65536];
                    int r1 = fs.Read(buf, 0, buf.Length);
                    ulong h = 14695981039346656037UL;
                    for (int i = 0; i < r1; i++) { h ^= buf[i]; h *= 1099511628211UL; }
                    if (fs.Length > 131072) { fs.Seek(-65536, SeekOrigin.End); int r2 = fs.Read(buf, 0, buf.Length); for (int i = 0; i < r2; i++) { h ^= buf[i]; h *= 1099511628211UL; } }
                    return Path.GetFileName(f).ToLowerInvariant() + "|" + fi.Length + "|" + h.ToString("x");
                }
            }
            catch { return null; }
        }

        static readonly Regex Ver = new Regex(@"(?i)(?:ver(?:sion)?\.?\s*|v)(\d+(?:\.\d+)*)|(?:^|[\s_\-])(\d+\.\d+(?:\.\d+)*)(?=$|[\s_\-])");

        /// <summary>버전 글: 이름의 ver 숫자, 없으면 데이터 파일 날짜</summary>
        public static string VersionText(GameInfo g)
        {
            foreach (var src in new[] { g.RawTitle, Path.GetFileName(g.Folder ?? "") })
            {
                if (string.IsNullOrEmpty(src)) continue;
                var m = Ver.Match(src);
                if (m.Success) return "v" + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            }
            var d = DataDate(g);
            return d > DateTime.MinValue ? d.ToString("yyyy.MM.dd") + " 파일" : "버전 모름";
        }

        public static DateTime DataDate(GameInfo g)
        {
            try { string f = DataFile(g); return f != null ? File.GetLastWriteTime(f) : DateTime.MinValue; } catch { return DateTime.MinValue; }
        }

        static string VersionSortKey(GameInfo g)
        {
            var v = VersionText(g);
            if (v.StartsWith("v")) return "1" + string.Join(".", v.Substring(1).Split('.').Select(x => x.PadLeft(6, '0')));
            return "0" + DataDate(g).ToString("yyyyMMddHHmm");
        }

        public static long FolderSize(GameInfo g)
        {
            try { return Directory.EnumerateFiles(g.Folder, "*", SearchOption.AllDirectories).Take(20000).Sum(f => new FileInfo(f).Length); }
            catch { return 0; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags; [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle; }
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        /// <summary>게임 폴더를 휴지통으로 (되살릴 수 있게). 성공하면 true</summary>
        public static bool DeleteToRecycle(GameInfo g, out string err)
        {
            err = null;
            try
            {
                string dir = g.Folder;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { err = "폴더가 없어요"; return false; }
                // 설정한 게임 폴더 자체나 드라이브 통째는 절대 안 지움
                foreach (var root in Settings.GameFolders)
                    if (string.Equals(Path.GetFullPath(root).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { err = "게임 폴더 전체는 지울 수 없어요"; return false; }
                if (Path.GetPathRoot(dir).TrimEnd('\\').Length >= dir.TrimEnd('\\').Length) { err = "드라이브는 지울 수 없어요"; return false; }
                var op = new SHFILEOPSTRUCT { wFunc = 3 /*FO_DELETE*/, pFrom = Path.GetFullPath(dir).TrimEnd('\\') + "\0\0", fFlags = 0x40 | 0x10 | 0x4 | 0x400 /*ALLOWUNDO|NOCONFIRM|SILENT|NOERRORUI*/ };
                int rc = SHFileOperation(ref op);
                if (rc != 0 || op.fAnyOperationsAborted || Directory.Exists(dir)) { err = "휴지통으로 옮기지 못했어요 (코드 " + rc + ")"; return false; }
                Log.Info("중복 게임 폴더를 휴지통으로: " + dir);
                foreach (var gr in Groups) gr.Games.Remove(g);
                Groups.RemoveAll(x => x.Games.Count < 2);
                return true;
            }
            catch (Exception ex) { Log.Error("게임 폴더 지우기", ex); err = ex.Message; return false; }
        }

        /// <summary>탐색기에서 게임 폴더를 골라 둔 채로 열기</summary>
        public static void OpenFolder(GameInfo g)
        {
            try
            {
                string target = g.Folder ?? Path.GetDirectoryName(g.Exe);
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"") { UseShellExecute = true });
                Log.Info("게임 위치 열기: " + target);
            }
            catch (Exception ex) { Log.Error("게임 위치 열기", ex); }
        }
    }
}
