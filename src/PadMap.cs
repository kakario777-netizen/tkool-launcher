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
        /// <summary>키보드만 받는 유니티 게임: 패드용 부품(Rewired·InputSystem 등)이 없으면</summary>
        public static bool AutoKeyboard(GameInfo g)
        {
            try
            {
                string e = g.Engine ?? "";
                if (!(e.StartsWith("Unity") || e.Contains("유나이트"))) return false;
                return InputNeeds.Get(g).S[0] != 'o';
            }
            catch { return false; }
        }
        public static bool Want(GameInfo g) { string v = Get(g); return v == "on" || v == "kb" ? true : v == "off" ? false : Auto(g) || AutoKeyboard(g); }
        /// <summary>0 쯔꾸르식(A=Z·B=X…), 1 키보드 게임식(A=Enter·B=Esc·X=Space…)</summary>
        public static int Profile(GameInfo g) { string v = Get(g); return v == "kb" ? 1 : v == "on" ? 0 : (!Auto(g) && AutoKeyboard(g) ? 1 : 0); }
        public static string Label(GameInfo g)
        {
            string v = Get(g);
            if (v == "on") return "쯔꾸르식";
            if (v == "kb") return "키보드 게임식";
            if (v == "off") return "끔";
            return "자동(" + (Auto(g) ? "쯔꾸르식" : AutoKeyboard(g) ? "키보드 게임식" : "끔") + ")";
        }

        /// <summary>읽어보기에 적힌 키 중 기본 배치에 없는 것 → 남는 버튼(X·Y·LB·RB·LT·RT·Back)에 차례로</summary>
        public static Dictionary<Pad, System.Windows.Forms.Keys> Extra(GameInfo g, int profile)
        {
            var d = new Dictionary<Pad, System.Windows.Forms.Keys>();
            List<string> keys;
            try { keys = InputNeeds.Get(g).Keys; } catch { return d; }
            var used = profile == 1
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Enter", "Esc", "Space", "Z", "Q", "E", "Tab" }
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Z", "X", "Shift", "Esc", "Q", "W", "Enter" };
            var free = profile == 1 ? new[] { Pad.LT, Pad.RT, Pad.LB, Pad.RB } : new[] { Pad.LT, Pad.RT, Pad.Back };
            int fi = 0;
            foreach (var k in keys)
            {
                if (used.Contains(k) || fi >= free.Length) continue;
                var kk = ToKey(k);
                if (kk == System.Windows.Forms.Keys.None) continue;
                d[free[fi++]] = kk; used.Add(k);
            }
            return d;
        }

        static System.Windows.Forms.Keys ToKey(string k)
        {
            k = k.Trim().ToLowerInvariant();
            if (k.Length == 1 && k[0] >= 'a' && k[0] <= 'z') return (System.Windows.Forms.Keys)((int)System.Windows.Forms.Keys.A + (k[0] - 'a'));
            if (k.StartsWith("f") && k.Length <= 3) { int n; if (int.TryParse(k.Substring(1), out n) && n >= 1 && n <= 12) return (System.Windows.Forms.Keys)((int)System.Windows.Forms.Keys.F1 + n - 1); }
            switch (k)
            {
                case "shift": return System.Windows.Forms.Keys.ShiftKey;
                case "ctrl": return System.Windows.Forms.Keys.ControlKey;
                case "alt": return System.Windows.Forms.Keys.Menu;
                case "space": return System.Windows.Forms.Keys.Space;
                case "enter": return System.Windows.Forms.Keys.Return;
                case "esc": return System.Windows.Forms.Keys.Escape;
                case "tab": return System.Windows.Forms.Keys.Tab;
                case "page up": return System.Windows.Forms.Keys.PageUp;
                case "page down": return System.Windows.Forms.Keys.Next;
                case "home": return System.Windows.Forms.Keys.Home;
                case "end": return System.Windows.Forms.Keys.End;
                case "delete": case "del": return System.Windows.Forms.Keys.Delete;
                case "insert": case "ins": return System.Windows.Forms.Keys.Insert;
                case "backspace": return System.Windows.Forms.Keys.Back;
            }
            return System.Windows.Forms.Keys.None;
        }
    }
}
