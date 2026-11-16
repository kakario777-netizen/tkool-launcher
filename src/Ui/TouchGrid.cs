using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TkoolLauncher
{
    class TileItem
    {
        public string Title;
        public string Sub;
        public List<(string text, Color color)> Badges = new List<(string, Color)>();
        public object Tag;
    }

    /// <summary>
    /// 손가락으로 끌어서 스크롤하고, 톡 치면 선택되는 타일 목록.
    /// 터치는 마우스 이벤트로 들어오므로 '움직인 거리'로 스크롤/탭을 구분한다.
    /// </summary>
    class TouchGrid : Control
    {
        public List<TileItem> Items = new List<TileItem>();
        public int MinTileWidth = 300;   // 디자인 기준(96dpi) 값
        public int TileHeight = 120;
        public int Gap = 14;
        public bool SingleColumn;
        public string EmptyText = "";
        public int SelectedIndex = -1;
        public bool ShowSelection;

        public event Action<TileItem> ItemClicked;

        int scroll;
        int downY, downX, downScroll, pressIndex = -1;
        bool mouseDown, dragging;

        Font titleFont, subFont, badgeFont, emptyFont;

        public TouchGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            titleFont = Theme.F(14f, true);
            subFont = Theme.F(9.5f);
            badgeFont = Theme.F(9f, true);
            emptyFont = Theme.F(13f);
        }

        public void SetItems(List<TileItem> items)
        {
            Items = items;
            scroll = 0;
            if (SelectedIndex >= Items.Count) SelectedIndex = -1;
            Invalidate();
        }

        int Cols
        {
            get
            {
                if (SingleColumn) return 1;
                int pad = Theme.S(Gap);
                int c = (Width - pad) / (Theme.S(MinTileWidth) + pad);
                return Math.Max(1, c);
            }
        }

        Rectangle TileRect(int i)
        {
            int pad = Theme.S(Gap);
            int cols = Cols;
            int w = (Width - pad * (cols + 1)) / cols;
            int h = Theme.S(TileHeight);
            int col = i % cols, row = i / cols;
            return new Rectangle(pad + col * (w + pad), pad + row * (h + pad) - scroll, w, h);
        }

        int ContentHeight
        {
            get
            {
                if (Items.Count == 0) return 0;
                int pad = Theme.S(Gap);
                int rows = (Items.Count + Cols - 1) / Cols;
                return pad + rows * (Theme.S(TileHeight) + pad);
            }
        }

        void ClampScroll()
        {
            int max = Math.Max(0, ContentHeight - Height);
            if (scroll > max) scroll = max;
            if (scroll < 0) scroll = 0;
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < Items.Count; i++)
                if (TileRect(i).Contains(p)) return i;
            return -1;
        }

        public void EnsureVisible(int i)
        {
            if (i < 0 || i >= Items.Count) return;
            var r = TileRect(i);
            int pad = Theme.S(Gap);
            if (r.Top < 0) scroll += r.Top - pad;
            else if (r.Bottom > Height) scroll += r.Bottom - Height + pad;
            ClampScroll();
            Invalidate();
        }

        /// <summary>패드 A 버튼: 선택된 칸을 누른 것과 같게</summary>
        public void ActivateSelected()
        {
            if (SelectedIndex >= 0 && SelectedIndex < Items.Count) ItemClicked?.Invoke(Items[SelectedIndex]);
        }

        public void MoveSelection(int delta)
        {
            if (Items.Count == 0) return;
            int n = SelectedIndex < 0 ? 0 : SelectedIndex + delta;
            SelectedIndex = Math.Max(0, Math.Min(Items.Count - 1, n));
            EnsureVisible(SelectedIndex);
        }

        public int ColumnCount => Cols;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            mouseDown = true; dragging = false;
            downX = e.X; downY = e.Y; downScroll = scroll;
            pressIndex = HitTest(e.Location);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseDown) return;
            int dy = e.Y - downY, dx = e.X - downX;
            if (!dragging && (Math.Abs(dy) > Theme.S(12) || Math.Abs(dx) > Theme.S(12)))
            {
                dragging = true;
                pressIndex = -1;
            }
            if (dragging)
            {
                scroll = downScroll - dy;
                ClampScroll();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasTap = mouseDown && !dragging;
            mouseDown = false;
            int idx = pressIndex;
            pressIndex = -1;
            Invalidate();
            if (wasTap && idx >= 0 && idx == HitTest(e.Location))
            {
                SelectedIndex = idx;
                Invalidate();
                ItemClicked?.Invoke(Items[idx]);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll -= e.Delta;
            ClampScroll();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (Items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, emptyFont, ClientRectangle, Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            int radius = Theme.S(14);
            int inner = Theme.S(16);
            for (int i = 0; i < Items.Count; i++)
            {
                var r = TileRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                var it = Items[i];

                bool hot = i == pressIndex;
                Theme.FillRound(g, r, radius, hot ? Theme.TileHot : Theme.Tile);
                if ((ShowSelection || Gamepad.Used) && i == SelectedIndex)
                {
                    using (var path = Theme.Round(r, radius))
                    using (var pen = new Pen(Theme.Accent, Theme.S(3)))
                        g.DrawPath(pen, path);
                }

                // 배지 (버전, 일본어 등)
                int bx = r.X + inner, by = r.Y + inner;
                int bh = Theme.S(24);
                foreach (var bd in it.Badges)
                {
                    var sz = TextRenderer.MeasureText(g, bd.text, badgeFont);
                    var br = new Rectangle(bx, by, sz.Width + Theme.S(10), bh);
                    Theme.FillRound(g, br, Theme.S(6), bd.color);
                    TextRenderer.DrawText(g, bd.text, badgeFont, br, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    bx = br.Right + Theme.S(6);
                }

                int top = it.Badges.Count > 0 ? by + bh + Theme.S(8) : r.Y + inner;
                int subH = string.IsNullOrEmpty(it.Sub) ? 0 : Theme.S(24);
                var titleRect = new Rectangle(r.X + inner, top, r.Width - inner * 2, r.Bottom - inner - subH - top);
                TextRenderer.DrawText(g, it.Title, titleFont, titleRect, Theme.Text,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                    (it.Badges.Count == 0 && subH == 0 ? TextFormatFlags.VerticalCenter : 0));

                if (subH > 0)
                {
                    var subRect = new Rectangle(r.X + inner, r.Bottom - inner - subH, r.Width - inner * 2, subH);
                    TextRenderer.DrawText(g, it.Sub, subFont, subRect, Theme.SubText,
                        TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
                }
            }

            // 스크롤 위치 표시
            int ch = ContentHeight;
            if (ch > Height)
            {
                int barH = Math.Max(Theme.S(40), Height * Height / ch);
                int barY = (Height - barH) * scroll / (ch - Height);
                Theme.FillRound(g, new Rectangle(Width - Theme.S(6), barY, Theme.S(4), barH), Theme.S(2),
                    Color.FromArgb(90, 255, 255, 255));
            }
        }
    }
}
