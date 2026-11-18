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
        public static string HomeLongKey = "";            // AYA 큰 버튼 길게 누를 때 다른 신호가 오면 그 키
        public static string CheatExe = "";               // 치트 에디터(MiniCheat.exe) 위치
        public static bool HardwareControl;
        // 성능 표시(오버레이) 꾸미기
        public static int OvStyle = 0, OvPos = 0, OvSize = 1, OvColor = 0, OvBg = 60, OvText = 100, OvItems = 0x3FF, OvMargin = 1;
        public static bool OvOn;   // 성능 표시 켜 둔 채로 기억
        public static float OvX = -1, OvY = -1;   // 끌어 놓은 위치 (화면 비율 0~1)
        public static string AdminTaskExe = "";   // 관리자 작업이 가리키는 exe 경로 (옮기면 다시 만듦)
        public static int SmartFps;          // 0 = 끔
        public static int AccentIndex;       // 테마 색
        public static bool UseBgImage = true, Bgm;
        public static int LcAction = 1, RcAction = 3;   // 위쪽 LC/RC 버튼 기능 (ButtonActions 번호)
        public static bool LoopGuard = true;
        public static int LedEffect;          // 0 고정, 1 숨쉬기, 2 무지개
        public static bool AutoScaleTkool;
        public static bool AutoCover = true;   // 표지 없는 게임은 처음 켤 때 화면을 찍어 표지로    // 쯔꾸르(2000~VX Ace) 켤 때 자동으로 도트 선명 확대
        // 가상 패드
        public static bool VPadOn;
        public static int Vibration = 100, StickDeadzone = 5, StickSens = 100, TurboMask, TurboHz = 12;
        public static int GyroMode, GyroSens = 5, GyroHold, GyroAxis;
        public static bool GyroInvertX, GyroInvertY;
        public static int Tdp;          // 0 = 기기 기본
        public static int Fan = -1;     // -1 = 자동
        public static string LibStartTab = "즐겨찾기";
        public static string ChipOrder = "";
        public static string DupIgnore = "";   // 중복 아님으로 표시한 게임 짝들   // 쯔꾸르 분류 칸 순서 (쉼표, 앞에 - 는 숨김)   // 라이브러리를 열면 처음 보일 탭
        public static int PerfMode = -1; // 성능 모드 0 저소음 1 균형 2 고성능 3 직접 (-1 = 처음, 직접으로 취급)
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
                    else if (key == "HomeLongKey") HomeLongKey = val;
                    else if (key == "CheatExe") CheatExe = val;
                    else if (key == "HardwareControl") HardwareControl = val == "1";
                    else if (key == "OvStyle") int.TryParse(val, out OvStyle);
                    else if (key == "OvPos") int.TryParse(val, out OvPos);
                    else if (key == "OvSize") int.TryParse(val, out OvSize);
                    else if (key == "OvColor") int.TryParse(val, out OvColor);
                    else if (key == "OvBg") int.TryParse(val, out OvBg);
                    else if (key == "OvText") int.TryParse(val, out OvText);
                    else if (key == "OvItems") int.TryParse(val, out OvItems);
                    else if (key == "OvMargin") int.TryParse(val, out OvMargin);
                    else if (key == "OvOn") OvOn = val == "1";
                    else if (key == "PerfMode") int.TryParse(val, out PerfMode);
                    else if (key == "LibStartTab") LibStartTab = val;
                    else if (key == "ChipOrder") ChipOrder = val;
                    else if (key == "DupIgnore") DupIgnore = val;
                    else if (key == "OvX") float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out OvX);
                    else if (key == "OvY") float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out OvY);
                    else if (key == "AdminTaskExe") AdminTaskExe = val;
                    else if (key == "Tdp") int.TryParse(val, out Tdp);
                    else if (key == "SmartFps") int.TryParse(val, out SmartFps);
                    else if (key == "Accent") int.TryParse(val, out AccentIndex);
                    else if (key == "BgImage") UseBgImage = val != "0";
                    else if (key == "Bgm") Bgm = val == "1";
                    else if (key == "LcAction") int.TryParse(val, out LcAction);
                    else if (key == "RcAction") int.TryParse(val, out RcAction);
                    else if (key == "LoopGuard") LoopGuard = val != "0";
                    else if (key == "LedEffect") int.TryParse(val, out LedEffect);
                    else if (key == "AutoScaleTkool") AutoScaleTkool = val == "1";
                    else if (key == "AutoCover") AutoCover = val != "0";
                    else if (key == "VPad")
                    {
                        var q = val.Split(',');
                        if (q.Length >= 13)
                        {
                            VPadOn = q[0] == "1";
                            int.TryParse(q[1], out Vibration); int.TryParse(q[2], out StickDeadzone); int.TryParse(q[3], out StickSens);
                            int.TryParse(q[4], out TurboMask); int.TryParse(q[5], out TurboHz); int.TryParse(q[6], out GyroMode);
                            int.TryParse(q[7], out GyroSens); int.TryParse(q[8], out GyroHold); int.TryParse(q[9], out GyroAxis);
                            GyroInvertX = q[10] == "1"; GyroInvertY = q[11] == "1";
                        }
                    }
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
                sb.AppendLine("HomeLongKey=" + HomeLongKey);
                sb.AppendLine("CheatExe=" + CheatExe);
                sb.AppendLine("HardwareControl=" + (HardwareControl ? "1" : "0"));
                sb.AppendLine("OvStyle=" + OvStyle); sb.AppendLine("OvPos=" + OvPos); sb.AppendLine("OvSize=" + OvSize);
                sb.AppendLine("OvColor=" + OvColor); sb.AppendLine("OvBg=" + OvBg); sb.AppendLine("OvText=" + OvText); sb.AppendLine("OvItems=" + OvItems);
                sb.AppendLine("OvMargin=" + OvMargin); sb.AppendLine("OvOn=" + (OvOn ? "1" : "0")); sb.AppendLine("PerfMode=" + PerfMode); sb.AppendLine("LibStartTab=" + LibStartTab); sb.AppendLine("ChipOrder=" + ChipOrder); sb.AppendLine("DupIgnore=" + DupIgnore);
                sb.AppendLine("OvX=" + OvX.ToString(System.Globalization.CultureInfo.InvariantCulture)); sb.AppendLine("OvY=" + OvY.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("AdminTaskExe=" + AdminTaskExe);
                sb.AppendLine("Tdp=" + Tdp);
                sb.AppendLine("SmartFps=" + SmartFps);
                sb.AppendLine("Accent=" + AccentIndex);
                sb.AppendLine("BgImage=" + (UseBgImage ? "1" : "0"));
                sb.AppendLine("Bgm=" + (Bgm ? "1" : "0"));
                sb.AppendLine("LcAction=" + LcAction);
                sb.AppendLine("RcAction=" + RcAction);
                sb.AppendLine("LoopGuard=" + (LoopGuard ? "1" : "0"));
                sb.AppendLine("LedEffect=" + LedEffect);
                sb.AppendLine("AutoScaleTkool=" + (AutoScaleTkool ? "1" : "0"));
                sb.AppendLine("AutoCover=" + (AutoCover ? "1" : "0"));
                sb.AppendLine("VPad=" + string.Join(",", new object[] { VPadOn ? 1 : 0, Vibration, StickDeadzone, StickSens, TurboMask, TurboHz,
                    GyroMode, GyroSens, GyroHold, GyroAxis, GyroInvertX ? 1 : 0, GyroInvertY ? 1 : 0, 0 }));
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
