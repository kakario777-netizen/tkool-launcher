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
    /// DLsite에 없는 게임: 스팀(한국어 상점)·ふりーむ(일본 무료 게임, 쯔꾸르 많음)에서 이름으로 찾아 정보 채우기.
    /// 이름이 같을 때만 붙임. 결과는 DlsiteInfo.Info 모양으로 cache\web\게임id.txt (못 찾으면 .none) — 한 번만.
    /// 화면·검색·태그·표지는 DlsiteInfo.Get이 이걸 이어 받아 그대로 씀. (크롬으로 두 사이트 응답 확인함)
    /// </summary>
    static class WebInfo
    {
        static string Dir => Path.Combine(Settings.Dir, "cache", "web");
        static readonly Dictionary<string, DlsiteInfo.Info> mem = new Dictionary<string, DlsiteInfo.Info>();

        public static DlsiteInfo.Info Get(GameInfo g)
        {
            if (g == null || g.Exe == null) return null;
            string id = GameData.IdOf(g);
            lock (mem)
            {
                DlsiteInfo.Info i;
                if (mem.TryGetValue(id, out i)) return i;
            }
            var r = Load(id);
            lock (mem) mem[id] = r;
            return r;
        }

        static DlsiteInfo.Info Load(string id)
        {
            try
            {
                string p = Path.Combine(Dir, id + ".txt");
                if (!File.Exists(p)) return null;
                var i = new DlsiteInfo.Info();
                foreach (var line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int k = line.IndexOf('=');
                    if (k < 0) continue;
                    string key = line.Substring(0, k), v = line.Substring(k + 1).Replace("\\n", "\n");
                    if (key == "id") i.Id = v; else if (key == "name") i.Name = v; else if (key == "circle") i.Circle = v;
                    else if (key == "desc") i.Desc = v; else if (key == "image") i.Image = v; else if (key == "date") i.Date = v;
                    else if (key == "genres") i.Genres = v.Split('|').Where(x => x.Length > 0).ToList();
                }
                return i.Id == null ? null : i;
            }
            catch { return null; }
        }

        static void Save(string id, DlsiteInfo.Info i)
        {
            Directory.CreateDirectory(Dir);
            if (i == null) { File.WriteAllText(Path.Combine(Dir, id + ".none"), NoneVer); return; }
            Func<string, string> e = s => (s ?? "").Replace("\r", "").Replace("\n", "\\n");
            var sb = new StringBuilder();
            sb.AppendLine("id=" + i.Id).AppendLine("name=" + e(i.Name)).AppendLine("circle=" + e(i.Circle)).AppendLine("desc=" + e(i.Desc))
              .AppendLine("image=" + e(i.Image)).AppendLine("date=" + e(i.Date)).AppendLine("genres=" + string.Join("|", i.Genres));
            File.WriteAllText(Path.Combine(Dir, id + ".txt"), sb.ToString(), new UTF8Encoding(false));
        }

        // 못 찾음 표시는 찾아본 사이트 묶음 판(v2 = 스팀·FANZA·ふりーむ·itch.io + 게임 안 흔적) — 예전 판 표시는 다시 찾아봄
        const string NoneVer = "v2";
        static bool Tried(string id)
        {
            if (File.Exists(Path.Combine(Dir, id + ".txt"))) return true;
            string n = Path.Combine(Dir, id + ".none");
            try { return File.Exists(n) && File.ReadAllText(n).Trim() == NoneVer; } catch { return false; }
        }

        static string Http(string url)
        {
            using (var wc = new QuickWeb { Encoding = Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                wc.Headers[HttpRequestHeader.AcceptLanguage] = "ko-KR,ko;q=0.9,ja;q=0.8";
                return wc.DownloadString(url);
            }
        }

        static string Norm(string s) => new string(WebUtility.HtmlDecode(s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>이름이 같거나 한쪽이 다른 쪽을 품고 길이가 거의 같을 때만</summary>
        static bool Same(string a, string b)
        {
            string x = Norm(a), y = Norm(b);
            if (x.Length < 2 || y.Length < 2) return false;
            if (x == y) return true;
            return Math.Min(x.Length, y.Length) >= 4 && (x.Contains(y) || y.Contains(x)) && Math.Min(x.Length, y.Length) * 10 >= Math.Max(x.Length, y.Length) * 8;
        }

        /// <summary>찾아볼 이름들: 게임 데이터 이름·정리한 이름·지금 이름·폴더 이름</summary>
        static List<string> Names(GameInfo g)
        {
            var l = new List<string>();
            var src = new List<string>();
            try { src.AddRange(GameIdent.InnerNames(g)); } catch { }   // 게임 안 진짜 이름 먼저 (올린 사람이 바꾸지 않는 것)
            src.AddRange(new[] { g.RawTitle, TitleTidy.Clean(g.RawTitle ?? "", new List<string>()), g.Title, TitleTidy.Clean(Path.GetFileName((g.Folder ?? "").TrimEnd('\\', '/')), new List<string>()) });
            foreach (var s in src)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                string t = s.Trim();
                if (NameFinder.Meaningless(g, t) || NameFinder.BadName(t) || l.Any(x => Norm(x) == Norm(t))) continue;
                l.Add(t);
            }
            return l.Take(4).ToList();
        }

        // ───────── 스팀 ─────────
        static readonly Regex SteamType = new Regex(@"""type""\s*:\s*""(app|dlc|sub)""");
        static readonly Regex SteamId = new Regex(@"""id""\s*:\s*(\d+)");
        static readonly Regex SteamName = new Regex(@"""name""\s*:\s*""((?:[^""\\]|\\.)*)""");

        static DlsiteInfo.Info Steam(List<string> names)
        {
            foreach (var name in names)
            {
              // 한국 상점은 지역 제한으로 안 보이는 게임이 많아 일본·미국 상점으로
              foreach (var store in new[] { "cc=JP&l=japanese", "cc=US&l=english" })
              {
                string json;
                try { json = Http("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(name) + "&" + store); }
                catch (Exception ex) { Log.Info("스팀 검색 " + name + ": " + ex.Message); continue; }
                // 항목마다 잘라서 (가격 같은 안쪽 { } 때문에 통째 정규식은 안 됨)
                var starts = SteamType.Matches(json).Cast<Match>().Select(x => x.Index).ToList();
                for (int si = 0; si < starts.Count; si++)
                {
                    string seg = json.Substring(starts[si], (si + 1 < starts.Count ? starts[si + 1] : json.Length) - starts[si]);
                    var mi = SteamId.Match(seg); var mn = SteamName.Match(seg);
                    if (!mi.Success || !mn.Success) continue;
                    string found = Regex.Unescape(mn.Groups[1].Value);
                    if (!Same(found, name)) continue;
                    string app = mi.Groups[1].Value;
                    var si2 = SteamApp(app, found);
                    if (si2 != null) { Log.Info("스팀에서 찾음: " + name + " → " + app + " " + si2.Name); return si2; }

                }
                System.Threading.Thread.Sleep(400);
              }
            }
            return null;
        }

        /// <summary>스팀 앱 번호로 정보 (한국어 설명 있으면 한국어)</summary>
        static DlsiteInfo.Info SteamApp(string app, string fallback)
        {
            try
            {
                        // 한국어 설명이 있으면 한국어, 일본 상점에서 막히면 미국 상점(영어)
                        string d = Http("https://store.steampowered.com/api/appdetails?appids=" + app + "&cc=JP&l=korean");
                        if (d.Contains("\"success\":false")) d = Http("https://store.steampowered.com/api/appdetails?appids=" + app + "&cc=US&l=korean");
                        if (d.Contains("\"success\":false")) return null;
                        var i = new DlsiteInfo.Info { Id = "Steam " + app, Name = TextDecode.JsonString(d, "name") ?? fallback };
                        i.Desc = WebUtility.HtmlDecode(Regex.Replace(TextDecode.JsonString(d, "short_description") ?? "", "<[^>]+>", "")).Trim();
                        i.Image = TextDecode.JsonString(d, "header_image");
                        int dv = d.IndexOf("\"developers\"");
                        if (dv > 0) { var dm = Regex.Match(d.Substring(dv), @"\[\s*""((?:[^""\\]|\\.)*)"""); if (dm.Success) i.Circle = Regex.Unescape(dm.Groups[1].Value); }
                        int gi = d.IndexOf("\"genres\"");
                        if (gi > 0)
                        {
                            int end = d.IndexOf(']', gi);
                            foreach (Match gm in Regex.Matches(d.Substring(gi, Math.Max(0, end - gi)), @"""description""\s*:\s*""((?:[^""\\]|\\.)*)"""))
                                i.Genres.Add(Regex.Unescape(gm.Groups[1].Value));
                        }
                        var rd = Regex.Match(d, @"""date""\s*:\s*""([^""]*)""");
                        if (rd.Success) i.Date = rd.Groups[1].Value;
                        return i;
            }
            catch (Exception ex) { Log.Info("스팀 정보 " + app + ": " + ex.Message); return null; }
        }

        // ───────── itch.io ─────────
        static readonly Regex ItchCell = new Regex(@"<a[^>]*class=""title game_link""[^>]*href=""(https://[^""]+)""[^>]*>([^<]+)</a>");
        static DlsiteInfo.Info Itch(List<string> names)
        {
            foreach (var name in names.Where(n => n.Any(c => c < 128 && char.IsLetter(c))))
            {
                string html;
                try { html = Http("https://itch.io/search?type=games&q=" + Uri.EscapeDataString(name)); }
                catch (Exception ex) { Log.Info("itch.io 검색 " + name + ": " + ex.Message); continue; }
                foreach (Match m in ItchCell.Matches(html))
                {
                    string found = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                    if (!Same(found, name)) continue;
                    try
                    {
                        string url = m.Groups[1].Value, page = Http(url);
                        var i = new DlsiteInfo.Info { Id = "itch " + url, Name = found };
                        var og = Regex.Match(page, @"<meta[^>]*content=""([^""]+)""[^>]*property=""og:image""|<meta[^>]*property=""og:image""[^>]*content=""([^""]+)""");
                        if (og.Success) i.Image = WebUtility.HtmlDecode(og.Groups[1].Success && og.Groups[1].Value.Length > 0 ? og.Groups[1].Value : og.Groups[2].Value);
                        var dd = Regex.Match(page, @"class=""formatted_description[^""]*""[^>]*>([\s\S]{0,4000}?)</div>");
                        if (dd.Success) { string t = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(dd.Groups[1].Value, @"<br\s*/?>|</p>", "\n"), "<[^>]+>", "")).Trim(); i.Desc = t.Length > 600 ? t.Substring(0, 600) + "…" : t; }
                        var au = Regex.Match(page, @"<td>Author</td><td><a[^>]*>([^<]+)</a>"); if (au.Success) i.Circle = WebUtility.HtmlDecode(au.Groups[1].Value);
                        foreach (var row in new[] { "Genre", "Tags" })
                        {
                            var gm = Regex.Match(page, "<td>" + row + @"</td><td>([\s\S]{0,800}?)</td>");
                            if (gm.Success) foreach (Match a in Regex.Matches(gm.Groups[1].Value, @">([^<]+)</a>")) if (i.Genres.Count < 8) i.Genres.Add(WebUtility.HtmlDecode(a.Groups[1].Value));
                        }
                        Log.Info("itch.io에서 찾음: " + name + " → " + url);
                        return i;
                    }
                    catch (Exception ex) { Log.Info("itch.io 정보: " + ex.Message); }
                }
                System.Threading.Thread.Sleep(400);
            }
            return null;
        }

        // ───────── FANZA 동인 (성인 사이트라 구조 직접 확인 못 함 — 표준 og 태그만 읽고, 막히면 이번 실행 동안 그만) ─────────
        static volatile bool fanzaOff;
        static readonly Regex FanzaCid = new Regex(@"/dc/doujin/-/detail/=/cid=(d_\d{5,7})");
        static DlsiteInfo.Info FanzaPage(string cid)
        {
            string page = HttpCookie("https://www.dmm.co.jp/dc/doujin/-/detail/=/cid=" + cid + "/");
            Func<string, string> Og = p =>
            {
                var m = Regex.Match(page, @"<meta[^>]*property=""og:" + p + @"""[^>]*content=""([^""]*)""|<meta[^>]*content=""([^""]*)""[^>]*property=""og:" + p + @"""");
                return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value).Trim() : null;
            };
            string title = Og("title");
            if (string.IsNullOrEmpty(title)) return null;
            title = Regex.Replace(title, @"\s*[|｜-]\s*(FANZA|DMM).*$", "").Trim();
            var i = new DlsiteInfo.Info { Id = "FANZA " + cid, Name = title, Image = Og("image"), Desc = Og("description") };
            foreach (Match a in Regex.Matches(page, @"/article=keyword/id=\d+/[^""]*""[^>]*>([^<]{1,30})</a>")) if (i.Genres.Count < 8 && !i.Genres.Contains(a.Groups[1].Value.Trim())) i.Genres.Add(WebUtility.HtmlDecode(a.Groups[1].Value.Trim()));
            var mk = Regex.Match(page, @"/article=maker/id=\d+/[^""]*""[^>]*>([^<]{1,40})</a>"); if (mk.Success) i.Circle = WebUtility.HtmlDecode(mk.Groups[1].Value.Trim());
            return i;
        }
        static DlsiteInfo.Info Fanza(string cid, List<string> names)
        {
            if (fanzaOff) return null;
            try
            {
                if (cid != null) { var i = FanzaPage(cid); if (i != null) { Log.Info("FANZA 번호로 찾음: " + cid); return i; } }
                foreach (var name in names.Where(n => n.Any(c => (c >= 0x3040 && c <= 0x30FF) || (c >= 0x4E00 && c <= 0x9FFF) || (c < 128 && char.IsLetter(c)))).Take(2))
                {
                    string html = HttpCookie("https://www.dmm.co.jp/dc/doujin/-/search/=/searchstr=" + Uri.EscapeDataString(name) + "/");
                    var cids = FanzaCid.Matches(html).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().Take(4).ToList();
                    if (cids.Count == 0 && !html.Contains("doujin")) { fanzaOff = true; Log.Info("FANZA: 지역 제한이나 연령 확인 화면으로 보여 이번엔 그만"); return null; }
                    foreach (var c in cids)
                    {
                        var i = FanzaPage(c);
                        if (i != null && Same(i.Name, name)) { Log.Info("FANZA에서 찾음: " + name + " → " + c); return i; }
                        System.Threading.Thread.Sleep(400);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Info("FANZA: " + ex.Message);
                if (ex is WebException we && we.Response is HttpWebResponse hr && ((int)hr.StatusCode == 403 || (int)hr.StatusCode == 451)) fanzaOff = true;
            }
            return null;
        }
        static string HttpCookie(string url)
        {
            using (var wc = new QuickWeb { Encoding = Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                wc.Headers[HttpRequestHeader.Cookie] = "age_check_done=1; ckcy=1; cklg=ja";
                wc.Headers[HttpRequestHeader.AcceptLanguage] = "ja";
                return wc.DownloadString(url);
            }
        }

        // ───────── ふりーむ (Freem!) ─────────
        static readonly Regex FreemItem = new Regex(@"href=""/win/game/(\d+)""[^>]*>([\s\S]{0,200}?)</a>");

        static DlsiteInfo.Info Freem(List<string> names)
        {
            foreach (var name in names.Where(n => n.Any(c => (c >= 0x3040 && c <= 0x30FF) || (c >= 0x4E00 && c <= 0x9FFF) || (c < 128 && char.IsLetter(c)))))
            {
                string html;
                try { html = Http("https://www.freem.ne.jp/search/" + Uri.EscapeDataString(name)); }
                catch (Exception ex) { Log.Info("ふりーむ 검색 " + name + ": " + ex.Message); continue; }
                foreach (Match m in FreemItem.Matches(html))
                {
                    string found = WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, "<[^>]+>", "")).Trim();
                    if (!Same(found, name)) continue;
                    string gid = m.Groups[1].Value;
                    try
                    {
                        string p = Http("https://www.freem.ne.jp/win/game/" + gid);
                        var i = new DlsiteInfo.Info { Id = "ふりーむ " + gid };
                        var h1 = Regex.Match(p, @"<h1[^>]*>([\s\S]{0,200}?)</h1>");
                        i.Name = h1.Success ? WebUtility.HtmlDecode(Regex.Replace(h1.Groups[1].Value, "<[^>]+>", "")).Trim() : found;
                        var img = Regex.Match(p, @"<meta property=""og:image"" content=""([^""]+)""");
                        if (img.Success) i.Image = img.Groups[1].Value;
                        var desc = Regex.Match(p, @"<meta name=""description"" content=""([^""]*)""");
                        if (desc.Success) i.Desc = WebUtility.HtmlDecode(desc.Groups[1].Value).Trim();
                        var gen = Regex.Match(p, @"ジャンル\s*】\s*([^<\r\n]{1,60})");
                        if (gen.Success)
                            foreach (var part in Regex.Split(WebUtility.HtmlDecode(gen.Groups[1].Value), @"[・/／、,，\s]+"))
                                if (part.Trim().Length > 1 && part.Trim().Length < 20) i.Genres.Add(part.Trim());
                        var tool = Regex.Match(p, @"(開発|制作)ツール\s*】\s*([^<\r\n]{1,40})");
                        if (tool.Success) i.Circle = null;
                        var auth = Regex.Match(p, @"作者[^<]{0,10}<[^>]*>\s*<a[^>]*>([^<]{1,40})</a>");
                        if (auth.Success) i.Circle = WebUtility.HtmlDecode(auth.Groups[1].Value).Trim();
                        Log.Info("ふりーむ에서 찾음: " + name + " → " + gid + " " + i.Name);
                        return i;
                    }
                    catch (Exception ex) { Log.Info("ふりーむ 정보 " + gid + ": " + ex.Message); }
                }
                System.Threading.Thread.Sleep(400);
            }
            return null;
        }

        public static volatile bool Busy;
        static volatile bool again;

        /// <summary>DLsite 정보가 없는 쯔꾸르 폴더 게임: 스팀 → ふりーむ 순으로 찾아봄 (한 번만, 흔적 남김)</summary>
        public static void RunAll(List<GameInfo> games, Action<int> done)
        {
            if (Busy) { again = true; return; }
            again = false;
            var list = games.Where(g => g.FromFolder && g.Exe != null && !Tried(GameData.IdOf(g)))
                            .Where(g => { string c = NameFinder.FindCode(g); return c == null || DlsiteInfo.Get(g) == null; }).ToList();
            if (list.Count == 0) { Activity.Note("스팀·ふりーむ · 새로 찾을 게임 없음"); done?.Invoke(0); return; }
            Busy = true;
            var th = new System.Threading.Thread(() =>
            {
                int n = 0, k = 0;
                try
                {
                    foreach (var g in list)
                    {
                        Activity.Set("web", "스팀·FANZA·ふりーむ·itch.io에서 찾는 중 · " + g.Title, ++k, list.Count);
                        string id = GameData.IdOf(g);
                        var names = Names(g);
                        DlsiteInfo.Info i = null;
                        string app = null, cid = null;
                        try { app = GameIdent.SteamApp(g); cid = GameIdent.DmmCid(g); } catch { }
                        if (app != null) try { i = SteamApp(app, g.Title); } catch { }   // steam_appid.txt — 이름이 바뀌어도 정확
                        if (i == null && cid != null) try { i = Fanza(cid, new List<string>()); } catch { }
                        if (i == null && names.Count > 0)
                        {
                            try { i = Steam(names); } catch (Exception ex) { Log.Error("스팀 찾기", ex); }
                            if (i == null) try { i = Fanza(null, names); } catch (Exception ex) { Log.Error("FANZA 찾기", ex); }
                            if (i == null) try { i = Freem(names); } catch (Exception ex) { Log.Error("ふりーむ 찾기", ex); }
                            if (i == null) try { i = Itch(names); } catch (Exception ex) { Log.Error("itch.io 찾기", ex); }
                        }
                        try { Save(id, i); } catch { }
                        lock (mem) mem[id] = i;
                        if (i != null)
                        {
                            n++;
                            foreach (var gn in i.Genres) DlsiteInfo.GenreKo(gn);   // 일본어 장르는 사전 번역 대기에
                            try { DlsiteInfo.SaveCover(g, i); } catch { }
                        }
                        System.Threading.Thread.Sleep(300);
                    }
                    DlsiteInfo.TranslateGenres();
                }
                finally
                {
                    Activity.Done("web", "스팀·ふりーむ 끝 · " + list.Count + "개 찾아봄, " + n + "개 채움");
                    Busy = false;
                    done?.Invoke(n);
                    if (again) RunAll(games, done);   // 도는 중에 새로 생긴 게임(DLsite에도 없던 것)
                }
            }) { IsBackground = true };
            th.Start();
        }
    }
}
