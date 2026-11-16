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
        public static string HomeKey = "";
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
                    else if (key == "HomeKey")
                        HomeKey = val;
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
                foreach (var f in GameFolders) sb.AppendLine("GameFolder=" + f);
                foreach (var f in ManualGames) sb.AppendLine("ManualGame=" + f);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
