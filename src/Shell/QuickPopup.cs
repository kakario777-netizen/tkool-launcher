using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// AYA 버튼을 누르면 게임 위 오른쪽에 미끄러져 나오는 빠른 설정 (AYASpace 팝업처럼).
    /// 런처 전체를 띄우지 않고 이 좁은 창만 맨 위에 뜸 — 게임 화면은 왼쪽에 그대로 보임.
    /// </summary>
    class QuickPopup : Form, IPadTarget
    {
        readonly QuickPanel qp;
        readonly ShellForm shell;
        readonly Timer slide = new Timer { Interval = 10 };
        int targetX;
        List<Hint> hints = new List<Hint>();
        Point down; bool isDown, dragged;

        public QuickPopup(ShellForm s)
        {
            shell = s;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(14, 20, 27);
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            int w = Theme.S(460);
            targetX = sc.Right - w;
            Bounds = new Rectangle(sc.Right, sc.Top, w, sc.Height);

            qp = new QuickPanel { Shell = s, Host = this, Popup = true, T = 1f };
            slide.Tick += (o, e) =>
            {
                int nx = Left - Math.Max(Theme.S(24), (Left - targetX) / 3);
                if (nx <= targetX) { nx = targetX; slide.Stop(); }
                Left = nx;
            };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } // 작업 표시줄·Alt+Tab에 안 나오게
        }

        public void Open()
        {
            qp.Show();       // 상태(밝기·소리 등) 다시 읽기
            qp.Open = true; qp.T = 1f;
            Show();
            slide.Start();
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Draw.EnsureFonts();
            qp.T = 1f;
            qp.Paint(g, ClientRectangle);

            // 아래 버튼 안내
            int h = Theme.S(52);
            var r = new Rectangle(0, Height - h, Width, h);
            using (var b = new SolidBrush(Color.FromArgb(14, 18, 24))) g.FillRectangle(b, r);
            using (var p = new Pen(Color.FromArgb(30, 255, 255, 255))) g.DrawLine(p, 0, r.Y, Width, r.Y);
            hints = qp.Hints();
            int x = Width - Theme.S(20), cy = r.Y + h / 2;
            for (int i = hints.Count - 1; i >= 0; i--)
            {
                var hn = hints[i];
                int lw = Draw.Measure(g, hn.Label, Draw.Body);
                int gw = Theme.S(22);
                int x0 = x - gw - Theme.S(8) - lw;
                Draw.PadGlyph(g, hn.Button, x0, cy);
                Draw.Text(g, hn.Label, Draw.Body, new Rectangle(x0 + gw + Theme.S(8), r.Y, lw + 2, h), Theme.Text);
                hn.Bounds = new Rectangle(x0 - Theme.S(8), r.Y, x - x0 + Theme.S(16), h);
                x = x0 - Theme.S(24);
            }
        }

        public void OnPad(Pad b)
        {
            qp.OnPad(b);
            if (Visible) Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            switch (key)
            {
                case Keys.Up: OnPad(Pad.Up); return true;
                case Keys.Down: OnPad(Pad.Down); return true;
                case Keys.Left: OnPad(Pad.Left); return true;
                case Keys.Right: OnPad(Pad.Right); return true;
                case Keys.Enter: case Keys.Space: OnPad(Pad.A); return true;
                case Keys.Escape: case Keys.Back: OnPad(Pad.B); return true;
            }
            return base.ProcessCmdKey(ref msg, key);
        }

        protected override void OnMouseDown(MouseEventArgs e) { down = e.Location; isDown = true; dragged = false; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!isDown) return;
            if (Math.Abs(e.X - down.X) > Theme.S(10)) dragged = true;
            if (dragged) { qp.OnDrag(e.Location); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!isDown) return;
            isDown = false;
            if (dragged) return;
            foreach (var h in hints) if (h.Bounds.Contains(e.Location)) { h.Act?.Invoke(); if (Visible) Invalidate(); return; }
            qp.OnTap(e.Location);
            if (Visible) Invalidate();
        }

        // 바깥(게임 쪽)을 누르면 닫힘
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible && !shell.PopupClosing) BeginInvoke(new Action(() => { if (Visible) shell.ClosePopup(false); }));
        }
    }
}
