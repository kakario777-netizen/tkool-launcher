using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TkoolLauncher
{
    static class Theme
    {
        // 스팀OS(게임 모드) 색
        public static readonly Color Bg = Color.FromArgb(14, 20, 27);
        public static readonly Color BgTop = Color.FromArgb(30, 38, 52);
        public static readonly Color BgBottom = Color.FromArgb(9, 12, 16);
        public static readonly Color Bar = Color.FromArgb(14, 18, 24);
        public static readonly Color Panel = Color.FromArgb(23, 29, 37);
        public static readonly Color Tile = Color.FromArgb(35, 41, 51);
        public static readonly Color TileHot = Color.FromArgb(61, 68, 80);
        public static readonly Color Accent = Color.FromArgb(26, 159, 255);
        public static readonly Color Focus = Color.FromArgb(240, 244, 248);
        public static readonly Color Text = Color.FromArgb(255, 255, 255);
        public static readonly Color SubText = Color.FromArgb(139, 146, 154);
        public static readonly Color Dim = Color.FromArgb(92, 100, 110);
        public static readonly Color PlayTop = Color.FromArgb(112, 214, 29);
        public static readonly Color PlayBottom = Color.FromArgb(1, 167, 91);
        public static readonly Color Danger = Color.FromArgb(214, 72, 72);

        public static float Scale = 1f;
        public static int S(float v) => (int)Math.Round(v * Scale);

        public const string FontName = "Malgun Gothic";

        public static void Init(Control c)
        {
            using (var g = c.CreateGraphics()) Scale = g.DpiX / 96f;
            // 작은 화면(7인치)에서 터치하기 좋게 기본을 조금 키움
            Scale *= 1.1f;
        }

        /// <summary>픽셀 크기 글꼴 (화면 배율 적용)</summary>
        public static Font Px(float px, bool bold = false) =>
            new Font(FontName, px * Scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

        public static Font F(float pt, bool bold = false) =>
            new Font(FontName, pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);

        public static Color VersionColor(TkoolVersion v)
        {
            switch (v)
            {
                case TkoolVersion.RM2000: return Color.FromArgb(232, 146, 60);
                case TkoolVersion.XP: return Color.FromArgb(92, 180, 96);
                case TkoolVersion.VX: return Color.FromArgb(56, 178, 172);
                case TkoolVersion.VXAce: return Color.FromArgb(76, 136, 232);
                case TkoolVersion.MV: return Color.FromArgb(150, 110, 230);
                default: return Color.FromArgb(226, 96, 160);
            }
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }

        /// <summary>터치하기 좋은 큰 평면 버튼</summary>
        public static Button MakeButton(string text, Color? back = null)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = back ?? Tile,
                ForeColor = Text,
                Font = F(12f, true),
                Height = S(52),
                Width = S(140),
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = TileHot;
            b.FlatAppearance.MouseDownBackColor = Accent;
            return b;
        }
    }
}
