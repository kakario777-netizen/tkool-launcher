using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace TkoolLauncher
{
    /// <summary>
    /// 패드·터치로 쓰기 쉬운 초성 검색. 한글은 초성(ㅁㄴㅇㅈ → 마녀의 집), 영어는 글자,
    /// 일본어는 あかさたな 줄로 찾는다. 키보드 없이도 몇 번만 누르면 됨.
    /// </summary>
    class SearchScreen : Screen
    {
        /// <summary>DLsite 원제·서클·장르(한글)로도 찾기</summary>
        static bool DlMatch(GameInfo g, string q)
        {
            var d = DlsiteInfo.Get(g);
            if (d == null) return false;
            foreach (var t in new[] { d.Name, d.Circle }.Concat(d.Genres).Concat(d.Genres.Select(DlsiteInfo.GenreKo)))
                if (!string.IsNullOrEmpty(t) && t.ToUpperInvariant().Contains(q)) return true;
            return false;
        }

        static readonly string[][] Keys =
        {
            new[] { "ㄱ","ㄴ","ㄷ","ㄹ","ㅁ","ㅂ","ㅅ","ㅇ","ㅈ","ㅊ","ㅋ","ㅌ","ㅍ","ㅎ" },
            new[] { "A","B","C","D","E","F","G","H","I","J","K","L","M" },
            new[] { "N","O","P","Q","R","S","T","U","V","W","X","Y","Z" },
            new[] { "あ","か","さ","た","な","は","ま","や","ら","わ","1","2","3" },
            new[] { "4","5","6","7","8","9","0","지우기","모두 지우기" },
        };

        string query = "";
        List<GameInfo> results = new List<GameInfo>();
        int kr, kc;              // 키보드 위치
        bool onResults;          // 결과 줄에 있는지
        int rfocus;
        readonly Dictionary<string, Rectangle> keyRects = new Dictionary<string, Rectangle>();
        readonly List<Rectangle> resRects = new List<Rectangle>();

        public override GameInfo FocusedGame => onResults && rfocus < results.Count ? results[rfocus] : null;

        public override void OnGamesChanged() => Update();

        void Update()
        {
            var all = Shell.VisibleGames.ToList();
            if (query.Length == 0) results = Shell.Recent(12);
            else
            {
                string q = query.ToUpperInvariant();
                results = all.Where(g => Key(g.Title).Contains(q) || g.Title.ToUpperInvariant().Contains(q) || DlMatch(g, q)).Take(40).ToList();
            }
            rfocus = Math.Min(rfocus, Math.Max(0, results.Count - 1));
            if (results.Count == 0) onResults = false;
        }

        // ───── 검색용 글자로 바꾸기 ─────
        const string Cho = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        static readonly Dictionary<char, char> KanaRow = BuildKana();

        static Dictionary<char, char> BuildKana()
        {
            var d = new Dictionary<char, char>();
            string[] rows =
            {
                "あいうえおぁぃぅぇぉゔ", "かきくけこがぎぐげごゕゖ", "さしすせそざじずぜぞ", "たちつてとだぢづでどっ",
                "なにぬねの", "はひふへほばびぶべぼぱぴぷぺぽ", "まみむめも", "やゆよゃゅょ", "らりるれろ", "わをんゎゐゑ"
            };
            foreach (var r in rows)
                foreach (char c in r)
                {
                    d[c] = r[0];
                    d[(char)(c + 0x60)] = r[0]; // 가타카나
                }
            d['ー'] = '\0';
            return d;
        }

        static string Key(string title)
        {
            var sb = new StringBuilder();
            foreach (char c0 in title)
            {
                char c = c0;
                if (c >= 0xAC00 && c <= 0xD7A3) c = Cho[(c - 0xAC00) / 588];
                if (c == 'ㄲ') c = 'ㄱ'; else if (c == 'ㄸ') c = 'ㄷ'; else if (c == 'ㅃ') c = 'ㅂ'; else if (c == 'ㅆ') c = 'ㅅ'; else if (c == 'ㅉ') c = 'ㅈ';
                if (c >= 0x3131 && c <= 0x314E) { sb.Append(c); continue; }
                if (KanaRow.TryGetValue(c, out char row)) { if (row != '\0') sb.Append(row); continue; }
                if (c >= 0xFF21 && c <= 0xFF5A) c = (char)(c - 0xFEE0); // 전각 영문
                if (char.IsLetterOrDigit(c) && c < 0x250) sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        void Press(string k)
        {
            if (k == "지우기") { if (query.Length > 0) query = query.Substring(0, query.Length - 1); }
            else if (k == "모두 지우기") query = "";
            else query += k;
            Update();
            Shell.FocusChanged();
        }

        public override void Paint(Graphics g)
        {
            int pad = S(40);
            // 검색 칸
            var box = new Rectangle(Area.X + pad, Area.Y + S(16), Area.Width - pad * 2, S(52));
            Draw.Fill(g, box, S(6), Color.FromArgb(200, 23, 29, 37));
            Draw.Ring(g, box, S(6), Color.FromArgb(70, 255, 255, 255), S(1));
            if (query.Length == 0)
                Draw.Text(g, "글자를 골라 검색 — 한글은 초성으로 (ㅁㄴㅇㅈ → 마녀의 집)", Draw.Body, Rectangle.Inflate(box, -S(20), 0), Theme.SubText);
            else
                Draw.Text(g, query, Theme.Px(22, true), Rectangle.Inflate(box, -S(20), 0), Theme.Text);
            Draw.Text(g, query.Length == 0 ? "최근 게임" : results.Count + "개", Draw.Body, new Rectangle(box.Right - S(220), box.Y, S(200), box.Height), Theme.SubText, Draw.Right);

            // 결과 줄
            resRects.Clear();
            int ch = (int)(Area.Height * 0.30f), cw = ch * 2 / 3;
            int ry = box.Bottom + S(18);
            int start = onResults ? Math.Max(0, rfocus - Math.Max(1, (Area.Width - pad * 2) / (cw + S(14)) - 2)) : 0;
            int x = Area.X + pad;
            for (int i = start; i < results.Count && x < Area.Right; i++)
            {
                var r = new Rectangle(x, ry, cw, ch);
                resRects.Add(r);
                bool f = onResults && i == rfocus;
                var dr = f ? Rectangle.Inflate(r, S(4), S(6)) : r;
                var art = CoverArt.Get(results[i]);
                if (art?.Capsule != null) Draw.Image(g, art.Capsule, dr, S(4));
                else { Draw.Fill(g, dr, S(4), Theme.Tile); Draw.Text(g, results[i].Title, Draw.Small, Rectangle.Inflate(dr, -S(8), -S(8)), Theme.SubText, Draw.Wrap); }
                if (f) Draw.FocusGlow(g, dr, S(4));
                x += cw + S(14);
            }
            if (results.Count == 0)
                Draw.Text(g, "맞는 게임이 없어요", Draw.Menu, new Rectangle(Area.X, ry, Area.Width, ch), Theme.SubText, Draw.Center);
            resStart = start;

            // 키보드
            keyRects.Clear();
            int ky = ry + ch + S(22);
            int kh = Math.Min(S(52), (Area.Bottom - ky - S(10)) / Keys.Length - S(6));
            int maxCols = 14;
            int kw = (Area.Width - pad * 2 - S(6) * (maxCols - 1)) / maxCols;
            for (int r = 0; r < Keys.Length; r++)
            {
                var row = Keys[r];
                int widthUnits = row.Sum(k => k.Length > 1 ? 2 : 1);
                int rowW = widthUnits * kw + (widthUnits - 1) * S(6);
                int kx = Area.X + (Area.Width - rowW) / 2;
                for (int c = 0; c < row.Length; c++)
                {
                    int units = row[c].Length > 1 ? 2 : 1;
                    var rect = new Rectangle(kx, ky, units * kw + (units - 1) * S(6), kh);
                    keyRects[r + ":" + c] = rect;
                    bool f = !onResults && r == kr && c == kc;
                    Draw.Fill(g, rect, S(4), f ? Theme.Focus : Color.FromArgb(190, 35, 41, 51));
                    Draw.Text(g, row[c], row[c].Length > 1 ? Draw.BodyBold : Theme.Px(18, true), rect, f ? Theme.Bar : Theme.Text, Draw.Center);
                    kx += rect.Width + S(6);
                }
                ky += kh + S(6);
            }
        }

        int resStart;

        public override void OnPad(Pad b)
        {
            if (b == Pad.B)
            {
                if (query.Length > 0) Press("지우기"); else Shell.Pop();
                return;
            }
            if (b == Pad.X) { Press("지우기"); return; }
            if (b == Pad.Y || b == Pad.Start) { if (results.Count > 0) { onResults = !onResults; Shell.FocusChanged(); } return; }

            if (onResults)
            {
                switch (b)
                {
                    case Pad.Left: rfocus = Math.Max(0, rfocus - 1); break;
                    case Pad.Right: rfocus = Math.Min(results.Count - 1, rfocus + 1); break;
                    case Pad.Down: onResults = false; break;
                    case Pad.A: if (rfocus < results.Count) Shell.Push(new DetailScreen(results[rfocus])); return;
                }
                Shell.FocusChanged();
                return;
            }
            switch (b)
            {
                case Pad.Left: kc = Math.Max(0, kc - 1); break;
                case Pad.Right: kc = Math.Min(Keys[kr].Length - 1, kc + 1); break;
                case Pad.Up:
                    if (kr == 0) { if (results.Count > 0) { onResults = true; Shell.FocusChanged(); } }
                    else { kr--; kc = Math.Min(kc, Keys[kr].Length - 1); }
                    break;
                case Pad.Down: if (kr < Keys.Length - 1) { kr++; kc = Math.Min(kc, Keys[kr].Length - 1); } break;
                case Pad.A: Press(Keys[kr][kc]); break;
            }
            Shell.Invalidate();
        }

        public override void OnTap(Point p)
        {
            foreach (var kv in keyRects)
                if (kv.Value.Contains(p))
                {
                    var parts = kv.Key.Split(':');
                    kr = int.Parse(parts[0]); kc = int.Parse(parts[1]);
                    onResults = false;
                    Press(Keys[kr][kc]);
                    return;
                }
            for (int i = 0; i < resRects.Count; i++)
                if (resRects[i].Contains(p))
                {
                    int idx = resStart + i;
                    if (idx < results.Count) { rfocus = idx; Shell.Push(new DetailScreen(results[idx])); }
                    return;
                }
        }

        public override List<Hint> Hints() => new List<Hint>
        {
            new Hint(Pad.A, onResults ? "열기" : "입력", () => OnPad(Pad.A)),
            new Hint(Pad.X, "지우기", () => Press("지우기")),
            new Hint(Pad.Y, onResults ? "키보드로" : "결과로", () => OnPad(Pad.Y)),
            new Hint(Pad.B, "뒤로", Shell.Pop),
        };
    }
}
