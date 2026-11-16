using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TkoolLauncher
{
    /// <summary>
    /// 설정은 내 문서\쯔꾸르 런처\settings.ini 에 저장 (exe를 옮기거나 지워도 유지)
    /// </summary>
    static class Settings
    {
        public static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "쯔꾸르 런처");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");

        public static List<string> GameFolders = new List<string>();
        public static bool AutoUpdate = true;
        public static bool StoreScan = true;
        public static string HomeKey = "Win+Ctrl+128";   // AYA 큰 버튼 (Ctrl+Win+F17)
        public static string SmallKey = "Win+68";        // AYA 작은 버튼 (Win+D)
        public static bool HardwareControl;
        public static int SmartFps;          // 0 = 끔
        public static int AccentIndex;       // 테마 색
        public static bool UseBgImage = true, Bgm;
        public static int LcAction = 1, RcAction = 3;   // 위쪽 LC/RC 버튼 기능 (ButtonActions 번호)
        public static bool LoopGuard = true;
        public static int Tdp;          // 0 = 기기 기본
        public static int Fan = -1;     // -1 = 자동
        public static bool LedSet, LedOn = true;
        public static int LedR = 255, LedG = 255, LedB = 255, LedBright = 60;
        public static List<string> ManualGames = new List<string>();

        public static void Load()
        {
            GameFolders.Clear();
            ManualGames.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string val = raw.Substring(eq + 1).Trim();
                    if (key == "GameFolder" && val.Length > 0 && !GameFolders.Contains(val))
                        GameFolders.Add(val);
                    else if (key == "ManualGame" && val.Length > 0 && !ManualGames.Contains(val))
                        ManualGames.Add(val);
                    else if (key == "HomeKey") HomeKey = val;
                    else if (key == "SmallKey") SmallKey = val;
                    else if (key == "HardwareControl") HardwareControl = val == "1";
                    else if (key == "Tdp") int.TryParse(val, out Tdp);
                    else if (key == "SmartFps") int.TryParse(val, out SmartFps);
                    else if (key == "Accent") int.TryParse(val, out AccentIndex);
                    else if (key == "BgImage") UseBgImage = val != "0";
                    else if (key == "Bgm") Bgm = val == "1";
                    else if (key == "LcAction") int.TryParse(val, out LcAction);
                    else if (key == "RcAction") int.TryParse(val, out RcAction);
                    else if (key == "LoopGuard") LoopGuard = val != "0";
                    else if (key == "Fan") int.TryParse(val, out Fan);
                    else if (key == "Led")
                    {
                        var q = val.Split(',');
                        if (q.Length == 5)
                        {
                            LedSet = true; LedOn = q[0] == "1";
                            int.TryParse(q[1], out LedR); int.TryParse(q[2], out LedG); int.TryParse(q[3], out LedB); int.TryParse(q[4], out LedBright);
                        }
                    }
                    else if (key == "StoreScan")
                        StoreScan = val != "0";
                    else if (key == "AutoUpdate")
                        AutoUpdate = val != "0";
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("[쯔꾸르 런처]");
                sb.AppendLine("AutoUpdate=" + (AutoUpdate ? "1" : "0"));
                sb.AppendLine("StoreScan=" + (StoreScan ? "1" : "0"));
                sb.AppendLine("HomeKey=" + HomeKey);
                sb.AppendLine("SmallKey=" + SmallKey);
                sb.AppendLine("HardwareControl=" + (HardwareControl ? "1" : "0"));
                sb.AppendLine("Tdp=" + Tdp);
                sb.AppendLine("SmartFps=" + SmartFps);
                sb.AppendLine("Accent=" + AccentIndex);
                sb.AppendLine("BgImage=" + (UseBgImage ? "1" : "0"));
                sb.AppendLine("Bgm=" + (Bgm ? "1" : "0"));
                sb.AppendLine("LcAction=" + LcAction);
                sb.AppendLine("RcAction=" + RcAction);
                sb.AppendLine("LoopGuard=" + (LoopGuard ? "1" : "0"));
                sb.AppendLine("Fan=" + Fan);
                if (LedSet) sb.AppendLine("Led=" + (LedOn ? "1" : "0") + "," + LedR + "," + LedG + "," + LedB + "," + LedBright);
                foreach (var f in GameFolders) sb.AppendLine("GameFolder=" + f);
                foreach (var f in ManualGames) sb.AppendLine("ManualGame=" + f);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
