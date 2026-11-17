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
    }

    static class GameData
    {
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
                    if (!s.Favorite && s.LastPlayed == 0 && s.TotalSeconds == 0 && !s.Hidden && s.Title == null) continue;
                    sb.Append(kv.Key).Append('|').Append(s.Favorite ? "1" : "0").Append('|')
                      .Append(s.LastPlayed).Append('|').Append(s.TotalSeconds).Append('|').Append(s.Hidden ? "1" : "0")
                      .Append('|').Append((s.Title ?? "").Replace("|", "\u2502").Replace("\n", " ")).AppendLine();
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
