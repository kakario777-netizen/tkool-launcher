using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TkoolLauncher
{
    /// <summary>게임별 기록 (즐겨찾기·마지막 플레이·총 플레이 시간) — 내 문서\쯔꾸르 런처\games.ini</summary>
    class GameStat
    {
        public bool Favorite;
        public long LastPlayed;     // DateTime.Ticks (0 = 안 해봄)
        public long TotalSeconds;
        public bool Hidden;
        public string Title;     // 게임 창 제목에서 가져온 진짜 이름 (없으면 null)
        public string Res;       // 이 게임 켤 때 강제할 해상도 "1280×720" (없으면 null)
        public bool Cleared;     // 다 깸 (직접 표시)
        public string Sig;       // 같은 게임 알아보기: 실행 파일 이름|크기|게임 데이터 제목 (폴더를 옮겨도 그대로)
        public string FolderName;// 마지막으로 본 게임 폴더 이름 (표지 파일 이름 옮기기용)
        public int Lang;         // 언어 직접 지정: 0 자동 1 한글 2 일본어
        public string UserTags; // 직접 붙인 태그 (쉼표)
        public string NameFrom;  // 이름을 찾아 바꿨으면 "원래 이름 · 어디서", 찾아봤는데 없으면 "-"
        public int WinMode;      // 창 모양: 0 그대로 1 꽉 찬 창(작업 표시줄 보임)
    }

    static class GameData
    {
        public static List<string> TagsOf(GameInfo g)
        {
            var t = Get(g).UserTags;
            return string.IsNullOrEmpty(t) ? new List<string>() : new List<string>(t.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        }
        static readonly string FilePath = Path.Combine(Settings.Dir, "games.ini");
        static readonly Dictionary<string, GameStat> stats = new Dictionary<string, GameStat>();

        /// <summary>게임 고유 번호: 실행 파일 경로로 만든 짧은 해시</summary>
        public static string IdOf(GameInfo g)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in g.Exe.ToLowerInvariant())
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            return h.ToString("x16");
        }

        public static GameStat Get(GameInfo g)
        {
            string id = IdOf(g);
            if (!stats.TryGetValue(id, out GameStat s))
            {
                s = new GameStat();
                stats[id] = s;
            }
            return s;
        }

        public static string SigOf(GameInfo g)
        {
            long size = 0;
            try { size = new FileInfo(g.Exe).Length; } catch { }
            return (Path.GetFileName(g.Exe) ?? "").ToLowerInvariant() + "|" + size + "|" + (g.Title ?? "").Trim().ToLowerInvariant();
        }

        /// <summary>
        /// 목록을 새로 찾은 뒤: 폴더 이름·위치가 바뀌어 번호가 달라진 게임을 예전 기록(플레이 시간·이름·표지·즐겨찾기…)과 이어 줌.
        /// 실행 파일 이름·크기·게임 데이터 제목이 같고, 그 기록이 지금 목록의 다른 게임 것이 아니면 같은 게임으로 봄.
        /// (꼭 g.Title이 스캔한 원래 제목일 때 — 저장된 이름을 덮어쓰기 전에 부름)
        /// </summary>
        /// <summary>폴더를 옮기거나 이름을 바꿔 예전 기록을 이어 붙인 게임 (새 게임 칸에 안 넣음)</summary>
        public static readonly HashSet<string> Relinked = new HashSet<string>();

        /// <summary>
        /// 게임 id(실행 파일 경로 해시)가 바뀌었을 때 런처가 모아 둔 정보 전부를 새 id로 옮김:
        /// cache 안 파일 이름(표지·썸네일·소개·사이트 정보…)과 목록 파일 안의 id(패드 변환·조작·화면 크기·일본어 환경·직접 표지·찾은 번호…),
        /// 설정(게임별 오버레이 위치·중복 아님), 그리고 이미 읽어 둔 메모리 표까지.
        /// </summary>
        static string Swap(string t, Dictionary<string, string> moves)
        {
            foreach (var kv in moves) if (t.Contains(kv.Key)) t = t.Replace(kv.Key, kv.Value);
            return t;
        }
        static void MoveIdData(Dictionary<string, string> moves)
        {
            int files = 0, texts = 0;
            var latin = Encoding.GetEncoding(28591);   // 바이트 그대로 (어떤 글자표 파일이든 id만 바꿈)
            try
            {
                string cache = Path.Combine(Settings.Dir, "cache");
                if (Directory.Exists(cache))
                    foreach (var f in Directory.GetFiles(cache, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            string name = Path.GetFileName(f);
                            string target = f;
                            string nn = Swap(name, moves);
                            if (nn != name)
                            {
                                target = Path.Combine(Path.GetDirectoryName(f), nn);
                                if (!File.Exists(target)) { File.Move(f, target); files++; } else target = f;
                            }
                            string ext = Path.GetExtension(target).ToLowerInvariant();
                            if ((ext == ".txt" || ext == ".ini") && new FileInfo(target).Length < 8 * 1024 * 1024)
                            {
                                string t = latin.GetString(File.ReadAllBytes(target));
                                string t2 = Swap(t, moves);
                                if (t2 != t) { File.WriteAllBytes(target, latin.GetBytes(t2)); texts++; }
                            }
                        }
                        catch { }
                    }
                string og = Swap(Settings.OvGame, moves), di = Swap(Settings.DupIgnore, moves);
                if (og != Settings.OvGame || di != Settings.DupIgnore) { Settings.OvGame = og; Settings.DupIgnore = di; Settings.Save(); }
                // 이미 읽어 둔 표(사전·집합)의 열쇠도 바꿈 — 다시 켜지 않아도 그대로 보이게
                foreach (var tp in typeof(GameData).Assembly.GetTypes())
                {
                    if (tp == typeof(GameData)) continue;
                    foreach (var fi in tp.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    {
                        try
                        {
                            if (fi.FieldType.IsGenericType && fi.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) && fi.FieldType.GetGenericArguments()[0] == typeof(string))
                            {
                                var d = fi.GetValue(null) as System.Collections.IDictionary;
                                if (d == null) continue;
                                lock (d)
                                {
                                    var ks = new List<string>();
                                    foreach (var k in d.Keys) { var sk = k as string; if (sk != null && Swap(sk, moves) != sk) ks.Add(sk); }
                                    foreach (var k in ks) { string nk = Swap(k, moves); if (!d.Contains(nk)) d[nk] = d[k]; d.Remove(k); }
                                }
                            }
                            else if (fi.FieldType == typeof(HashSet<string>))
                            {
                                var h = fi.GetValue(null) as HashSet<string>;
                                if (h == null) continue;
                                lock (h) foreach (var kv in moves) if (h.Remove(kv.Key)) h.Add(kv.Value);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { Log.Error("게임 정보 옮기기", ex); }
            Log.Info("게임 정보 옮김 " + moves.Count + "개 (파일 " + files + " · 목록 " + texts + ")");
        }

        public static void Reconcile(List<GameInfo> games)
        {
            try
            {
                var used = new HashSet<string>();
                foreach (var g in games) used.Add(IdOf(g));
                var bySig = new Dictionary<string, List<string>>();
                foreach (var kv in stats)
                    if (kv.Value.Sig != null && !used.Contains(kv.Key))
                    {
                        if (!bySig.TryGetValue(kv.Value.Sig, out var l)) bySig[kv.Value.Sig] = l = new List<string>();
                        l.Add(kv.Key);
                    }
                bool changed = false;
                var moves = new Dictionary<string, string>();
                foreach (var g in games)
                {
                    string id = IdOf(g);
                    string sig = SigOf(g);
                    string folder = Path.GetFileName((g.Folder ?? "").TrimEnd('\\'));
                    if (stats.TryGetValue(id, out var cur))
                    {
                        if (cur.Sig != sig || cur.FolderName != folder) { cur.Sig = sig; cur.FolderName = folder; changed = true; }
                        continue;
                    }
                    if (!bySig.TryGetValue(sig, out var cands) || cands.Count != 1) continue;
                    string old = cands[0];
                    var st = stats[old];
                    stats.Remove(old); bySig.Remove(sig);
                    // 표지 파일 이름도 새 폴더 이름으로
                    if (!string.IsNullOrEmpty(st.FolderName) && st.FolderName != folder)
                        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".auto.png" })
                        {
                            try
                            {
                                string a = Path.Combine(CoverArt.CoverDir, st.FolderName + ext), b = Path.Combine(CoverArt.CoverDir, folder + ext);
                                if (File.Exists(a) && !File.Exists(b)) File.Move(a, b);
                            }
                            catch { }
                        }
                    // 사용자가 폴더 이름을 직접 바꿨으면 그 이름을 사용자가 고른 이름으로 — 런처가 다시 자동으로 안 바꿈
                    if (!string.IsNullOrEmpty(st.FolderName) && st.FolderName != folder && g.FromFolder)
                    {
                        string nm = GameScanner.FolderName(g.Folder);
                        if (!string.IsNullOrEmpty(nm) && nm == folder)
                        {
                            string clean = TitleTidy.Clean(nm, new List<string>());
                            st.Title = "=" + (string.IsNullOrWhiteSpace(clean) ? nm.Trim() : clean.Trim());
                            st.NameFrom = "폴더 이름" + NameFinder.ByUser;
                            Log.Info("폴더 이름 직접 바뀜 → 이름 고정: " + st.FolderName + " → " + nm);
                        }
                    }
                    st.FolderName = folder; st.Sig = sig;
                    stats[id] = st;
                    Relinked.Add(id);
                    moves[old] = id;
                    changed = true;
                    Log.Info("옮겨진 게임 기록 이어 붙임: " + g.Title + " (" + g.Folder + ")");
                }
                if (changed) Save();
                if (moves.Count > 0) MoveIdData(moves);
            }
            catch (Exception ex) { Log.Error("게임 기록 맞추기", ex); }
        }

        public static void Load()
        {
            stats.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var p = line.Split('|');
                    if (p.Length < 4) continue;
                    var s = new GameStat
                    {
                        Favorite = p[1] == "1",
                        LastPlayed = long.TryParse(p[2], out long lp) ? lp : 0,
                        TotalSeconds = long.TryParse(p[3], out long ts) ? ts : 0,
                        Hidden = p.Length > 4 && p[4] == "1",
                        Title = p.Length > 5 && p[5].Length > 0 ? p[5].Replace("\u2502", "|") : null,
                        Res = p.Length > 6 && p[6].Length > 0 ? p[6] : null,
                        Cleared = p.Length > 7 && p[7] == "1",
                        Sig = p.Length > 8 && p[8].Length > 0 ? p[8].Replace("\u2502", "|") : null,
                        FolderName = p.Length > 9 && p[9].Length > 0 ? p[9] : null,
                        Lang = p.Length > 10 && int.TryParse(p[10], out int lg) ? lg : 0,
                        WinMode = p.Length > 11 && int.TryParse(p[11], out int wm) ? wm : 0,
                        UserTags = p.Length > 12 && p[12].Length > 0 ? p[12] : null,
                        NameFrom = p.Length > 13 && p[13].Length > 0 ? p[13].Replace("\u2502", "|") : null,
                    };
                    stats[p[0]] = s;
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                var sb = new StringBuilder();
                foreach (var kv in stats)
                {
                    var s = kv.Value;
                    if (!s.Favorite && s.LastPlayed == 0 && s.TotalSeconds == 0 && !s.Hidden && s.Title == null && s.Res == null && !s.Cleared && s.Lang == 0 && s.WinMode == 0 && string.IsNullOrEmpty(s.UserTags) && s.NameFrom == null) continue;
                    sb.Append(kv.Key).Append('|').Append(s.Favorite ? "1" : "0").Append('|')
                      .Append(s.LastPlayed).Append('|').Append(s.TotalSeconds).Append('|').Append(s.Hidden ? "1" : "0")
                      .Append('|').Append((s.Title ?? "").Replace("|", "\u2502").Replace("\n", " ")).Append('|').Append(s.Res ?? "").Append('|').Append(s.Cleared ? "1" : "0")
                      .Append('|').Append((s.Sig ?? "").Replace("|", "\u2502")).Append('|').Append(s.FolderName ?? "").Append('|').Append(s.Lang).Append('|').Append(s.WinMode).Append('|').Append((s.UserTags ?? "").Replace("|", "")).Append('|').Append((s.NameFrom ?? "").Replace("|", "\u2502").Replace("\n", " ")).AppendLine();
                }
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static void UnhideAll()
        {
            foreach (var s in stats.Values) s.Hidden = false;
            Save();
        }

        public static int HiddenCount()
        {
            int n = 0;
            foreach (var s in stats.Values) if (s.Hidden) n++;
            return n;
        }

        public static string PlayTimeText(long seconds)
        {
            if (seconds <= 0) return "없음";
            if (seconds < 60) return "1분 미만";
            if (seconds < 3600) return (seconds / 60) + "분";
            double h = seconds / 3600.0;
            return h < 10 ? h.ToString("0.0") + "시간" : ((long)h) + "시간";
        }

        public static string LastPlayedText(long ticks)
        {
            if (ticks <= 0) return "플레이 기록 없음";
            var t = new DateTime(ticks);
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "방금";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + "분 전";
            if (t.Date == DateTime.Today) return "오늘";
            if (t.Date == DateTime.Today.AddDays(-1)) return "어제";
            if (d.TotalDays < 7) return (int)d.TotalDays + "일 전";
            return t.ToString("yyyy년 M월 d일");
        }
    }
}
