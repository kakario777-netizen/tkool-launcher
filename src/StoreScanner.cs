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
    }
}
