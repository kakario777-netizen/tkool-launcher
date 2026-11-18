using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TkoolLauncher
{
    /// <summary>
    /// 쯔꾸르 게임 이름 정리: 버전·날짜·[괄호 꼬리표]·_kr·한글패치 같은 걸 떼어 내고 깔끔한 이름만 남김.
    /// 떼어 낸 정보 중 쓸모 있는 건 꼬리표(한글·체험판)로 돌려줌.
    /// 예) "[2015]魔女の家_ver1.08_한글패치" → "魔女の家" + [한글]
    /// </summary>
    static class TitleTidy
    {
        static readonly Regex Brackets = new Regex(@"[\[\(【（〔「『<＜][^\]\)】）〕」』>＞]{0,40}[\]\)】）〕」』>＞]");
        static readonly Regex Version = new Regex(@"(?i)(?<![A-Za-z])(?:ver(?:sion)?\.?\s*|v)\s*\d+(?:[\.\-_]\d+)*[a-z]?(?=$|[^0-9A-Za-z])");
        static readonly Regex BareVersion = new Regex(@"(?:^|[\s_\-])\d+\.\d+(?:\.\d+)*[a-z]?(?=$|[\s_\-])");
        static readonly Regex Date = new Regex(@"(?:^|[\s_\-])(?:19|20)\d{2}(?:[\.\-_/]?\d{2}){0,2}(?=$|[\s_\-])");
        static readonly Regex KoTag = new Regex(@"(?i)(한글\s*패치|한글\s*판|한글화|한국어\s*판?|한글|\bkor(?:ean)?\b|(?<=[\s_\-])kr\b|^kr\b)");
        static readonly Regex TrialTag = new Regex(@"(?i)(체험판|体験版|\btrial\b|\bdemo\b|데모)");
        static readonly Regex Noise = new Regex(@"(?i)(完成版|완성판|最終版|최종판|修正版|수정판|\bfix(?:ed)?\b|\bfinal\b|\bfull\b|\bupdate\b|업데이트)");

        public static string Clean(string raw, List<string> tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            string s = raw.Trim();

            // 괄호 안이 꼬리표면 떼고, 이름 자체가 괄호뿐이면 안의 글자를 씀
            s = Brackets.Replace(s, m =>
            {
                string inner = m.Value.Substring(1, m.Value.Length - 2);
                if (KoTag.IsMatch(inner)) Add(tags, "한글");
                if (TrialTag.IsMatch(inner)) Add(tags, "체험판");
                return " ";
            });
            if (s.Trim().Length == 0) s = Brackets.Match(raw).Success ? raw.Trim().Trim('[', ']', '(', ')', '【', '】') : raw;

            if (KoTag.IsMatch(s)) { Add(tags, "한글"); s = KoTag.Replace(s, " "); }
            if (TrialTag.IsMatch(s)) { Add(tags, "체험판"); s = TrialTag.Replace(s, " "); }
            s = Noise.Replace(s, " ");
            s = Version.Replace(s, " ");
            s = BareVersion.Replace(s, " ");
            s = Date.Replace(s, " ");
            s = s.Replace('_', ' ').Replace('　', ' ');
            s = Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '-', '－', '~', '～', '.', '·', '+', '&');
            return s.Length == 0 ? raw.Trim() : s;
        }

        static void Add(List<string> tags, string t) { if (!tags.Contains(t)) tags.Add(t); }
    }
}
