using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace TkoolLauncher
{
    /// <summary>
    /// 구글 번역 (무료 웹 주소 translate.googleapis.com, client=gtx). 일본어·중국어 글을 한국어로.
    /// 결과는 cache\id_ko.txt 에 저장해 다시 번역하지 않음.
    /// </summary>
    static class Translate
    {
        static readonly Dictionary<string, string> mem = new Dictionary<string, string>();
        static readonly HashSet<string> busy = new HashSet<string>();
        static readonly object gate = new object();
        static int lastCall;
        public static Action Changed;

        /// <summary>일본어(가나)나 한자가 꽤 있고 한글이 적은 글인지</summary>
        public static bool Needs(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            int kana = 0, han = 0, hangul = 0, letters = 0, latin = 0;
            foreach (char c in s)
            {
                if (c >= 0x3040 && c <= 0x30FF) kana++;
                else if (c >= 0x4E00 && c <= 0x9FFF) han++;
                else if (c >= 0xAC00 && c <= 0xD7A3) hangul++;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
                if (char.IsLetter(c)) letters++;
            }
            int cjk = kana + han;
            if (cjk >= 8 && cjk > hangul * 2 && cjk * 5 >= letters) return true;   // 일본어·중국어
            return latin >= 30 && latin > hangul * 4;                               // 영어 등 (한글이 거의 없을 때)
        }

        /// <summary>번역이 있으면 글, 하는 중이면 null, 실패면 ""</summary>
        public static string Get(string key, string text)
        {
            lock (mem)
            {
                if (mem.TryGetValue(key, out string t)) return t;
                if (!busy.Add(key)) return null;
            }
            Task.Run(() =>
            {
                string r = "";
                string cp = Path.Combine(Settings.Dir, "cache", key + "_ko.txt");
                try
                {
                    if (File.Exists(cp)) r = File.ReadAllText(cp, Encoding.UTF8);
                    else
                    {
                        r = Run(text);
                        if (!string.IsNullOrEmpty(r)) { Directory.CreateDirectory(Path.GetDirectoryName(cp)); File.WriteAllText(cp, r, Encoding.UTF8); }
                    }
                }
                catch (Exception ex) { Log.Info("구글 번역 실패: " + ex.Message); r = ""; }
                lock (mem) { mem[key] = r ?? ""; busy.Remove(key); }
                // 실패는 잠깐만 기억했다가 다시 시도 (인터넷이 잠깐 끊겼거나 너무 자주 물어 막혔을 때)
                if (string.IsNullOrEmpty(r))
                    Task.Delay(30000).ContinueWith(_ => { lock (mem) { string v; if (mem.TryGetValue(key, out v) && v == "") mem.Remove(key); } try { Changed?.Invoke(); } catch { } });
                try { Changed?.Invoke(); } catch { }
            });
            return null;
        }

        /// <summary>글을 1200자 안팎 덩어리로 나눠 번역 (줄바꿈 유지)</summary>
        public static string Run(string text)
        {
            var lines = text.Replace("\r", "").Split('\n');
            var chunks = new List<string>();
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                if (sb.Length + l.Length > 1200 && sb.Length > 0) { chunks.Add(sb.ToString()); sb.Clear(); }
                sb.Append(l).Append('\n');
            }
            if (sb.Length > 0) chunks.Add(sb.ToString());
            var outSb = new StringBuilder();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            foreach (var c in chunks)
            {
                string part = null;
                Exception last = null;
                // 너무 자주 물으면 막혀서 한 번에 하나씩, 실패하면 조금 쉬었다 다시 (최대 3번)
                for (int attempt = 0; attempt < 3 && part == null; attempt++)
                {
                    lock (gate)
                    {
                        int wait = 250 - (Environment.TickCount - lastCall);
                        if (wait > 0) System.Threading.Thread.Sleep(wait);
                        try
                        {
                            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                            {
                                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                                string json;
                                if (attempt == 1 && c.Length < 1500)   // 두 번째는 GET으로
                                    json = wc.DownloadString("https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=ko&dt=t&q=" + Uri.EscapeDataString(c));
                                else
                                {
                                    wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded; charset=UTF-8";
                                    json = wc.UploadString("https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=ko&dt=t", "q=" + Uri.EscapeDataString(c));
                                }
                                part = Parse(json);
                            }
                        }
                        catch (Exception ex) { last = ex; }
                        finally { lastCall = Environment.TickCount; }
                    }
                    if (part == null) System.Threading.Thread.Sleep(1500 * (attempt + 1));
                }
                if (part == null) throw last ?? new Exception("번역 응답 없음");
                outSb.Append(part);
            }
            return outSb.ToString().TrimEnd();
        }

        // 응답 모양: [[["번역","원문",…],["번역","원문",…]…], …] → 첫 배열의 각 조각 [0]을 이어 붙임
        static string Parse(string json)
        {
            int i = 0;
            var root = Value(json, ref i) as List<object>;
            if (root == null || root.Count == 0) return "";
            var segs = root[0] as List<object>;
            if (segs == null) return "";
            var sb = new StringBuilder();
            foreach (var s in segs) { var seg = s as List<object>; if (seg != null && seg.Count > 0 && seg[0] is string) sb.Append((string)seg[0]); }
            return sb.ToString();
        }

        static object Value(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '[')
            {
                i++;
                var list = new List<object>();
                while (true)
                {
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                    if (i >= s.Length) return list;
                    if (s[i] == ']') { i++; return list; }
                    if (s[i] == ',') { i++; continue; }
                    list.Add(Value(s, ref i));
                }
            }
            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        char e = s[++i];
                        if (e == 'n') sb.Append('\n');
                        else if (e == 't') sb.Append('\t');
                        else if (e == 'r') { }
                        else if (e == 'u' && i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        else sb.Append(e);
                        i++;
                    }
                    else sb.Append(s[i++]);
                }
                i++;
                return sb.ToString();
            }
            if (c == '{')   // 쓰지 않음 — 건너뛰기
            {
                int depth = 0;
                for (; i < s.Length; i++) { if (s[i] == '{') depth++; else if (s[i] == '}' && --depth == 0) { i++; break; } }
                return null;
            }
            int st = i;
            while (i < s.Length && s[i] != ',' && s[i] != ']') i++;
            return s.Substring(st, i - st).Trim();
        }
    }
}
