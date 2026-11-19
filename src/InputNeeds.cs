using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TkoolLauncher
{
    /// <summary>게임 조작 방식: 패드·키보드·마우스·터치가 되는지/꼭 필요한지 — 엔진·폴더 파일·읽어보기 글로 추측, 사용자가 직접 고칠 수 있음(cache\input.txt)
    /// 상태 글자: n=꼭 필요, o=됨, x=안 됨, ?=모름</summary>
    static class InputNeeds
    {
        public static readonly string[] Names = { "패드", "키보드", "마우스", "터치" };
        public class Result { public char[] S = { '?', '?', '?', '?' }; public bool User; public string Note = ""; public bool PadByLauncher; }

        static Dictionary<string, string> user;
        static readonly Dictionary<string, Result> mem = new Dictionary<string, Result>();
        static string Path_ => Path.Combine(Settings.Dir, "cache", "input.txt");

        static Dictionary<string, string> User()
        {
            if (user != null) return user;
            user = new Dictionary<string, string>();
            try { if (File.Exists(Path_)) foreach (var l in File.ReadAllLines(Path_, Encoding.UTF8)) { int i = l.IndexOf('='); if (i > 0) user[l.Substring(0, i)] = l.Substring(i + 1); } } catch { }
            return user;
        }

        public static void SetUser(GameInfo g, string s)
        {
            lock (mem)
            {
                var u = User(); string id = GameData.IdOf(g);
                if (s == null) u.Remove(id); else u[id] = s;
                mem.Remove(id);
                try { Directory.CreateDirectory(Path.GetDirectoryName(Path_)); File.WriteAllLines(Path_, u.Select(kv => kv.Key + "=" + kv.Value).ToArray(), Encoding.UTF8); } catch { }
            }
        }

        public static Result Get(GameInfo g)
        {
            string id = GameData.IdOf(g);
            lock (mem) { Result r; if (mem.TryGetValue(id, out r)) return r; }
            var res = Detect(g);
            string us; lock (mem) { if (User().TryGetValue(id, out us) && us.Length == 4) { res.S = us.ToCharArray(); res.User = true; } mem[id] = res; }
            return res;
        }

        static void Set(Result r, int i, char c) { if (r.S[i] != 'n' || c == 'x') r.S[i] = c; }

        static Result Detect(GameInfo g)
        {
            var r = new Result();
            string dir = g.InstallDir ?? g.Folder;
            Func<string, bool> F = rel => { try { return dir != null && File.Exists(Path.Combine(dir, rel)); } catch { return false; } };
            Func<string, bool> AnyDeep = pat => { try { return dir != null && Directory.EnumerateFiles(dir, pat, SearchOption.AllDirectories).Any(); } catch { return false; } };
            string eng = g.Engine ?? "";
            if (g.IsTkool)
            {
                switch (g.Version)
                {
                    case TkoolVersion.MV: case TkoolVersion.MZ:
                        r.S = "oooo".ToCharArray(); r.Note = "MV·MZ는 패드·키보드·마우스·터치 다 돼요"; break;
                    default:   // 2000~VX Ace: 원래 키보드 게임 → 런처가 패드를 키보드로 바꿔 줌
                        r.S = new[] { 'o', 'o', g.Version == TkoolVersion.RM2000 ? 'x' : '?', 'x' }; r.PadByLauncher = true;
                        r.Note = "원래 키보드 게임이라 런처가 패드 버튼을 키보드로 바꿔 줘요"; break;
                }
            }
            else if (eng.StartsWith("Unity") || eng.Contains("유나이트"))
            {
                r.S = new[] { '?', 'o', '?', '?' };
                if (AnyDeep("Rewired_*.dll") || AnyDeep("Unity.InputSystem.dll") || AnyDeep("XInputDotNetPure.dll") || AnyDeep("InControl.dll")) { r.S[0] = 'o'; r.Note = "패드용 부품이 들어 있어요"; }
                if (eng.Contains("유나이트")) r.S = "oooo".ToCharArray();
            }
            else if (eng == "Ren'Py") r.S = "oooo".ToCharArray();
            else if (eng.StartsWith("울프")) { r.S = new[] { 'o', 'o', '?', 'x' }; r.Note = "울프 RPG는 패드를 지원해요 (안 되면 마우스 모드·키 바꾸기)"; }
            else if (eng.StartsWith("티라노") || eng.StartsWith("키리키리") || eng == "엔스크립터" || eng == "라이브메이커" || eng.StartsWith("비주얼 노벨"))
            { r.S = new[] { '?', 'o', 'n', 'o' }; r.Note = "노벨 게임은 마우스(터치)로 해요 — 패드로 하려면 빠른 설정 '마우스' 모드"; }
            else if (eng == "Unreal") r.S = new[] { 'o', 'o', 'o', '?' };
            else if (eng.StartsWith("Java") || eng.StartsWith("플래시") || eng == "HSP" || eng.StartsWith("클릭팀") || eng.StartsWith("DX") || eng.StartsWith("게임메이커"))
                r.S = new[] { '?', 'o', '?', '?' };

            // 읽어보기 글로 보강 (일본어·한국어·영어)
            string t = "";
            try { t = (GameDesc.Raw(g) ?? "").ToLowerInvariant(); } catch { }
            if (t.Length > 0)
            {
                Func<string, bool> Has = pat => Regex.IsMatch(t, pat);
                if (Has(@"(パッド|ゲームパッド|コントローラー?|ジョイスティック)\s*(には|は)?\s*(非対応|未対応|対応していません|使用できません)|(패드|컨트롤러|게임패드)\s*(미지원|지원\s*안|사용\s*불가)|(gamepad|controller)s?\s*(are\s*)?not\s*supported"))
                { r.S[0] = 'x'; r.Note = "읽어보기에 패드 안 된다고 적혀 있어요" + (r.PadByLauncher ? " (런처 변환으로는 될 수 있어요)" : ""); if (r.PadByLauncher) r.S[0] = 'o'; }
                else if (Has(@"(パッド|ゲームパッド|コントローラー?|ジョイパッド|xbox)\s*(にも|も)?\s*(対応|使用可|使えます)|(패드|게임패드|컨트롤러)\s*(도\s*)?(지원|사용\s*가능)|(gamepad|controller)\s*(support|supported|compatible)"))
                    r.S[0] = 'o';
                if (Has(@"マウス\s*(必須|のみ|専用|が必要|で(の)?操作)|마우스\s*(필수|전용|만|로\s*조작)|mouse\s*(only|required)")) { r.S[2] = 'n'; if (r.S[3] == '?' || r.S[3] == 'x') r.S[3] = 'o'; }
                else if (Has(@"マウス\s*(にも|も)?\s*対応|마우스\s*(도\s*)?지원|mouse\s*support")) Set(r, 2, 'o');
                if (Has(@"キーボード\s*(必須|のみ|専用|が必要)|키보드\s*(필수|전용|만)|keyboard\s*(only|required)")) r.S[1] = 'n';
                if (Has(@"タッチ\s*(操作)?\s*(対応|可能)|터치\s*(지원|가능)|touch\s*(support|screen)")) Set(r, 3, 'o');
            }
            return r;
        }

        public static string Label(int i, char c)
        {
            string n = Names[i];
            switch (c) { case 'n': return n + " 필요"; case 'o': return n + " ○"; case 'x': return n + " ✕"; default: return n + " ?"; }
        }
        public static string StateWord(char c) => c == 'n' ? "꼭 필요" : c == 'o' ? "됨" : c == 'x' ? "안 됨" : "모름";
    }
}
