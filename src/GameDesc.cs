using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임 소개 글 (스팀처럼 게임 화면에 보여줌)
    /// - 스팀 게임: 스팀 상점의 한국어 짧은 소개 (한 번 받으면 저장)
    /// - 쯔꾸르·PC 게임: 게임 폴더의 읽어보기 파일(readme, 説明書, はじめに, 설명 등) 앞부분
    /// </summary>
    static class GameDesc
    {
        static readonly Dictionary<string, string> mem = new Dictionary<string, string>();
        static readonly HashSet<string> loading = new HashSet<string>();
        public static Action Changed;

        /// <summary>읽어보기 글 원문 그대로 (번역 방식 알아보기용, 없으면 "")</summary>
        public static string Raw(GameInfo g) { try { return Readme(g); } catch { return ""; } }

        static string CachePath(GameInfo g) => Path.Combine(Settings.Dir, "cache", GameData.IdOf(g) + "_d.txt");

        /// <summary>있으면 글, 아직이면 null(뒤에서 읽는 중), 없으면 ""</summary>
        public static string Get(GameInfo g)
        {
            string id = GameData.IdOf(g);
            lock (mem)
            {
                if (mem.TryGetValue(id, out string d)) return d;
                if (!loading.Add(id)) return null;
            }
            Task.Run(() =>
            {
                string d = "";
                try { d = Load(g); } catch { }
                lock (mem) { mem[id] = d ?? ""; loading.Remove(id); }
                Changed?.Invoke();
            });
            return null;
        }

        static string Load(GameInfo g)
        {
            string cp = CachePath(g);
            if (File.Exists(cp)) return File.ReadAllText(cp, Encoding.UTF8);

            string d = "";
            if (g.Kind == GameKind.Steam) d = Steam(g);
            if (string.IsNullOrEmpty(d)) d = Readme(g);
            if (!string.IsNullOrEmpty(d) && g.Kind == GameKind.Steam)
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(cp)); File.WriteAllText(cp, d, Encoding.UTF8); } catch { }
            }
            return d;
        }

        static string Steam(GameInfo g)
        {
            string id = g.Exe.StartsWith("steam:") ? g.Exe.Substring(6) : null;
            if (id == null) return "";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "TkoolLauncher";
                    string json = wc.DownloadString("https://store.steampowered.com/api/appdetails?filters=basic&l=korean&appids=" + id);
                    string s = TextDecode.JsonString(json, "short_description") ?? "";
                    s = Regex.Replace(s, "<[^>]+>", " ");
                    return WebUtility.HtmlDecode(s).Trim();
                }
            }
            catch { return ""; }
        }

        static readonly string[] ReadmeWords = { "readme", "read me", "read_me", "説明", "はじめに", "お読み", "取扱", "manual", "설명", "필독", "읽어", "소개", "info" };

        static string Readme(GameInfo g)
        {
            string[] files;
            try { files = Directory.GetFiles(g.Folder, "*.txt"); } catch { return ""; }
            if (files.Length == 0) return "";
            string pick = files.FirstOrDefault(f => ReadmeWords.Any(w => Path.GetFileName(f).ToLowerInvariant().Contains(w))) ?? files[0];
            try
            {
                if (new FileInfo(pick).Length > 2000000) return "";
            }
            catch { return ""; }
            string t = TextDecode.ReadFile(pick, out _) ?? "";
            // 줄 정리: 빈 줄·장식 줄 빼고 앞부분만
            var lines = t.Replace("\r", "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !Regex.IsMatch(l, @"^[\-=_*#~■□◆◇●○★☆─━＝－・…\s]+$"))
                .Take(150);
            string r = string.Join("\n", lines);
            return r.Length > 5000 ? r.Substring(0, 5000) + "…" : r;
        }
    }
}
