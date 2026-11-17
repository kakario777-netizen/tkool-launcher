using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TkoolLauncher
{
    /// <summary>스팀·에픽·GOG에 설치된 게임을 각 스토어의 설치 기록에서 읽어옴</summary>
    static class StoreScanner
    {
        public static List<GameInfo> ScanAll()
        {
            var list = new List<GameInfo>();
            try { list.AddRange(Steam()); } catch { }
            try { list.AddRange(Epic()); } catch { }
            try { list.AddRange(Gog()); } catch { }
            try { list.AddRange(Battle()); } catch { }
            return list;
        }

        // ───────── Steam ─────────
        // 게임이 아닌 것(런타임·도구)
        static readonly HashSet<string> SteamSkip = new HashSet<string>
        {
            "228980", "1070560", "1391110", "1628350", "1493710", "2180100", "2348590", "1826330", "1161040", "961940", "1580130", "250820"
        };

        static string SteamPath()
        {
            string p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrEmpty(p))
                p = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            return string.IsNullOrEmpty(p) ? null : p.Replace('/', '\\');
        }

        static IEnumerable<GameInfo> Steam()
        {
            string steam = SteamPath();
            if (steam == null || !Directory.Exists(steam)) yield break;

            var libs = new List<string> { steam };
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (!libs.Any(l => l.Equals(p, StringComparison.OrdinalIgnoreCase))) libs.Add(p);
                }

            foreach (var lib in libs)
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    string t;
                    try { t = File.ReadAllText(acf); } catch { continue; }
                    string id = Vdf(t, "appid"), name = Vdf(t, "name"), dir = Vdf(t, "installdir");
                    if (id == null || name == null || SteamSkip.Contains(id)) continue;
                    if (name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase)) continue;
                    string install = dir != null ? Path.Combine(apps, "common", dir) : null;
                    if (install != null && !Directory.Exists(install)) continue;

                    yield return new GameInfo
                    {
                        Kind = GameKind.Steam,
                        Title = name,
                        Folder = install ?? apps,
                        InstallDir = install,
                        Exe = "steam:" + id,
                        LaunchUri = "steam://rungameid/" + id,
                        CoverHint = SteamArt(steam, id, "library_600x900"),
                        HeroHint = SteamArt(steam, id, "library_hero"),
                    };
                }
            }
        }

        static string Vdf(string text, string key)
        {
            var m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>스팀이 이미 받아둔 표지 그림 (옛 방식·새 방식 폴더 둘 다)</summary>
        static string SteamArt(string steam, string id, string kind)
        {
            string cache = Path.Combine(steam, "appcache", "librarycache");
            foreach (var ext in new[] { ".jpg", ".png" })
            {
                string a = Path.Combine(cache, id + "_" + kind + ext);
                if (File.Exists(a)) return a;
                string b = Path.Combine(cache, id, kind + ext);
                if (File.Exists(b)) return b;
            }
            // 새 방식은 하위 폴더 안에 한 단계 더 들어있기도 함
            try
            {
                string d = Path.Combine(cache, id);
                if (Directory.Exists(d))
                {
                    var f = Directory.GetFiles(d, kind + ".*", SearchOption.AllDirectories).FirstOrDefault();
                    if (f != null) return f;
                }
            }
            catch { }
            return null;
        }

        // ───────── Epic ─────────
        static IEnumerable<GameInfo> Epic()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.GetFiles(dir, "*.item"))
            {
                string j;
                try { j = File.ReadAllText(f); } catch { continue; }
                if (j.Contains("\"bIsIncompleteInstall\": true") || j.Contains("\"bIsApplication\": false")) continue;
                string name = TextDecode.JsonString(j, "DisplayName");
                string loc = TextDecode.JsonString(j, "InstallLocation");
                string exe = TextDecode.JsonString(j, "LaunchExecutable");
                string app = TextDecode.JsonString(j, "AppName");
                string ns = TextDecode.JsonString(j, "CatalogNamespace");
                string item = TextDecode.JsonString(j, "CatalogItemId");
                if (name == null || loc == null || app == null || !Directory.Exists(loc)) continue;
                if (app.StartsWith("UE_")) continue; // 언리얼 엔진 자체
                yield return new GameInfo
                {
                    Kind = GameKind.Epic,
                    Title = name,
                    Folder = loc,
                    InstallDir = loc,
                    Exe = exe != null ? Path.Combine(loc, exe) : "epic:" + app,
                    LaunchUri = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(ns + ":" + item + ":" + app) + "?action=launch&silent=true",
                };
            }
        }

        // ───────── GOG ─────────
        static IEnumerable<GameInfo> Gog()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games"))
            {
                if (key == null) yield break;
                foreach (var id in key.GetSubKeyNames())
                {
                    using (var k = key.OpenSubKey(id))
                    {
                        if (k == null) continue;
                        string name = k.GetValue("gameName") as string;
                        string exe = k.GetValue("exe") as string;
                        string path = k.GetValue("path") as string;
                        string work = k.GetValue("workingDir") as string;
                        if (name == null || exe == null || !File.Exists(exe)) continue;
                        yield return new GameInfo
                        {
                            Kind = GameKind.Gog,
                            Title = name,
                            Folder = string.IsNullOrEmpty(work) ? (path ?? Path.GetDirectoryName(exe)) : work,
                            InstallDir = path ?? Path.GetDirectoryName(exe),
                            Exe = exe,
                        };
                    }
                }
            }
        }

        // ───────── 설치 기록(제어판 프로그램 목록) 읽기 ─────────
        class Uninst { public string Key, Name, Publisher, Dir, Icon; }

        static List<Uninst> UninstallEntries()
        {
            var res = new List<Uninst>();
            var roots = new[]
            {
                new { H = Registry.LocalMachine, P = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" },
                new { H = Registry.LocalMachine, P = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" },
                new { H = Registry.CurrentUser,  P = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" },
            };
            foreach (var r in roots)
            {
                try
                {
                    using (var key = r.H.OpenSubKey(r.P))
                    {
                        if (key == null) continue;
                        foreach (var id in key.GetSubKeyNames())
                        {
                            try
                            {
                                using (var k = key.OpenSubKey(id))
                                {
                                    if (k == null) continue;
                                    var u = new Uninst
                                    {
                                        Key = id,
                                        Name = k.GetValue("DisplayName") as string,
                                        Publisher = k.GetValue("Publisher") as string ?? "",
                                        Dir = k.GetValue("InstallLocation") as string,
                                        Icon = k.GetValue("DisplayIcon") as string,
                                    };
                                    if (string.IsNullOrWhiteSpace(u.Name)) continue;
                                    if ((k.GetValue("SystemComponent") as int?) == 1) continue;
                                    u.Dir = CleanDir(u.Dir);
                                    if (u.Dir == null && u.Icon != null)
                                    {
                                        string ic = IconExe(u.Icon);
                                        if (ic != null) u.Dir = Path.GetDirectoryName(ic);
                                    }
                                    if (u.Dir == null || !Directory.Exists(u.Dir)) continue;
                                    res.Add(u);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return res;
        }

        static string CleanDir(string d)
        {
            if (string.IsNullOrWhiteSpace(d)) return null;
            d = d.Trim().Trim('"').TrimEnd('\\');
            return d.Length < 4 ? null : d;   // "C:" 같은 드라이브 통째는 제외
        }

        static string IconExe(string icon)
        {
            string p = icon.Trim();
            int comma = p.LastIndexOf(',');
            if (comma > 2 && !p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, comma);
            p = p.Trim().Trim('"');
            return p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p) ? p : null;
        }

        // ───────── Battle.net (블리자드) ─────────
        // 표시 이름 → 배틀넷 실행 코드
        static readonly string[][] BlizzCodes =
        {
            new[] { "Diablo II: Resurrected", "OSI" }, new[] { "Diablo II Resurrected", "OSI" },
            new[] { "Diablo IV", "Fen" }, new[] { "Diablo III", "D3" }, new[] { "Diablo Immortal", "ANBS" },
            new[] { "Overwatch", "Pro" }, new[] { "Hearthstone", "WTCG" }, new[] { "Heroes of the Storm", "Hero" },
            new[] { "StarCraft II", "S2" }, new[] { "StarCraft", "S1" }, new[] { "Warcraft III", "W3" },
            new[] { "World of Warcraft Classic", "WoW_classic" }, new[] { "World of Warcraft", "WoW" },
            new[] { "Warcraft Rumble", "GRYPHON" },
        };

        static IEnumerable<GameInfo> Battle()
        {
            var all = UninstallEntries();
            var bnet = all.FirstOrDefault(u => u.Name == "Battle.net");
            string launcher = bnet != null ? Path.Combine(bnet.Dir, "Battle.net.exe") : null;
            if (launcher == null || !File.Exists(launcher))
            {
                string pf = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
                launcher = Path.Combine(pf, "Battle.net", "Battle.net.exe");
                if (!File.Exists(launcher)) launcher = null;
            }
            var res = new List<GameInfo>();
            foreach (var u in all)
            {
                if (u.Publisher.IndexOf("Blizzard", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (u.Name.StartsWith("Battle.net", StringComparison.OrdinalIgnoreCase)) continue;
                GameInfo pc = null;
                try { pc = GameScanner.DetectPc(u.Dir); } catch { }
                string exe = IconExe(u.Icon ?? "") ?? pc?.Exe;
                // 런처 껍데기(…Launcher.exe) 말고 진짜 게임 실행 파일이 있으면 그걸로 (아이콘·기록용)
                if (exe == null) continue;
                string code = null;
                foreach (var c in BlizzCodes) if (u.Name.StartsWith(c[0], StringComparison.OrdinalIgnoreCase)) { code = c[1]; break; }
                var g = new GameInfo { Kind = GameKind.Battle, Title = u.Name, Folder = u.Dir, InstallDir = u.Dir, Exe = exe };
                if (code != null && launcher != null) { g.Launcher = launcher; g.Args = "--exec=\"launch " + code + "\""; }
                res.Add(g);
            }
            return res;
        }

        // ───────── 직접 설치한 PC 게임 ─────────
        // 게임 회사(설치 기록의 '게시자')
        static readonly string[] GamePublishers =
        {
            "nexon", "ncsoft", "netmarble", "smilegate", "krafton", "pubg", "pearl abyss", "kakao games", "riot games",
            "electronic arts", "ubisoft", "bandai namco", "square enix", "capcom", "sega", "koei tecmo", "hoyoverse", "mihoyo",
            "cognosphere", "wemade", "gravity", "neople", "com2us", "devsisters", "rockstar games", "cd projekt", "bethesda",
            "2k", "activision", "konami", "falcom", "nis america", "spike chunsoft", "atlus", "kadokawa", "degica", "playism",
            "paradox", "focus entertainment", "deep silver", "thq", "devolver", "annapurna", "team17", "humble games",
            "nintendo", "valve", "mojang", "grinding gear", "digital extremes", "wargaming", "gaijin", "hoyo", "shift up",
            "nexon games", "line games", "gamania", "xd entertainment", "kuro games", "level-5", "marvelous", "idea factory",
        };
        // 게임이 아닌 것 (런처·런타임·도구)
        static readonly string[] NotGame =
        {
            "launcher", "client", "steam", "epic games", "ea app", "origin", "ubisoft connect", "gog galaxy", "xbox", "playnite",
            "redistributable", "runtime", "directx", "driver", "anti-cheat", "anticheat", "easy anti", "battleye", "punkbuster",
            "nvidia", "geforce", "radeon", "amd ", "intel", "gamemaker", "game bar", "gamestream", "gameinput", "gaming services",
            "sdk", "editor", "toolkit", "overlay", "update", "helper", "visual c++", ".net", "framework", "microsoft", "windows",
            "rtp", "rpg maker", "rpgツクール", "ツクール", "unity hub", "unreal engine", "godot", "obs ", "discord", "afterburner",
            "rivatuner", "ayaspace", "aya space", "handheld companion", "vigem", "hidhide", "pawnio", "7-zip", "winrar", "bandizip",
        };

        public static List<GameInfo> Installed()
        {
            var res = new List<GameInfo>();
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in UninstallEntries())
            {
                if (u.Key.StartsWith("Steam App", StringComparison.OrdinalIgnoreCase)) continue;
                if (u.Publisher.IndexOf("Blizzard", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string ln = u.Name.ToLowerInvariant(), lp = u.Publisher.ToLowerInvariant(), ld = u.Dir.ToLowerInvariant();
                if (NotGame.Any(w => ln.Contains(w))) continue;
                if (ld.Contains("\\steamapps\\") || ld.Contains("\\windowsapps\\") || ld.Contains("\\microsoft")) continue;
                bool pub = GamePublishers.Any(w => lp.Contains(w));
                bool path = ld.Split('\\').Any(seg => seg.Contains("game") || seg.Contains("게임") || seg.Contains("ゲーム"));
                if (!pub && !path) continue;
                if (!dirs.Add(u.Dir)) continue;
                GameInfo pc = null;
                try { pc = GameScanner.DetectPc(u.Dir); } catch { }
                string exe = IconExe(u.Icon ?? "");
                if (exe != null && !exe.StartsWith(u.Dir, StringComparison.OrdinalIgnoreCase)) exe = null;
                if (exe != null && Path.GetFileName(exe).ToLowerInvariant().StartsWith("unins")) exe = null;
                exe = exe ?? pc?.Exe;
                if (exe == null) continue;
                res.Add(new GameInfo { Kind = GameKind.Pc, Title = u.Name.Trim(), Folder = Path.GetDirectoryName(exe), InstallDir = u.Dir, Exe = exe });
            }
            return res;
        }
    }
}
