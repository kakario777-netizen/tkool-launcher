using System.Collections.Generic;
using System;
using System.IO;
using System.Text;

namespace TkoolLauncher
{
    enum TextLang { Unknown, Japanese, Korean, Other }

    /// <summary>
    /// 쯔꾸르 설정 파일 글자 판별: UTF-8 / 일본어(Shift-JIS) / 한국어(CP949)
    /// 일본어 제목이 깨지지 않도록 두 인코딩으로 모두 풀어보고 더 자연스러운 쪽을 고른다.
    /// </summary>
    static class TextDecode
    {
        static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);
        static Encoding sjis, cp949, eucjp;

        static Encoding Strict(int cp) =>
            Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        public static string ReadFile(string path, out TextLang lang)
        {
            lang = TextLang.Unknown;
            try { return Decode(File.ReadAllBytes(path), out lang); }
            catch { return null; }
        }

        public static string Decode(byte[] b, out TextLang lang)
        {
            lang = TextLang.Unknown;
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            {
                var s = Encoding.UTF8.GetString(b, 3, b.Length - 3);
                lang = LangOf(s);
                return s;
            }
            bool ascii = true;
            foreach (var x in b) if (x >= 0x80) { ascii = false; break; }
            if (ascii) return Encoding.ASCII.GetString(b);

            try
            {
                var s = Utf8Strict.GetString(b);
                lang = LangOf(s);
                return s;
            }
            catch { }

            // UTF-16 (BOM 있거나, 짝수 자리마다 0이 많으면)
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { var s16 = Encoding.Unicode.GetString(b, 2, b.Length - 2); lang = LangOf(s16); return s16; }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { var s16 = Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2); lang = LangOf(s16); return s16; }
            {
                int z = 0, n = Math.Min(b.Length, 4000) & ~1;
                for (int i = 1; i < n; i += 2) if (b[i] == 0) z++;
                if (n > 20 && z > n / 6) { var s16 = Encoding.Unicode.GetString(b); lang = LangOf(s16); return s16; }
            }

