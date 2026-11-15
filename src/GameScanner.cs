using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TkoolLauncher
{
    enum TkoolVersion { RM2000, XP, VX, VXAce, MV, MZ }

    class GameInfo
    {
        public string Title;
        public string Folder;
        public string Exe;
        public TkoolVersion Version;
        public bool Japanese;
        public bool NeedsRtp;
        public string RtpName;

        public string VersionLabel
        {
            get
            {
                switch (Version)
                {
                    case TkoolVersion.RM2000: return "2000/2003";
                    case TkoolVersion.XP: return "XP";
                    case TkoolVersion.VX: return "VX";
                    case TkoolVersion.VXAce: return "VX Ace";
                    case TkoolVersion.MV: return "MV";
                    default: return "MZ";
                }
            }
        }
    }

    static class GameScanner
    {
        const int MaxDepth = 5;

        static readonly string[] SkipDirs =
        {
            "www", "data", "js", "audio", "img", "graphics", "fonts", "movies", "system",
            "save", "locales", "swiftshader", "$recycle.bin", "system volume information"
        };

        static readonly string[] HelperExe =
        {
            "notification_helper", "nwjc", "crashpad_handler", "unins", "uninstall", "setup",
            "config", "dxsetup", "vcredist", "rtp", "patch", "update", "zip", "7z"
        };

        public static List<GameInfo> ScanAll(IEnumerable<string> roots)
        {
            var list = new List<GameInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in roots)
            {
                if (Directory.Exists(r)) Walk(r, 0, list, seen);
            }
            return list.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static void Walk(string dir, int depth, List<GameInfo> list, HashSet<string> seen)
        {
            GameInfo g = null;
            try { g = Detect(dir); } catch { }
            if (g != null)
            {
                if (seen.Add(g.Exe)) list.Add(g);
                return; // 게임 폴더 안쪽은 더 뒤지지 않음
            }
            if (depth >= MaxDepth) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (var s in subs)
            {
                string name = Path.GetFileName(s).ToLowerInvariant();
                if (SkipDirs.Contains(name)) continue;
                try
                {
                    var attr = File.GetAttributes(s);
                    if ((attr & FileAttributes.System) != 0 && (attr & FileAttributes.Hidden) != 0) continue;
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }
                Walk(s, depth + 1, list, seen);
            }
        }

        static bool Has(string dir, string rel) => File.Exists(Path.Combine(dir, rel));
        static bool HasDir(string dir, string rel) => Directory.Exists(Path.Combine(dir, rel));

        static bool AnyFile(string dir, string pattern)
        {
            try { return Directory.Exists(dir) && Directory.EnumerateFiles(dir, pattern).Any(); }
            catch { return false; }
        }

        public static GameInfo Detect(string dir)
        {
            // ── 2000 / 2003 ──
            if (Has(dir, "RPG_RT.exe") || (Has(dir, "RPG_RT.ldb") && Has(dir, "RPG_RT.lmt")))
                return Detect2k(dir);

            // ── XP / VX / VX Ace ──
            if (Has(dir, "Game.ini") || Has(dir, "Game.rgssad") || Has(dir, "Game.rgss2a") || Has(dir, "Game.rgss3a"))
            {
                var g = DetectRgss(dir);
                if (g != null) return g;
            }

            // ── MV / MZ ──
            if (HasDir(dir, "www") || HasDir(dir, "js"))
            {
                var g = DetectMvMz(dir);
                if (g != null) return g;
            }
            return null;
        }

        static GameInfo Detect2k(string dir)
        {
            string exe = Path.Combine(dir, "RPG_RT.exe");
            if (!File.Exists(exe)) exe = FindExe(dir, null);
            if (exe == null) return null;

            var g = new GameInfo { Folder = dir, Exe = exe, Version = TkoolVersion.RM2000 };
            string ini = TextDecode.ReadFile(Path.Combine(dir, "RPG_RT.ini"), out TextLang lang);
            string title = TextDecode.IniValue(ini, "RPG_RT", "GameTitle");
            g.Title = Clean(title) ?? Path.GetFileName(dir);
            g.Japanese = IsJapanese(lang, g.Title, dir);

            // FullPackageFlag=1 이면 RTP 없이 돌아가도록 다 들어있는 게임
            string full = TextDecode.IniValue(ini, "RPG_RT", "FullPackageFlag");
            g.NeedsRtp = full != "1";
            g.RtpName = "RPG2000/2003 RTP";
            return g;
        }

        static GameInfo DetectRgss(string dir)
        {
            string iniText = TextDecode.ReadFile(Path.Combine(dir, "Game.ini"), out TextLang lang);
            string lib = (TextDecode.IniValue(iniText, "Game", "Library") ?? "").ToUpperInvariant();

            TkoolVersion v;
            if (Has(dir, "Game.rgss3a") || lib.Contains("RGSS3") || AnyFile(Path.Combine(dir, "Data"), "*.rvdata2"))
                v = TkoolVersion.VXAce;
            else if (Has(dir, "Game.rgss2a") || lib.Contains("RGSS2") || AnyFile(Path.Combine(dir, "Data"), "*.rvdata"))
                v = TkoolVersion.VX;
            else if (Has(dir, "Game.rgssad") || lib.Contains("RGSS1") || lib.Contains("RGSS10") || AnyFile(Path.Combine(dir, "Data"), "*.rxdata"))
                v = TkoolVersion.XP;
            else
                return null;

            string exe = Path.Combine(dir, "Game.exe");
            if (!File.Exists(exe)) exe = FindExe(dir, null);
            if (exe == null) return null;

            var g = new GameInfo { Folder = dir, Exe = exe, Version = v };
            g.Title = Clean(TextDecode.IniValue(iniText, "Game", "Title")) ?? Path.GetFileName(dir);
            g.Japanese = IsJapanese(lang, g.Title, dir);

            // RTP=, RTP1= 값이 비어있지 않으면 RTP 필요
            string rtp = TextDecode.IniValue(iniText, "Game", "RTP");
            if (string.IsNullOrWhiteSpace(rtp)) rtp = TextDecode.IniValue(iniText, "Game", "RTP1");
            g.NeedsRtp = !string.IsNullOrWhiteSpace(rtp);
            g.RtpName = g.NeedsRtp ? rtp : null;
            return g;
        }

        static GameInfo DetectMvMz(string dir)
        {
            string baseDir = HasDir(dir, "www") ? Path.Combine(dir, "www") : dir;
            string js = Path.Combine(baseDir, "js");
            TkoolVersion v;
            if (File.Exists(Path.Combine(js, "rmmz_core.js"))) v = TkoolVersion.MZ;
            else if (File.Exists(Path.Combine(js, "rpg_core.js"))) v = TkoolVersion.MV;
            else return null;

            string exe = FindExe(dir, "Game.exe");
            if (exe == null) return null;

            var g = new GameInfo { Folder = dir, Exe = exe, Version = v };

            string sys = TextDecode.ReadFile(Path.Combine(baseDir, "data", "System.json"), out _);
            string title = TextDecode.JsonString(sys, "gameTitle");
            if (string.IsNullOrWhiteSpace(title))
            {
                string pkg = TextDecode.ReadFile(Path.Combine(dir, "package.json"), out _)
                             ?? TextDecode.ReadFile(Path.Combine(baseDir, "package.json"), out _);
                title = TextDecode.JsonString(pkg, "title");
            }
            g.Title = Clean(title) ?? Path.GetFileName(dir);

            string locale = TextDecode.JsonString(sys, "locale") ?? "";
            var tl = TextDecode.LangOf(g.Title);
            g.Japanese = tl == TextLang.Japanese || (tl != TextLang.Korean && locale.StartsWith("ja"));
            g.NeedsRtp = false;
            return g;
        }

        static bool IsJapanese(TextLang fileLang, string title, string dir)
        {
            var tl = TextDecode.LangOf(title);
            if (tl == TextLang.Korean) return false;
            if (tl == TextLang.Japanese) return true;
            if (fileLang == TextLang.Japanese) return true;
            if (fileLang == TextLang.Korean) return false;
            // 제목이 영어뿐이면 폴더 이름으로 한 번 더 판단
            return TextDecode.LangOf(Path.GetFileName(dir)) == TextLang.Japanese;
        }

        /// <summary>실행 파일 찾기: 원하는 이름 → nw.dll 옆의 exe → 가장 큰 exe</summary>
        static string FindExe(string dir, string preferred)
        {
            if (preferred != null && File.Exists(Path.Combine(dir, preferred)))
                return Path.Combine(dir, preferred);
            string[] exes;
            try { exes = Directory.GetFiles(dir, "*.exe"); } catch { return null; }
            var cands = exes.Where(e =>
            {
                string n = Path.GetFileNameWithoutExtension(e).ToLowerInvariant();
                return !HelperExe.Any(h => n.StartsWith(h));
            }).ToList();
            if (cands.Count == 0) return null;
            if (cands.Count == 1) return cands[0];
            var game = cands.FirstOrDefault(e => Path.GetFileName(e).Equals("Game.exe", StringComparison.OrdinalIgnoreCase));
            if (game != null) return game;
            return cands.OrderByDescending(e => { try { return new FileInfo(e).Length; } catch { return 0L; } }).First();
        }

        static string Clean(string s)
        {
            if (s == null) return null;
            s = s.Trim().Trim('"').Trim();
            return s.Length == 0 ? null : s;
        }
    }
}
