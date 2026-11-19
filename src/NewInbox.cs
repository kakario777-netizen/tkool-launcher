using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TkoolLauncher
{
    /// <summary>새로 들어온 게임(노란 칸): 폴더는 안 옮기고 런처가 목록만 기억, "확인 끝" 전까지 며칠이 지나도 그대로</summary>
    static class NewInbox
    {
        static readonly string Dir = Path.Combine(Settings.Dir, "cache");
        static readonly string KnownFile = Path.Combine(Dir, "known_dirs.txt");
        static readonly string NewFile = Path.Combine(Dir, "new_games.txt");
        static HashSet<string> known, fresh;
        static readonly object lk = new object();

        static string Key(string folder) => (folder ?? "").TrimEnd('\\').ToLowerInvariant();

        static void Load()
        {
            if (fresh != null) return;
            fresh = new HashSet<string>(); known = null;
            try { if (File.Exists(NewFile)) foreach (var l in File.ReadAllLines(NewFile)) if (l.Trim() != "") fresh.Add(l.Trim()); } catch { }
            try { if (File.Exists(KnownFile)) known = new HashSet<string>(File.ReadAllLines(KnownFile).Where(l => l.Trim() != "").Select(l => l.Trim())); } catch { }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(NewFile, fresh.ToArray());
                if (known != null) File.WriteAllLines(KnownFile, known.ToArray());
            }
            catch { }
        }

        public static int Count { get { lock (lk) { Load(); return fresh.Count; } } }

        public static bool IsNew(GameInfo g)
        {
            if (g == null || !g.FromFolder) return false;
            lock (lk) { Load(); return fresh.Count > 0 && fresh.Contains(Key(g.Folder)); }
        }

        /// <summary>진짜 폴더 검사가 끝난 뒤: 처음 보는 게임 폴더를 노란 칸에. 처음 쓸 때는 지금 있는 것 전부를 "원래 있던 것"으로</summary>
        public static int Update(List<GameInfo> found)
        {
            lock (lk)
            {
                Load();
                var now = found.Where(g => g.FromFolder && !string.IsNullOrEmpty(g.Folder)).ToList();
                var keys = new HashSet<string>(now.Select(g => Key(g.Folder)));
                int added = 0;
                if (known == null) known = new HashSet<string>(keys);
                else
                {
                    foreach (var g in now)
                    {
                        string k = Key(g.Folder);
                        if (known.Contains(k)) continue;
                        known.Add(k);
                        var st = GameData.Get(g);
                        if (st.TotalSeconds > 0 || st.LastPlayed > 0) continue;   // 폴더만 옮긴 예전 게임(기록 있음)은 새 게임 아님
                        if (fresh.Add(k)) added++;
                    }
                }
                fresh.RemoveWhere(k => !keys.Contains(k));   // 지웠거나 옮긴 것
                Save();
                if (added > 0) Log.Info("새로 들어온 게임 " + added + "개");
                return added;
            }
        }

        public static void Clear() { lock (lk) { Load(); fresh.Clear(); Save(); } }
        public static void Remove(GameInfo g) { lock (lk) { Load(); if (fresh.Remove(Key(g.Folder))) Save(); } }

        /// <summary>옮길 폴더: 게임 폴더에서 위로 올라가며, 그 안에 다른 라이브러리 게임이 없는 가장 위 폴더 (설정한 게임 폴더 바로 아래까지)</summary>
        static string TopFolder(GameInfo g, IEnumerable<GameInfo> lib, out string root)
        {
            string dir = Path.GetFullPath(g.Folder).TrimEnd('\\');
            root = Settings.GameFolders.Select(r => Path.GetFullPath(r).TrimEnd('\\'))
                .Where(r => dir.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Length).FirstOrDefault();
            if (root == null) return null;
            var others = lib.Where(x => x != g && x.FromFolder && !string.IsNullOrEmpty(x.Folder)).Select(x => Path.GetFullPath(x.Folder).TrimEnd('\\') + "\\").ToList();
            string cur = dir;
            while (true)
            {
                string up = Path.GetDirectoryName(cur);
                if (up == null || up.Length <= root.Length) break;
                string upS = up + "\\";
                if (others.Any(o => o.StartsWith(upS, StringComparison.OrdinalIgnoreCase))) break;
                cur = up;
            }
            return cur;
        }

        /// <summary>옛 버전을 게임 폴더\_old 로 옮김(지우지 않음). copySaves면 세이브를 새 버전으로 복사(새 버전에 같은 파일이 있으면 _save_backup에 남겨 둠)</summary>
        public static bool MoveToOld(GameInfo old, GameInfo newer, bool copySaves, IEnumerable<GameInfo> lib, out string msg)
        {
            msg = null;
            try
            {
                string root;
                string top = TopFolder(old, lib, out root);
                if (top == null) { msg = "설정한 게임 폴더 안의 게임이 아니에요"; return false; }
                if (newer != null && Path.GetFullPath(newer.Folder).StartsWith(top + "\\", StringComparison.OrdinalIgnoreCase)) { msg = "새 버전이 옛 버전 폴더 안에 있어요"; return false; }
                int copied = 0;
                if (copySaves && newer != null)
                {
                    string src = Path.GetFullPath(old.Folder).TrimEnd('\\'), dst = Path.GetFullPath(newer.Folder).TrimEnd('\\');
                    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Take(30000))
                    {
                        if (!DupFinder.IsSaveFile(Path.GetFileName(f))) continue;
                        string rel = f.Substring(src.Length + 1);
                        string to = Path.Combine(dst, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        if (File.Exists(to))
                        {
                            string bk = Path.Combine(dst, "_save_backup", rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(bk));
                            File.Copy(to, bk, true);
                        }
                        File.Copy(f, to, true);
                        copied++;
                    }
                }
                string oldDir = Path.Combine(root, "_old");
                Directory.CreateDirectory(oldDir);
                string dest = Path.Combine(oldDir, Path.GetFileName(top));
                for (int i = 2; Directory.Exists(dest) || File.Exists(dest); i++) dest = Path.Combine(oldDir, Path.GetFileName(top) + " (" + i + ")");
                Directory.Move(top, dest);
                Log.Info("옛 버전 옮김: " + top + " → " + dest + (copySaves ? " (세이브 " + copied + "개 복사)" : ""));
                foreach (var gr in DupFinder.Groups) gr.Games.Remove(old);
                DupFinder.Groups.RemoveAll(x => x.Games.Count < 2);
                msg = "옛 버전을 _old 폴더로 옮겼어요" + (copySaves ? " · 세이브 " + copied + "개 복사" : "");
                return true;
            }
            catch (Exception ex) { Log.Error("옛 버전 옮기기", ex); msg = "옮기지 못했어요: " + ex.Message + " (게임이 켜져 있거나 다른 드라이브일 수 있어요)"; return false; }
        }
    }
}
