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
        /// <summary>확인 끝 표시 (2판: 모든 게임 확인·외국어 이름 번역·DLsite 이름 검색까지)</summary>
        public const string Checked = "-2";

        /// <summary>찾아볼 게임: 아직 2판으로 확인 안 한 모든 쯔꾸르 폴더 게임 (사용자가 직접 고른 이름은 빼고)</summary>
        public static bool Wants(GameInfo g)
        {
            if (!g.FromFolder || g.Folder == null) return false;
            var st = GameData.Get(g);
            if (st.NameFrom != null && st.NameFrom != "-") return false;    // 2판으로 확인했거나 이미 찾아 바꿈
            if (st.Title != null && st.Title.StartsWith("=") && st.NameFrom == null) return false;  // 사용자가 고른 이름
            return true;
        }

        /// <summary>한글이 하나도 없는 외국어 이름 (일본어·영어 등)</summary>
        public static bool Foreign(string t) => !string.IsNullOrWhiteSpace(t) && !Hangul.IsMatch(t) && t.Any(char.IsLetter);

        public class Found { public string Name, From; public bool NeedTr; }   // NeedTr: 나중에 몰아서 한글로 번역

        /// <summary>이름 후보 찾기 (느릴 수 있음 — 백그라운드에서)</summary>
        public static Found Find(GameInfo g, bool online)
        {
            // 1) 읽어보기의 한글 제목
            var f = FromReadme(g);
            if (f != null && Hangul.IsMatch(f.Name)) return f;
            bool meaningless = Meaningless(g, g.Title);
            if (online)
            {
                // 2) RJ 번호 → DLsite 원제(+번역)
                string code = FindCode(g);
                if (code != null && (meaningless || Foreign(g.Title))) { var d = FromDlsite(code); if (d != null) return d; }
                // 3) 번호 없으면 원래 이름으로 DLsite 검색 (이름이 딱 맞을 때만)
                if (code == null) SearchDlsite(g);
                code = FindCode(g);
                if (code != null && meaningless) { var d = FromDlsite(code); if (d != null) return d; }
            }
            if (f != null) { f.NeedTr = online && Foreign(f.Name); return f; }   // 읽어보기 영어·일본어 제목 (뜻 없는 이름보다 나음)
            // 4) 외국어 이름은 구글 번역으로 한글 이름 (읽어보기 자동 번역처럼, 원래 이름은 정보에 남김) — 번역은 RunAll에서 몰아서
            if (online && Foreign(g.Title) && !meaningless) return new Found { Name = g.Title, From = "", NeedTr = true };
            return null;
        }

        // ───────── DLsite 이름 검색 (RJ 번호 없는 게임) ─────────
        static readonly Dictionary<string, string> found = new Dictionary<string, string>();   // 게임 id → RJ (찾은 것만)
        static bool foundLoaded;
        static string FoundPath => Path.Combine(Settings.Dir, "cache", "dlsite", "found.txt");
        static readonly Regex SearchItem = new Regex(@"<div class=""multiline_truncate"">\s*<a[^>]*product_id/((?:RJ|VJ|BJ)\d+)\.html""[^>]*title=""([^""]*)""");

        static void LoadFound()
        {
            if (foundLoaded) return;
            foundLoaded = true;
            try
            {
                if (!File.Exists(FoundPath)) return;
                foreach (var l in File.ReadAllLines(FoundPath, Encoding.UTF8))
                {
                    int k = l.IndexOf('=');
                    if (k > 0) found[l.Substring(0, k)] = l.Substring(k + 1);
                }
            }
            catch { }
        }

        static string Norm(string s) => new string(WebUtility.HtmlDecode(s ?? "").ToLowerInvariant().Where(c => char.IsLetterOrDigit(c)).ToArray());

        /// <summary>원래 이름(게임 데이터·폴더)으로 DLsite 검색, 이름이 같으면 번호를 기억 → DLsite 정보·그림이 그걸로 채워짐</summary>
        static void SearchDlsite(GameInfo g)
        {
            string id = GameData.IdOf(g);
            lock (found) { LoadFound(); if (found.ContainsKey(id)) return; }
            var names = new List<string>();
            foreach (var s in new[] { g.RawTitle, TitleTidy.Clean(g.RawTitle ?? "", new List<string>()), TitleTidy.Clean(Path.GetFileName(g.Folder ?? ""), new List<string>()) })
                if (!string.IsNullOrWhiteSpace(s) && Foreign(s) && !names.Contains(s.Trim()) && !Meaningless(g, s) && Norm(s).Length >= 2) names.Add(s.Trim());
            foreach (var name in names.Take(2))
            {
                string q = Norm(name);
                foreach (var site in new[] { "maniax", "home" })
                {
                    try
                    {
                        string json;
                        using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                        {
                            wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                            json = wc.DownloadString("https://www.dlsite.com/" + site + "/fsr/ajax/=/language/jp/keyword/" + Uri.EscapeDataString(name) + "/per_page/30/");
                        }
                        string html = TextDecode.JsonString(json, "search_result") ?? "";
                        foreach (Match m in SearchItem.Matches(html))
                        {
                            string t = Norm(m.Groups[2].Value);
                            // 이름이 똑같거나, 한쪽이 다른 쪽을 품고 길이가 거의 같을 때만 (엉뚱한 작품 막기)
                            bool ok = t == q || (q.Length >= 4 && (t.Contains(q) || q.Contains(t)) && Math.Min(t.Length, q.Length) * 10 >= Math.Max(t.Length, q.Length) * 8);
                            if (!ok) continue;
                            string code = m.Groups[1].Value.ToUpperInvariant();
                            lock (found)
                            {
                                found[id] = code;
                                try { Directory.CreateDirectory(Path.GetDirectoryName(FoundPath)); File.AppendAllText(FoundPath, id + "=" + code + "\r\n", new UTF8Encoding(false)); } catch { }
                            }
                            Log.Info("DLsite 검색으로 찾음: " + name + " → " + code + " " + WebUtility.HtmlDecode(m.Groups[2].Value));
                            return;
                        }
                    }
                    catch (Exception ex) { Log.Info("DLsite 검색 " + name + ": " + ex.Message); }
                    System.Threading.Thread.Sleep(300);
                }
            }
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
            lock (found)
            {
                LoadFound();
                string c;
                if (g.Exe != null && found.TryGetValue(GameData.IdOf(g), out c)) return c;
            }
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
                        return new Found { Name = name, From = "DLsite " + id + " · 원제 " + name, NeedTr = Foreign(name) };
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
                    // 1) 게임마다 찾기 (읽어보기·DLsite) — 번역이 필요한 건 모아 둠
                    var pend = new List<KeyValuePair<GameInfo, Found>>();
                    int k = 0;
                    foreach (var g in list)
                    {
                        Activity.Set("name", "이름 확인 중 · " + g.Title, ++k, list.Count);
                        Found f = null;
                        try { f = Find(g, online); } catch (Exception ex) { Log.Error("이름 찾기 " + g.Title, ex); }
                        if (f != null && f.NeedTr) pend.Add(new KeyValuePair<GameInfo, Found>(g, f));
                        else if (Apply(g, f)) n++;
                        if (k % 50 == 0) GameData.Save();
                    }
                    // 2) 번역은 몰아서: 이름 여러 개를 줄바꿈으로 이어 한 번에 보내고, 묶음 사이에 쉬기
                    int done2 = 0;
                    for (int i = 0; i < pend.Count; )
                    {
                        var batch = new List<KeyValuePair<GameInfo, Found>>();
                        int len = 0;
                        while (i < pend.Count && batch.Count < 40 && len + pend[i].Value.Name.Length < 1100)
                        { len += pend[i].Value.Name.Length + 1; batch.Add(pend[i]); i++; }
                        if (batch.Count == 0) { batch.Add(pend[i]); i++; }
                        Activity.Set("name", "이름 한글로 번역 중", done2, pend.Count);
                        List<string> kos = null;
                        try
                        {
                            string joined = string.Join("\n", batch.Select(x => x.Value.Name.Replace("\n", " ")));
                            kos = (Translate.Run(joined) ?? "").Replace("\r", "").Split('\n').Select(x => x.Trim()).ToList();
                            if (kos.Count != batch.Count) { Log.Info("이름 몰아 번역: 줄 수가 달라 하나씩 다시 (" + kos.Count + "/" + batch.Count + ")"); kos = null; }
                        }
                        catch (Exception ex) { Log.Info("이름 몰아 번역 실패: " + ex.Message); }
                        if (kos == null)   // 줄이 섞였거나 실패 → 이 묶음만 하나씩
                        {
                            kos = new List<string>();
                            foreach (var x in batch) { string t = null; try { t = Translate.Run(x.Value.Name); } catch { } kos.Add((t ?? "").Trim()); }
                        }
                        for (int j = 0; j < batch.Count; j++)
                        {
                            var g = batch[j].Key; var f = batch[j].Value;
                            string ko = Tidy(kos[j]);
                            bool okKo = ko != null && Hangul.IsMatch(ko) && ko != f.Name;
                            if (okKo)
                            {
                                f.From = f.From.Length == 0 ? "구글 번역" : f.From + " (구글 번역)";
                                f.Name = ko;
                                if (Apply(g, f)) n++;
                            }
                            else if (f.From.Length > 0) { if (Apply(g, f)) n++; }   // 번역은 안 됐지만 DLsite·읽어보기 이름은 씀
                            // 제목 번역만 실패한 건 표시 안 함 → 다음에 다시 시도
                        }
                        done2 += batch.Count;
                        GameData.Save();
                        System.Threading.Thread.Sleep(2000);   // 묶음 사이에 쉬기
                    }
                }
                finally
                {
                    GameData.Save();
                    Activity.Done("name");
                    Busy = false;
                    done?.Invoke(n);
                }
            }) { IsBackground = true };
            th.Start();
        }

        /// <summary>찾은 이름 적용 (없으면 확인 흔적만)</summary>
        static bool Apply(GameInfo g, Found f)
        {
            var st = GameData.Get(g);
            if (f != null && !string.IsNullOrWhiteSpace(f.Name) && f.Name != g.Title)
            {
                st.NameFrom = (g.RawTitle ?? g.Title) + " · " + f.From;
                st.Title = "=" + f.Name;
                g.Title = f.Name;
                Log.Info("이름 찾음: " + st.NameFrom + " → " + f.Name);
                return true;
            }
            st.NameFrom = Checked;   // 확인한 흔적
            return false;
        }

        /// <summary>정리 모드 이름 후보용 (읽어보기만, 빠름)</summary>
        public static string ReadmeName(GameInfo g)
        {
            try { var f = FromReadme(g); return f?.Name; } catch { return null; }
        }

        /// <summary>"이름 다시 찾기": 찾아본 표시를 지움</summary>
        public static void ResetTried(List<GameInfo> games)
        {
            foreach (var g in games) { var st = GameData.Get(g); if (st.NameFrom == "-" || st.NameFrom == Checked) st.NameFrom = null; }
            GameData.Save();
        }
    }
}
