using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TkoolLauncher
{
    /// <summary>
    /// 폴더·이름에 RJ 번호가 있는 게임: DLsite에서 작품 정보(원제·서클·장르·짧은 소개·대표 그림)를 가져옴.
    /// 장르는 구글 번역해 한글로. 캐시: cache\dlsite\RJ번호.txt (한 번만 받음, 없는 번호는 "-" 표시)
    /// 쓰는 곳: 상세 화면 장르·서클 배지, 소개 글 없을 때 소개, 표지 없을 때 대표 그림, 검색.
    /// </summary>
    static class DlsiteInfo
    {
        public class Info { public string Id, Name, Circle, Desc, Image, Date; public List<string> Genres = new List<string>(), GenresKo = new List<string>(); }

        static readonly Dictionary<string, Info> mem = new Dictionary<string, Info>();
        static string Dir => Path.Combine(Settings.Dir, "cache", "dlsite");

        /// <summary>받아 둔 정보 (없으면 null) — 화면에서 바로 써도 됨</summary>
        public static Info Get(GameInfo g)
        {
            string code = NameFinder.FindCode(g);
            if (code == null) return null;
            lock (mem)
            {
                Info i;
                if (mem.TryGetValue(code, out i)) return i;
            }
            var r = LoadCache(code);
            lock (mem) mem[code] = r;
            return r;
        }

        static Info LoadCache(string code)
        {
            try
            {
                string p = Path.Combine(Dir, code + ".txt");
                if (!File.Exists(p)) return null;
                var i = new Info();
                foreach (var line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int k = line.IndexOf('=');
                    if (k < 0) continue;
                    string key = line.Substring(0, k), v = line.Substring(k + 1).Replace("\\n", "\n");
                    switch (key)
                    {
                        case "id": i.Id = v; break;
                        case "name": i.Name = v; break;
                        case "circle": i.Circle = v; break;
                        case "desc": i.Desc = v; break;
                        case "image": i.Image = v; break;
                        case "date": i.Date = v; break;
                        case "genres": i.Genres = v.Split('|').Where(x => x.Length > 0).ToList(); break;
                        case "genres_ko": i.GenresKo = v.Split('|').Where(x => x.Length > 0).ToList(); break;
                    }
                }
                return i.Id == null ? null : i;
            }
            catch { return null; }
        }

        static bool Tried(string code) => File.Exists(Path.Combine(Dir, code + ".txt")) || File.Exists(Path.Combine(Dir, code + ".none"));

        static string Http(string url)
        {
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                wc.Headers[HttpRequestHeader.Cookie] = "adultchecked=1; locale=ja_JP";
                return wc.DownloadString(url);
            }
        }

        static readonly Regex GenreA = new Regex(@"<a[^>]*href=""[^""]*/fsr/=/genre/\d+/from/work\.genre[^""]*""[^>]*>([\s\S]{0,120}?)</a>");
        static readonly Regex Maker = new Regex(@"class=""c-productInfo__makerName""[^>]*>([\s\S]{0,200}?)</a>");
        static readonly Regex MetaDesc = new Regex(@"<meta name=""description"" content=""([^""]*)""");
        static string StripTags(string s) => WebUtility.HtmlDecode(Regex.Replace(s ?? "", @"<!--[\s\S]*?-->|<[^>]+>", "")).Trim();

        static Info Fetch(string code)
        {
            foreach (var id in NameFinder.CodeVariants(code))
            {
                string json;
                try { json = Http("https://www.dlsite.com/maniax/product/info/ajax?product_id=" + id + "&cdn_cache_min=1"); }
                catch (Exception ex) { Log.Info("DLsite 정보 " + id + ": " + ex.Message); continue; }
                if (json == null || json.IndexOf("\"" + id + "\"", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var i = new Info { Id = id };
                i.Name = WebUtility.HtmlDecode(TextDecode.JsonString(json, "work_name") ?? "").Trim();
                string img = TextDecode.JsonString(json, "work_image");
                if (!string.IsNullOrEmpty(img)) i.Image = img.StartsWith("//") ? "https:" + img : img;
                i.Date = (TextDecode.JsonString(json, "regist_date") ?? "").Split(' ')[0];
                // 작품 페이지: 장르·서클·짧은 소개
                try
                {
                    string html = Http("https://www.dlsite.com/maniax/work/=/product_id/" + id + ".html");
                    foreach (Match m in GenreA.Matches(html))
                    {
                        string t = StripTags(m.Groups[1].Value);
                        if (t.Length > 0 && t.Length < 30 && !i.Genres.Contains(t)) i.Genres.Add(t);
                    }
                    var mk = Maker.Match(html);
                    if (mk.Success) i.Circle = StripTags(mk.Groups[1].Value);
                    var md = MetaDesc.Match(html);
                    if (md.Success)
                    {
                        string d = WebUtility.HtmlDecode(md.Groups[1].Value);
                        int cut = d.IndexOf("「DLsite");
                        if (cut > 0) d = d.Substring(0, cut);
                        i.Desc = d.Trim();
                    }
                }
                catch (Exception ex) { Log.Info("DLsite 작품 페이지 " + id + ": " + ex.Message); }
                // 장르 한글로 (한 번에)
                if (i.Genres.Count > 0)
                {
                    try
                    {
                        string ko = Translate.Run(string.Join("\n", i.Genres));
                        var parts = (ko ?? "").Replace("\r", "").Split('\n').Select(x => x.Trim()).ToList();
                        if (parts.Count == i.Genres.Count) i.GenresKo = parts;
                    }
                    catch { }
                }
                return i;
            }
            return null;
        }

        static void Save(string code, Info i)
        {
            Directory.CreateDirectory(Dir);
            if (i == null) { File.WriteAllText(Path.Combine(Dir, code + ".none"), ""); return; }
            Func<string, string> e = s => (s ?? "").Replace("\r", "").Replace("\n", "\\n");
            var sb = new StringBuilder();
            sb.AppendLine("id=" + i.Id).AppendLine("name=" + e(i.Name)).AppendLine("circle=" + e(i.Circle)).AppendLine("desc=" + e(i.Desc))
              .AppendLine("image=" + e(i.Image)).AppendLine("date=" + e(i.Date))
              .AppendLine("genres=" + string.Join("|", i.Genres)).AppendLine("genres_ko=" + string.Join("|", i.GenresKo));
            File.WriteAllText(Path.Combine(Dir, code + ".txt"), sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>표지가 없으면 DLsite 대표 그림을 표지로 (covers\폴더.auto.png)</summary>
        static bool SaveCover(GameInfo g, Info i)
        {
            if (string.IsNullOrEmpty(i.Image) || g.Folder == null) return false;
            // 이미 표지(직접·찍은 것)가 있으면 그대로
            string name = Path.GetFileName(g.Folder);
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".auto.png", ".pending.png" })
                if (File.Exists(Path.Combine(CoverArt.CoverDir, name + ext))) return false;
            // 게임 안 타이틀 그림이 있으면 그걸 씀 — 그림을 못 찾은 게임만 (아직 안 만들어 봤으면 지금 확인)
            if (!CoverArt.Needs(g) && !CoverArt.HasNoHero(g))
            {
                try { using (var t = ArtExtract.FindTitleImage(g)) if (t != null) return false; } catch { }
            }
            string dest = CoverArt.AutoShotPath(g);
            try
            {
                byte[] b;
                using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0"; b = wc.DownloadData(i.Image); }
                using (var ms = new MemoryStream(b))
                using (var bmp = new Bitmap(ms))
                {
                    Directory.CreateDirectory(CoverArt.CoverDir);
                    bmp.Save(dest, ImageFormat.Png);
                }
                CoverArt.Invalidate(g);
                return true;
            }
            catch (Exception ex) { Log.Info("DLsite 그림 " + i.Id + ": " + ex.Message); return false; }
        }

        public static volatile bool Busy;

        /// <summary>RJ 번호가 있는 쯔꾸르 폴더 게임 중 아직 안 받은 것만 뒤에서 받음</summary>
        public static void RunAll(List<GameInfo> games, Action<int> done)
        {
            if (Busy) return;
            var withCode = games.Where(g => g.FromFolder && NameFinder.FindCode(g) != null).ToList();
            var list = withCode.Where(g => !Tried(NameFinder.FindCode(g))).ToList();
            // 예전에 정보만 받고 그림을 못 넣은 게임 (표지 없음) → 그림만 다시
            var needImg = withCode.Where(g => Tried(NameFinder.FindCode(g))).ToList();
            if (list.Count == 0 && needImg.Count == 0) { done?.Invoke(0); return; }
            Busy = true;
            var th = new System.Threading.Thread(() =>
            {
                int n = 0;
                try
                {
                    int k = 0;
                    foreach (var g in list)
                    {
                        Activity.Set("dlsite", "DLsite 정보 받는 중 · " + g.Title, ++k, list.Count);
                        string code = NameFinder.FindCode(g);
                        if (Tried(code)) continue;
                        Info i = null;
                        try { i = Fetch(code); } catch (Exception ex) { Log.Error("DLsite 정보 " + code, ex); }
                        try { Save(code, i); } catch { }
                        lock (mem) mem[code] = i;
                        if (i != null) { n++; SaveCover(g, i); }
                        System.Threading.Thread.Sleep(400);   // 사이트에 부담 안 주게 천천히
                    }
                    k = 0;
                    foreach (var g in needImg)
                    {
                        Activity.Set("dlsite", "DLsite 그림 확인 중", ++k, needImg.Count);
                        var i = Get(g);
                        if (i != null && SaveCover(g, i)) { n++; System.Threading.Thread.Sleep(300); }
                    }
                }
                finally { Activity.Done("dlsite"); Busy = false; done?.Invoke(n); }
            }) { IsBackground = true };
            th.Start();
        }
    }
}
