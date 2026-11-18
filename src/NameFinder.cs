using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TkoolLauncher
{
    /// <summary>
    /// 뜻 없는 이름(RJ37082·yagiri 3112·my·Game 같은 것)이나 한글판인데 영어/일본어 이름인 게임의
    /// 진짜 이름(가능하면 한글)을 찾아 줌.
    /// 찾는 곳: 읽어보기(readme·설명·한글패치 txt)의 "제목:" 같은 줄 → 『한글 제목』 →
    ///         RJ 번호면 DLsite 작품 이름(일본어) + 구글 번역.
    /// 찾으면 이름을 바꾸고 GameStat.NameFrom에 "원래 이름 · 어디서" 기록 (상세 화면에 보임).
    /// 한 번 찾아본 게임은 다시 안 찾음(NameFrom = "-").
    /// </summary>
    static class NameFinder
    {
        static readonly Regex Code = new Regex(@"(?i)^\s*(RJ|VJ|BJ|RE|RG|D_)\s*\d{4,9}\s*$");
        static readonly Regex CodeAny = new Regex(@"(?i)\b(RJ|VJ|BJ)\d{5,9}\b");
        static readonly Regex Hangul = new Regex(@"[가-힣]");
        static readonly Regex Kana = new Regex(@"[぀-ヿ一-鿿]");

        /// <summary>이름이 뜻이 없어 보이는지</summary>
        public static bool Meaningless(GameInfo g, string t)
        {
            if (GameCapture.IsGenericTitle(g, t)) return true;
            t = (t ?? "").Trim();
            if (Code.IsMatch(t) || CodeAny.IsMatch(t)) return true;
            if (Hangul.IsMatch(t) || Kana.IsMatch(t)) return false;
            string letters = new string(t.Where(char.IsLetter).ToArray());
            int digits = t.Count(char.IsDigit);
            if (letters.Length <= 3) return true;                          // my, abc, 3112
            if (digits >= 3) return true;                                  // yagiri 3112, game2023
            var words = t.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1 && letters.Length <= 5) return true;     // 짧은 한 단어
            if (!t.Any(c => "aeiouAEIOU".IndexOf(c) >= 0)) return true;    // 모음 없는 영문 (xkcd 같은 것)
            return false;
        }

        /// <summary>찾아볼 게임: 뜻 없는 이름, 또는 한글판인데 이름에 한글이 없음</summary>
        public static bool Wants(GameInfo g)
        {
            if (!g.FromFolder || g.Folder == null) return false;
            var st = GameData.Get(g);
            if (st.NameFrom != null) return false;                         // 이미 찾아봄
            if (st.Title != null && st.Title.StartsWith("=")) return false;  // 사용자가 고른 이름
            if (Meaningless(g, g.Title)) return true;
            return LibraryScreen.IsKorean(g) && !Hangul.IsMatch(g.Title ?? "");
        }

        public class Found { public string Name, From; }

        /// <summary>이름 후보 찾기 (느릴 수 있음 — 백그라운드에서)</summary>
        public static Found Find(GameInfo g, bool online)
        {
            var f = FromReadme(g);
            if (f != null) return f;
            if (online)
            {
                string code = FindCode(g);
                if (code != null) return FromDlsite(code);
            }
            return null;
        }

        // ───────── 읽어보기에서 ─────────
        static readonly Regex Label = new Regex(@"^\s*[\[【■◆●○▶☆★\-\*]*\s*(한글\s*제목|한국어\s*제목|게임\s*제목|게임\s*이름|게임명|제\s*목|타이틀|작품명|원제|タイトル|作品名|ゲーム名|title)\s*[\]】]?\s*[:：=\]】」]\s*(.+?)\s*$", RegexOptions.IgnoreCase);
        static readonly Regex Quoted = new Regex(@"[『「《〈\[]([^』」》〉\]\r\n]{2,40})[』」》〉\]]");

        static IEnumerable<string> TextFiles(string dir)
        {
            var list = new List<string>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    if (e == ".txt" || e == ".md" || e == ".nfo" || e == ".ini" && Path.GetFileName(f).ToLowerInvariant().Contains("read")) list.Add(f);
                }
                foreach (var sub in Directory.EnumerateDirectories(dir).Take(20))
                {
                    string n = Path.GetFileName(sub).ToLowerInvariant();
                    if (n.Contains("read") || n.Contains("한글") || n.Contains("patch") || n.Contains("패치") || n.Contains("説明") || n.Contains("doc"))
                        foreach (var f in Directory.EnumerateFiles(sub, "*.txt").Take(10)) list.Add(f);
                }
            }
            catch { }
            // 한글·패치·readme 이름이 앞으로
            return list.OrderByDescending(f =>
            {
                string n = Path.GetFileName(f).ToLowerInvariant();
                int s = 0;
                if (n.Contains("한글") || n.Contains("패치") || n.Contains("kor")) s += 4;
                if (n.Contains("read") || n.Contains("읽") || n.Contains("説明") || n.Contains("はじめ") || n.Contains("설명")) s += 2;
                return s;
            }).Take(12);
        }

        static string Tidy(string s)
        {
            s = (s ?? "").Trim().Trim('"', '\'', '「', '」', '『', '』', '《', '》', '〈', '〉', '[', ']', '【', '】', ' ', '　');
            if (s.Length < 2 || s.Length > 40) return null;
            if (s.Contains("http") || s.Contains("@") || s.Contains("://")) return null;
            return s;
        }

        static Found FromReadme(GameInfo g)
        {
            string best = null, bestFrom = null; int bestScore = 0;
            foreach (var f in TextFiles(g.Folder))
            {
                string text;
                try
                {
                    if (new FileInfo(f).Length > 400000) continue;
                    TextLang lang;
                    text = TextDecode.ReadFile(f, out lang);
                }
                catch { continue; }
                if (string.IsNullOrEmpty(text)) continue;
                var lines = text.Replace("\r", "").Split('\n').Take(60).ToList();
                string fname = Path.GetFileName(f);
                for (int i = 0; i < lines.Count; i++)
                {
                    var m = Label.Match(lines[i]);
                    if (!m.Success) continue;
                    string name = Tidy(m.Groups[2].Value);
                    if (name == null || Meaningless(g, name)) continue;
                    int score = 5 + (Hangul.IsMatch(name) ? 10 : 0) + (m.Groups[1].Value.Contains("한") ? 5 : 0);
                    if (score > bestScore) { bestScore = score; best = name; bestFrom = fname; }
                }
                // 앞쪽 줄의 『한글 제목』
                for (int i = 0; i < Math.Min(15, lines.Count); i++)
                {
                    var m = Quoted.Match(lines[i]);
                    if (!m.Success) continue;
                    string name = Tidy(m.Groups[1].Value);
                    if (name == null || !Hangul.IsMatch(name) || Meaningless(g, name)) continue;
                    if (8 > bestScore) { bestScore = 8; best = name; bestFrom = fname; }
                }
                if (bestScore >= 15) break;
            }
            if (best == null) return null;
            // 한글판인데 찾은 게 한글이 아니고 원래 이름도 뜻이 있으면 안 바꿈
            if (!Hangul.IsMatch(best) && !Meaningless(g, g.Title)) return null;
            return new Found { Name = best, From = bestFrom };
        }

        // ───────── DLsite (RJ 번호) ─────────
        public static string FindCode(GameInfo g)
        {
            foreach (var s in new[] { g.RawTitle, g.Title, Path.GetFileName(g.Folder ?? "") })
            {
                if (string.IsNullOrEmpty(s)) continue;
                var m = CodeAny.Match(s);
                if (m.Success) return m.Value.ToUpperInvariant();
            }
            return null;
        }

        /// <summary>RJ37082 → RJ037082 (DLsite 번호는 6자리나 8자리로 0을 채움)</summary>
        public static List<string> CodeVariants(string code)
        {
            var m = Regex.Match(code, @"(?i)^([A-Z]+)(\d+)$");
            var l = new List<string>();
            if (!m.Success) { l.Add(code); return l; }
            string pre = m.Groups[1].Value.ToUpperInvariant(), num = m.Groups[2].Value;
            if (num.Length <= 6) l.Add(pre + num.PadLeft(6, '0'));
            if (num.Length <= 8) l.Add(pre + num.PadLeft(8, '0'));
            if (!l.Contains(pre + num)) l.Add(pre + num);
            return l;
        }

        static Found FromDlsite(string code)
        {
            foreach (var id in CodeVariants(code))
                foreach (var site in new[] { "maniax" })   // 어느 사이트 주소로 물어도 같은 답
                {
                    try
                    {
                        string json;
                        using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                        {
                            wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                            json = wc.DownloadString("https://www.dlsite.com/" + site + "/product/info/ajax?product_id=" + id + "&cdn_cache_min=1");
                        }
                        if (json == null || json.IndexOf("\"" + id + "\"", StringComparison.OrdinalIgnoreCase) < 0) continue;   // 없는 번호면 []
                        string name = TextDecode.JsonString(json, "work_name");
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        name = WebUtility.HtmlDecode(name).Trim();
                        string ko = null;
                        try { ko = Translate.Run(name); } catch { }
                        ko = Tidy(ko);
                        if (ko != null && Hangul.IsMatch(ko))
                            return new Found { Name = ko, From = "DLsite " + id + " · 원제 " + name + " (구글 번역)" };
                        return new Found { Name = name, From = "DLsite " + id };
                    }
                    catch (Exception ex) { Log.Info("DLsite 이름 찾기 " + id + " " + site + ": " + ex.Message); }
                }
            return null;
        }

        // ───────── 한꺼번에 ─────────
        public static volatile bool Busy;

        /// <summary>목록이 바뀐 뒤 뒤에서 조용히: 찾아볼 게임들의 이름을 찾아 바꿈</summary>
        public static void RunAll(List<GameInfo> games, bool online, Action<int> done)
        {
            if (Busy) return;
            var list = games.Where(Wants).ToList();
            if (list.Count == 0) { done?.Invoke(0); return; }
            Busy = true;
            var th = new System.Threading.Thread(() =>
            {
                int n = 0;
                try
                {
                    foreach (var g in list)
                    {
                        Found f = null;
                        try { f = Find(g, online); } catch (Exception ex) { Log.Error("이름 찾기 " + g.Title, ex); }
                        var st = GameData.Get(g);
                        if (f != null && f.Name != g.Title)
                        {
                            st.NameFrom = (g.RawTitle ?? g.Title) + " · " + f.From;
                            st.Title = "=" + f.Name;
                            g.Title = f.Name;
                            n++;
                            Log.Info("이름 찾음: " + st.NameFrom + " → " + f.Name);
                        }
                        else st.NameFrom = "-";
                    }
                }
                finally
                {
                    GameData.Save();
                    Busy = false;
                    done?.Invoke(n);
                }
            }) { IsBackground = true };
            th.Start();
        }

        /// <summary>정리 모드 이름 후보용 (읽어보기만, 빠름)</summary>
        public static string ReadmeName(GameInfo g)
        {
            try { var f = FromReadme(g); return f?.Name; } catch { return null; }
        }

        /// <summary>"이름 다시 찾기": 찾아본 표시를 지움</summary>
        public static void ResetTried(List<GameInfo> games)
        {
            foreach (var g in games) { var st = GameData.Get(g); if (st.NameFrom == "-") st.NameFrom = null; }
            GameData.Save();
        }
    }
}