            // 일본어(Shift-JIS·EUC-JP)·한국어(CP949) 중 '말이 되는' 글자가 제일 많은 쪽 고르기
            // (EUC-JP를 CP949로 읽으면 자모·그리스 문자·드문 한자가 뒤섞인 깨진 글이 됨 → 점수가 낮음)
            if (sjis == null) { sjis = Strict(932); cp949 = Strict(949); try { eucjp = Strict(51932); } catch { try { eucjp = Strict(20932); } catch { } } }
            string best = null; int bestScore = int.MinValue; TextLang bestLang = TextLang.Unknown;
            var cands = new List<KeyValuePair<Encoding, TextLang>> { new KeyValuePair<Encoding, TextLang>(sjis, TextLang.Japanese), new KeyValuePair<Encoding, TextLang>(cp949, TextLang.Korean) };
            if (eucjp != null) cands.Add(new KeyValuePair<Encoding, TextLang>(eucjp, TextLang.Japanese));
            if (b.Length % 2 == 0) cands.Add(new KeyValuePair<Encoding, TextLang>(new UnicodeEncoding(false, false, true), TextLang.Unknown));   // BOM 없는 UTF-16
            foreach (var c in cands)
            {
                string t;
                try { t = c.Key.GetString(b); } catch { continue; }
                int sc = Score(t) + (c.Value == TextLang.Korean && LooksKorean(b) ? 5 : 0);
                if (sc > bestScore) { bestScore = sc; best = t; bestLang = c.Value; }
            }
            if (best != null) { lang = bestLang == TextLang.Unknown ? LangOf(best) : bestLang; return best; }
            lang = TextLang.Japanese;
            return Encoding.GetEncoding(932).GetString(b);
        }

        static bool[] common;
        static bool CommonHangul(char c)
        {
            if (common == null)
            {
                var t = new bool[11172];
                try
                {
                    var enc = Encoding.GetEncoding(949);
                    var buf = new char[1];
                    for (int i = 0; i < t.Length; i++)
                    {
                        buf[0] = (char)(0xAC00 + i);
                        var bb = enc.GetBytes(buf);
                        t[i] = bb.Length == 2 && bb[0] >= 0xB0 && bb[0] <= 0xC8 && bb[1] >= 0xA1;
                    }
                }
                catch { for (int i = 0; i < t.Length; i++) t[i] = true; }
                common = t;
            }
            return common[c - 0xAC00];
        }

        /// <summary>글이 제대로 풀렸는지 점수: 가나·흔한 한글은 +, 자모·그리스·키릴·반각 가나 덩어리·드문 기호는 −</summary>
        static int Score(string t)
        {
            int sc = 0;
            foreach (char c in t)
            {
                if (c == 0x30FB) sc -= 3;                                          // ・ (잘못 읽힌 글자 대신 자주 나옴)
                else if (c >= 0x3040 && c <= 0x30FF) sc += 3;                  // 히라가나·가타카나
                else if (c >= 0xAC00 && c <= 0xD7A3) sc += CommonHangul(c) ? 2 : -3;   // 흔한 한글(KS X 1001 2350자)만 +, 드문 글자(UHC 확장)는 깨진 글
                else if (c >= 0x4E00 && c <= 0x9FFF) sc += 1;                  // 한자
                else if (c >= 0x3131 && c <= 0x318E) sc -= 4;                  // 한글 자모 낱개 (깨진 글에 많음)
                else if (c >= 0x0370 && c <= 0x04FF) sc -= 4;                  // 그리스·키릴
                else if (c >= 0xFF61 && c <= 0xFF9F) sc -= 1;                  // 반각 가나 (SJIS 오독에 많음)
                else if (c >= 0x2460 && c <= 0x24FF || c >= 0x3200 && c <= 0x33FF) sc -= 2;   // 동그라미 숫자·단위 기호
                else if (c == '\uFFFD') sc -= 10;
                else if (c < 0x20 && c != '\r' && c != '\n' && c != '\t') sc -= 5;   // 제어 문자
                else if (c >= 0xD800 && c <= 0xF8FF) sc -= 5;                  // 짝 없는 대리·사용자 정의 영역
                else if (c >= 0xA000 && c <= 0xABFF) sc -= 3;                  // 이 문서에 나올 리 없는 문자들 (UTF-16 오독)
            }
            return sc;
        }

        /// <summary>
        /// 실제 한글 문서는 거의 다 KS X 1001 영역(두 바이트 모두 0xA1 이상)에 들어간다.
        /// 반면 일본어 Shift-JIS는 히라가나·가타카나·자주 쓰는 한자가 첫 바이트 0x81~0x9F라서
        /// CP949로 억지로 풀면 잘 안 쓰는 확장 한글(뻷, 룛 같은 글자)이 된다. 이 차이로 구분.
        /// </summary>
        static bool LooksKorean(byte[] b)
        {
            int ks = 0, other = 0;
            for (int i = 0; i < b.Length; i++)
            {
                byte x = b[i];
                if (x < 0x80) continue;
                byte y = i + 1 < b.Length ? b[i + 1] : (byte)0;
                if (x >= 0xA1 && x <= 0xFE && y >= 0xA1 && y <= 0xFE) { ks++; i++; }
                else if (x >= 0x81 && x <= 0xFE && x != 0xA0) { other++; i++; }
                else other++;
            }
            return ks > 0 && ks >= other * 3;
        }

        public static TextLang LangOf(string s)
        {
            if (string.IsNullOrEmpty(s)) return TextLang.Unknown;
            bool kana = false, hangul = false;
            foreach (char c in s)
            {
                if (c >= 0xAC00 && c <= 0xD7A3) hangul = true;
                else if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0xFF66 && c <= 0xFF9F)) kana = true;
            }
            if (hangul) return TextLang.Korean;
            if (kana) return TextLang.Japanese;
            return TextLang.Other;
        }

        /// <summary>INI에서 [section] 아래 key 값 읽기</summary>
        public static string IniValue(string text, string section, string key)
        {
            if (text == null) return null;
            bool inSec = section == null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("["))
                {
                    inSec = section == null ||
                            string.Equals(line.Trim('[', ']', ' '), section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSec) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(eq + 1).Trim();
            }
            return null;
        }

        /// <summary>JSON에서 "key":"값" 형태의 문자열 하나 꺼내기 (작은 파일용 간이 파서)</summary>
        public static string JsonString(string json, string key)
        {
            if (json == null) return null;
            string pat = "\"" + key + "\"";
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            while (i >= 0)
            {
                int p = i + pat.Length;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                if (p < json.Length && json[p] == ':')
                {
                    p++;
                    while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                    if (p < json.Length && json[p] == '"') return ReadJsonStr(json, p + 1);
                }
                i = json.IndexOf(pat, i + 1, StringComparison.Ordinal);
            }
            return null;
        }

        static string ReadJsonStr(string s, int p)
        {
            var sb = new StringBuilder();
            while (p < s.Length)
            {
                char c = s[p++];
                if (c == '"') return sb.ToString();
                if (c != '\\' || p >= s.Length) { sb.Append(c); continue; }
                char e = s[p++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': break;
                    case 'u':
                        if (p + 4 <= s.Length &&
                            int.TryParse(s.Substring(p, 4), System.Globalization.NumberStyles.HexNumber, null, out int code))
                        { sb.Append((char)code); p += 4; }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }
    }
}
