using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>스팀OS 느낌 그리기 도구 모음 (글자는 일본어도 잘 나오게 GDI TextRenderer 사용)</summary>
    static class Draw
    {
        public static int S(float v) => Theme.S(v);

        // 글꼴은 화면 배율이 바뀔 때만 새로 만듦
        public static Font Tiny, Small, Body, BodyBold, Label, H2, H1, Hero, Glyph, Menu;
        static float fontScale = -1;

        public static void EnsureFonts()
        {
            if (Math.Abs(fontScale - Theme.Scale) < 0.001f) return;
            fontScale = Theme.Scale;
            Tiny = Theme.Px(11); Small = Theme.Px(13); Body = Theme.Px(15); BodyBold = Theme.Px(15, true);
            Label = Theme.Px(12, true); H2 = Theme.Px(20, true); H1 = Theme.Px(26, true); Hero = Theme.Px(36, true);
            Glyph = Theme.Px(11, true); Menu = Theme.Px(17);
        }

        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        public const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags fl = Left)
        {
            TextRenderer.DrawText(g, s ?? "", f, r, c, fl);
        }

        public static void TextShadow(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags fl = Left)
        {
            var sr = r; sr.Offset(S(2), S(2));
            TextRenderer.DrawText(g, s ?? "", f, sr, Color.FromArgb(10, 10, 10), fl);
            TextRenderer.DrawText(g, s ?? "", f, r, c, fl);
        }

        public static int Measure(Graphics g, string s, Font f) =>
            TextRenderer.MeasureText(g, s ?? "", f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

        public static void Fill(Graphics g, Rectangle r, int radius, Color c)
        {
            if (radius <= 0) { using (var b = new SolidBrush(c)) g.FillRectangle(b, r); return; }
            Theme.FillRound(g, r, radius, c);
        }

        public static void Ring(Graphics g, Rectangle r, int radius, Color c, float width)
        {
            using (var path = Theme.Round(r, radius))
            using (var pen = new Pen(c, width))
                g.DrawPath(pen, path);
        }

        /// <summary>스팀덱처럼 포커스된 것 주위 흰 테두리 + 은은한 빛</summary>
        public static void FocusGlow(Graphics g, Rectangle r, int radius)
        {
            for (int i = 3; i >= 1; i--)
            {
                var rr = Rectangle.Inflate(r, S(2 + i * 2), S(2 + i * 2));
                Ring(g, rr, radius + S(i * 2), Color.FromArgb(18 * i, 120, 190, 255), S(3));
            }
            Ring(g, Rectangle.Inflate(r, S(2), S(2)), radius + S(2), Theme.Focus, S(2.5f));
        }

        public static void Image(Graphics g, Image img, Rectangle r, int radius)
        {
            if (img == null) return;
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (radius > 0)
            {
                using (var path = Theme.Round(r, radius))
                {
                    var clip = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    g.DrawImage(img, r);
                    g.Clip = clip;
                }
            }
            else g.DrawImage(img, r);
            g.InterpolationMode = old;
            g.PixelOffsetMode = PixelOffsetMode.Default;
        }

        /// <summary>패드 버튼 모양 (A/B/X/Y 동그라미, LB/RB, 보기·메뉴 버튼)</summary>
        public static int PadGlyph(Graphics g, Pad b, int x, int cy)
        {
            int d = S(22);
            var light = Color.FromArgb(214, 218, 224);
            var dark = Color.FromArgb(20, 24, 30);
            if (b == Pad.A || b == Pad.B || b == Pad.X || b == Pad.Y)
            {
                var r = new Rectangle(x, cy - d / 2, d, d);
                using (var br = new SolidBrush(light)) g.FillEllipse(br, r);
                string t = b == Pad.A ? "A" : b == Pad.B ? "B" : b == Pad.X ? "X" : "Y";
                Text(g, t, Glyph, r, dark, Center);
                return d;
            }
            if (b == Pad.LB || b == Pad.RB)
            {
                var r = new Rectangle(x, cy - d / 2, S(30), d);
                Fill(g, r, S(6), light);
                Text(g, b == Pad.LB ? "LB" : "RB", Glyph, r, dark, Center);
                return r.Width;
            }
            if (b == Pad.Back || b == Pad.Start)
            {
                var r = new Rectangle(x, cy - d / 2, S(30), d);
                Fill(g, r, S(11), light);
                using (var pen = new Pen(dark, Math.Max(1f, S(1.6f))))
                {
                    int cx = r.X + r.Width / 2;
                    if (b == Pad.Start)
                    {
                        for (int i = -1; i <= 1; i++)
                            g.DrawLine(pen, cx - S(6), cy + i * S(4), cx + S(6), cy + i * S(4));
                    }
                    else
                    {
                        g.DrawRectangle(pen, cx - S(6), cy - S(5), S(8), S(7));
                        g.DrawRectangle(pen, cx - S(2), cy - S(2), S(8), S(7));
                    }
                }
                return r.Width;
            }
            return 0;
        }

        /// <summary>왼쪽 위 메뉴 아이콘 (줄 세 개)</summary>
        public static void Hamburger(Graphics g, Rectangle r, Color c)
        {
            using (var pen = new Pen(c, S(2.2f)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                for (int i = -1; i <= 1; i++)
                    g.DrawLine(pen, cx - S(9), cy + i * S(6), cx + S(9), cy + i * S(6));
            }
        }

        public static void Battery(Graphics g, int right, int cy, int percent, bool charging)
        {
            int w = S(24), h = S(12);
            var body = new Rectangle(right - w - S(3), cy - h / 2, w, h);
            using (var pen = new Pen(Theme.Text, S(1.4f))) g.DrawRectangle(pen, body);
            g.FillRectangle(Brushes.White, body.Right + 1, cy - S(3), S(2.5f), S(6));
            int fill = Math.Max(0, Math.Min(100, percent)) * (w - S(4)) / 100;
            var c = percent <= 15 && !charging ? Theme.Danger : charging ? Theme.PlayTop : Theme.Text;
            using (var b = new SolidBrush(c)) g.FillRectangle(b, body.X + S(2), body.Y + S(2), fill, h - S(4) + 1);
        }

        public static void Star(Graphics g, Rectangle r, bool filled, Color c)
        {
            var pts = new PointF[10];
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f + r.Height * 0.04f;
            float ro = r.Width / 2f, ri = ro * 0.45f;
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5;
                float rad = i % 2 == 0 ? ro : ri;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rad, cy + (float)Math.Sin(a) * rad);
            }
            if (filled) using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
            else using (var p = new Pen(c, S(2))) { p.LineJoin = LineJoin.Round; g.DrawPolygon(p, pts); }
        }

        public static void PlayTriangle(Graphics g, Rectangle r, Color c)
        {
            var pts = new[]
            {
                new PointF(r.X + r.Width * 0.2f, r.Y),
                new PointF(r.Right, r.Y + r.Height / 2f),
                new PointF(r.X + r.Width * 0.2f, r.Bottom),
            };
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        public static void Badge(Graphics g, ref int x, int y, string text, Color c)
        {
            int w = Measure(g, text, Label) + S(14);
            var r = new Rectangle(x, y, w, S(22));
            Fill(g, r, S(4), c);
            Text(g, text, Label, r, Color.White, Center);
            x += w + S(6);
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * Math.Min(1f, t);
    }
}
