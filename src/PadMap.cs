using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TkoolLauncher
{
    /// <summary>패드 → 키보드 바꾸기 (게임별): 자동이면 진짜 쯔꾸르 2000~VX Ace만(RPG_RT·RGSS 파일 확인). 패드를 스스로 읽는 게임에 겹쳐 보내면 두 번 눌리거나 깜빡일 수 있어 끌 수 있게.</summary>
    static class PadMap
    {
        static Dictionary<string, string> map;
        static string FilePath => Path.Combine(Settings.Dir, "cache", "padmap.txt");
        static Dictionary<string, string> M()
        {
            if (map != null) return map;
            map = new Dictionary<string, string>();
            try { if (File.Exists(FilePath)) foreach (var l in File.ReadAllLines(FilePath, Encoding.UTF8)) { int i = l.IndexOf('='); if (i > 0) map[l.Substring(0, i)] = l.Substring(i + 1); } } catch { }
            return map;
        }
        /// <summary>"on"·"off"·null(자동)</summary>
        public static string Get(GameInfo g) { lock (typeof(PadMap)) { string v; return M().TryGetValue(GameData.IdOf(g), out v) ? v : null; } }
        public static void Set(GameInfo g, string v)
        {
            lock (typeof(PadMap))
            {
                if (v == null) M().Remove(GameData.IdOf(g)); else M()[GameData.IdOf(g)] = v;
                try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllLines(FilePath, map.Select(kv => kv.Key + "=" + kv.Value).ToArray(), Encoding.UTF8); } catch { }
            }
        }
        public static bool Auto(GameInfo g)
        {
            if (!g.IsTkool || g.Version == TkoolVersion.MV || g.Version == TkoolVersion.MZ || g.Engine != null) return false;
            string d = g.InstallDir ?? g.Folder;
            try
            {
                if (File.Exists(Path.Combine(d, "RPG_RT.exe")) || File.Exists(Path.Combine(d, "RPG_RT.ldb"))) return true;
                if (Directory.EnumerateFiles(d, "*.rgss*").Any() || Directory.EnumerateFiles(d, "RGSS*.dll").Any() || Directory.Exists(Path.Combine(d, "System")) && Directory.EnumerateFiles(Path.Combine(d, "System"), "RGSS*.dll").Any()) return true;
                if (File.Exists(Path.Combine(d, "Game.ini")) && File.ReadAllText(Path.Combine(d, "Game.ini")).IndexOf("RGSS", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return false;
        }
        public static bool Want(GameInfo g) { string v = Get(g); return v == "on" ? true : v == "off" ? false : Auto(g); }
        public static string Label(GameInfo g) { string v = Get(g); return v == "on" ? "켬" : v == "off" ? "끔" : "자동(" + (Auto(g) ? "켬" : "끔") + ")"; }
    }
}
