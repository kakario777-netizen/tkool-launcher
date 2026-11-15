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
        static Encoding sjis, cp949;

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

            if (sjis == null) { sjis = Strict(932); cp949 = Strict(949); }
            string j = null, k = null;
            try { j = sjis.GetString(b); } catch { }
            try { k = cp949.GetString(b); } catch { }

            if (j != null && k == null) { lang = TextLang.Japanese; return j; }
            if (k != null && j == null) { lang = TextLang.Korean; return k; }
            if (j == null && k == null)
            {
                lang = TextLang.Japanese;
                return Encoding.GetEncoding(932).GetString(b);
            }
            if (LooksKorean(b)) { lang = TextLang.Korean; return k; }
            lang = TextLang.Japanese;
            return j;
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
