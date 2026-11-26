using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TkoolLauncher
{
    /// <summary>올린 사람이 이름을 바꿔도 남는 게임 고유 흔적 — 판매 번호(RJ·VJ·BJ·FANZA d_)·스팀 앱 번호(steam_appid.txt)·게임 안 진짜 이름(Unity app.info·exe 제품 이름·Ren'Py config.name·package.json 창 제목)</summary>
    static class GameIdent
    {
        static readonly Regex Rj = new Regex(@"(?i)\b(RJ|VJ|BJ)\d{5,9}\b"), Dmm = new Regex(@"(?i)\bd_\d{5,7}\b");
        static readonly Dictionary<string, Tuple<string, string, string, List<string>>> mem = new Dictionary<string, Tuple<string, string, string, List<string>>>();
        static readonly HashSet<string> Generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "game", "rpg maker", "rpgmaker", "rgss player", "rgss3 player", "nw.js", "node-webkit", "unity", "unity player", "electron", "launcher", "setup", "player", "rpg_rt", "game.exe", "renpy", "ren'py", "wolf rpg editor", "game_pro", "tyranoscript", "kirikiri", "krkr", "nwjs" };

        static Tuple<string, string, string, List<string>> Scan(GameInfo g)
        {
            string id = GameData.IdOf(g);
            lock (mem) { Tuple<string, string, string, List<string>> t; if (mem.TryGetValue(id, out t)) return t; }
            if (UiThread.IsUi)
            {
                // 화면 그리다 처음 만난 게임: 폴더 속을 뒤지는 건 뒤에서 (첫 시작 때 화면이 1분 넘게 멈추던 원인)
                var gg = g; UiThread.Later("ident:" + id, () => Scan(gg));
                return Tuple.Create<string, string, string, List<string>>(null, null, null, new List<string>());
            }
            string rj = null, dmm = null, steam = null; var names = new List<string>();
            string dir = g.InstallDir ?? g.Folder;
            try
            {
                if (dir != null && Directory.Exists(dir))
                {
                    // 파일·폴더 이름 (두 단계까지)
                    var entries = new List<string>();
                    try { entries.AddRange(Directory.EnumerateFileSystemEntries(dir).Take(400)); } catch { }
                    try { foreach (var d in Directory.EnumerateDirectories(dir).Take(30)) entries.AddRange(Directory.EnumerateFileSystemEntries(d).Take(100)); } catch { }
                    entries.Add(dir);
                    foreach (var e in entries)
                    {
                        string n = Path.GetFileName(e);
                        if (rj == null) { var m = Rj.Match(n); if (m.Success) rj = m.Value.ToUpperInvariant(); }
                        if (dmm == null) { var m = Dmm.Match(n); if (m.Success) dmm = m.Value.ToLowerInvariant(); }
                        if (steam == null && n.Equals("steam_appid.txt", StringComparison.OrdinalIgnoreCase))
                            try { var m = Regex.Match(File.ReadAllText(e), @"\d{3,9}"); if (m.Success) steam = m.Value; } catch { }
                    }
                    // 읽어보기 글 안의 번호
                    foreach (var f in entries.Where(x => Regex.IsMatch(x, @"(?i)\.(txt|md|nfo|html?|url)$")).Take(20))
                    {
                        try
                        {
                            if (new FileInfo(f).Length > 300000) continue;
                            string t = TextDecode.ReadFile(f, out TextLang _l);
                            if (rj == null) { var m = Rj.Match(t); if (m.Success) rj = m.Value.ToUpperInvariant(); }
                            if (dmm == null) { var m = Regex.Match(t, @"(?i)cid=(d_\d{5,7})"); if (m.Success) dmm = m.Groups[1].Value.ToLowerInvariant(); }
                            if (steam == null) { var m = Regex.Match(t, @"store\.steampowered\.com/app/(\d+)"); if (m.Success) steam = m.Groups[1].Value; }
                        }
                        catch { }
                    }
                    // 게임 안 진짜 이름
                    try
                    {
                        foreach (var d in Directory.EnumerateDirectories(dir, "*_Data").Take(3))
                        {
                            string ai = Path.Combine(d, "app.info");
                            if (File.Exists(ai)) { var ls = File.ReadAllLines(ai, Encoding.UTF8); if (ls.Length > 1) names.Add(ls[1].Trim()); }
                        }
                    }
                    catch { }
                    try
                    {
                        string opt = Path.Combine(dir, "game", "options.rpy");
                        if (File.Exists(opt)) { var m = Regex.Match(File.ReadAllText(opt, Encoding.UTF8), @"config\.name\s*=\s*_?\(?\s*""([^""]+)"""); if (m.Success) names.Add(m.Groups[1].Value); }
                    }
                    catch { }
                    try
                    {
                        string pj = Path.Combine(dir, "package.json");
                        if (File.Exists(pj)) { string j = File.ReadAllText(pj, Encoding.UTF8); var m = Regex.Match(j, @"""title""\s*:\s*""((?:[^""\\]|\\.)+)"""); if (m.Success) names.Add(Regex.Unescape(m.Groups[1].Value)); }
                    }
                    catch { }
                    try
                    {
                        if (g.Exe != null && File.Exists(g.Exe))
                        {
                            var vi = FileVersionInfo.GetVersionInfo(g.Exe);
                            foreach (var s in new[] { vi.ProductName, vi.FileDescription }) if (!string.IsNullOrWhiteSpace(s)) names.Add(s.Trim());
                        }
                    }
                    catch { }
                }
            }
            catch { }
            names = names.Where(n => n.Length >= 2 && n.Length <= 80 && !Generic.Contains(n) && !Regex.IsMatch(n, @"(?i)^(rpg ?maker|rgss|nw\.?js|unity|electron|game)\b")).Distinct().ToList();
            var r = Tuple.Create(rj, dmm, steam, names);
            lock (mem) mem[id] = r;
            return r;
        }

        public static string RjCode(GameInfo g) => Scan(g).Item1;
        public static string DmmCid(GameInfo g) => Scan(g).Item2;
        public static string SteamApp(GameInfo g) => Scan(g).Item3;
        public static List<string> InnerNames(GameInfo g) => Scan(g).Item4;
    }
}
