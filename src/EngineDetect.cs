using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TkoolLauncher
{
    /// <summary>
    /// 쯔꾸르가 아닌 게임의 엔진 알아보기 (폴더 안 파일 모양으로).
    /// 유니티·언리얼·렌파이·울프 RPG·픽셀 게임 메이커 MV·티라노·키리키리·엔스크립터·게임메이커·고도·
    /// SRPG 스튜디오·스마일 게임 빌더/바킨·클릭팀 퓨전·컨스트럭트·AGS·HSP·LÖVE·XNA/모노게임·자바·플래시·일렉트론/NW.js 등.
    /// </summary>
    static class EngineDetect
    {
        public static string Detect(string dir, string exe)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
                Func<string, bool> F = rel => File.Exists(Path.Combine(dir, rel));
                Func<string, bool> D = rel => Directory.Exists(Path.Combine(dir, rel));
                Func<string, bool> Any = pat => { try { return Directory.EnumerateFiles(dir, pat).Any(); } catch { return false; } };
                Func<string, string, bool> AnyIn = (sub, pat) => { try { string p = Path.Combine(dir, sub); return Directory.Exists(p) && Directory.EnumerateFiles(p, pat).Any(); } catch { return false; } };
                string exeName = exe != null ? Path.GetFileNameWithoutExtension(exe) : "";

                // 유니티 (쯔꾸르 유나이트 포함)
                if (F("UnityPlayer.dll") || D(exeName + "_Data") || Directory.EnumerateDirectories(dir, "*_Data").Any(d => File.Exists(Path.Combine(d, "globalgamemanagers")) || File.Exists(Path.Combine(d, "data.unity3d"))))
                {
                    bool unite = Directory.EnumerateDirectories(dir, "*_Data").Any(d => Directory.Exists(Path.Combine(d, "StreamingAssets", "RPGMaker")));
                    return unite ? "쯔꾸르 유나이트(Unity)" : "Unity";
                }
                if (D("Engine\\Binaries") || Directory.EnumerateDirectories(dir).Any(d => Directory.Exists(Path.Combine(d, "Content", "Paks")))) return "Unreal";
                if (D("renpy") || (D("game") && (AnyIn("game", "*.rpa") || AnyIn("game", "*.rpyc")))) return "Ren'Py";
                if (F("Data.wolf") || AnyIn("Data", "*.wolf") || F("GuruguruSMF4.dll") || F("Game.wolf")) return "울프 RPG 에디터";
                if (D("Resources\\data") && F("Resources\\data\\info.json") || F("Player.exe") && D("Resources")) return "픽셀 게임 메이커 MV";
                if (F("data.dts")) return "SRPG 스튜디오";
                if (F("SharpKmyBase.dll") || F("SharpKmyCore.dll")) return Any("*.rbpack") || F("data.rbpack") ? "RPG 개발자 바킨" : "스마일 게임 빌더 / 바킨";
                if (Any("*.xp3")) return "키리키리(吉里吉里)";
                if (F("nscript.dat") || F("arc.nsa") || Any("*.nsa") || F("0.txt") && F("default.ttf")) return "엔스크립터";
                if (Any("*.lsb") || F("live.dll")) return "라이브메이커";
                if (F("data.win") || F("game.unx")) return "게임메이커";
                if (F(exeName + ".pck") || F("data.pck")) return "Godot";
                if (F("acsetup.cfg") || F("audio.vox") || Any("*.vox")) return "AGS (어드벤처 게임 스튜디오)";
                if (F("mmfs2.dll") || D("Modules") && AnyIn("Modules", "*.mfx")) return "클릭팀 퓨전";
                if (F("hsp3.exe") || Any("hsp3*.dll") || F("start.ax") || F("data.dpm")) return "HSP";
                if (F("love.dll") || F("lua51.dll") && Any("*.love")) return "LÖVE";
                if (F("MonoGame.Framework.dll")) return "MonoGame";
                if (F("FNA.dll")) return "FNA";
                if (F("Microsoft.Xna.Framework.dll") || F("Microsoft.Xna.Framework.Game.dll")) return "XNA";
                if (Any("*.jar") || D("jre")) return "Java";
                if (Any("*.swf")) return "플래시";
                if (F("DxLib.dll") || F("DxLib_x64.dll")) return "DX 라이브러리";
                // 웹 기술 (티라노·컨스트럭트·비주얼 노벨 메이커·일렉트론·NW.js)
                bool electron = F("resources\\app.asar") || F("resources\\electron.asar");
                bool nw = F("nw.pak") || F("package.nw");
                if (electron || nw || F("package.json"))
                {
                    string html = "";
                    foreach (var p in new[] { "package.json", "index.html", "www\\index.html", "resources\\app\\package.json", "resources\\app\\index.html" })
                        if (F(p)) try { html += File.ReadAllText(Path.Combine(dir, p)); } catch { }
                    string low = html.ToLowerInvariant();
                    if (low.Contains("tyrano") || D("tyrano") || D("resources\\app\\tyrano")) return "티라노 스크립트/빌더";
                    if (low.Contains("c3runtime") || low.Contains("construct 3")) return "컨스트럭트 3";
                    if (low.Contains("c2runtime") || low.Contains("construct 2")) return "컨스트럭트 2";
                    if (low.Contains("visual novel maker") || low.Contains("vnmaker")) return "비주얼 노벨 메이커";
                    if (low.Contains("pixi")) return electron ? "일렉트론 (HTML5)" : "NW.js (HTML5)";
                    return electron ? "일렉트론 (HTML5)" : nw ? "NW.js (HTML5)" : "HTML5";
                }
                if (F("d3dx9_43.dll") || F("SDL2.dll")) return "직접 만든 엔진 (SDL/DirectX)";
                return null;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// 한글 번역 방식 알아보기: 폴더 파일 + 읽어보기 글의 낱말로.
    /// 예) 직접 번역(텍스트), 설치형 패치, xdelta 패치, XUnity 자동번역, 이지트랜스·아네모네 같은 실시간 후킹, MTool, 로케일 필요
    /// </summary>
    static class TransDetect
    {
        public static List<string> Detect(GameInfo g, string readme)
        {
            var r = new List<string>();
            string dir = g.Folder;
            if (dir == null) return r;
            Func<string, bool> F = rel => File.Exists(Path.Combine(dir, rel));
            Func<string, bool> D = rel => Directory.Exists(Path.Combine(dir, rel));
            Func<string, bool> Any = pat => { try { return Directory.EnumerateFiles(dir, pat).Any(); } catch { return false; } };
            string t = (readme ?? "").ToLowerInvariant();
            Func<string, bool> Has = pattern => Regex.IsMatch(t, pattern);
            try
            {
                if (D("BepInEx") && (D("BepInEx\\plugins\\XUnity.AutoTranslator") || D("AutoTranslator") || Any("*AutoTranslator*"))) r.Add("XUnity 자동번역");
                else if (D("BepInEx")) r.Add("BepInEx 플러그인");
                if (Any("*.xdelta") || Any("*.vcdiff") || Has(@"xdelta")) r.Add("xdelta 패치");
                if (Any("MTool*") || Has(@"mtool")) r.Add("MTool");
                if (Has(@"ez\s*trans|이지\s*트랜스|ehnd")) r.Add("이지트랜스 실시간");
                if (Has(@"아네모네|anemone")) r.Add("아네모네 후킹");
                if (Has(@"textractor|\bith\b|vnr|후킹")) r.Add("후킹 번역");
                if (Has(@"translator\+\+|translator\s*plus")) r.Add("Translator++");
                if (Has(@"파파고|papago|기계\s*번역|구글\s*번역|deepl|gpt")) r.Add("기계 번역");
                if (Has(@"\.msi|인스톨|설치\s*파일|setup\.exe|패치\s*(파일|프로그램).*(실행|설치)") || Any("*패치*.exe") || Any("*patch*.exe")) r.Add("설치형 패치");
                if (Has(@"locale\s*emulator|로케일|applocale|ntlea")) r.Add("로케일 필요");
                if (Has(@"폰트|글꼴|font").Equals(true) && Has(@"설치")) r.Add("글꼴 설치 필요");
                if (r.Count == 0 || r.All(x => x == "로케일 필요" || x == "글꼴 설치 필요"))
                {
                    if (LibraryScreen.IsKorean(g) && (g.Tags.Contains("한글") || !g.Japanese)) r.Insert(0, "직접 번역 (데이터)");
                }
            }
            catch { }
            return r;
        }
    }
}
