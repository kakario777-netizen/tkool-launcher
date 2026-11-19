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
        readonly Timer refresh = new Timer { Interval = 1500 };   // 지금 상태(TDP·팬·온도) 숫자 새로 그리기
        int targetX;
        List<Hint> hints = new List<Hint>();
        Point down; bool isDown, dragged, vScroll;

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
            int w = QuickPanel.PanelWidth(); // 런처 안 빠른 설정판과 같은 폭
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
            // 작업 표시줄·Alt+Tab에 안 나오고(0x80), 맨 위(0x8), 눌러도 포커스를 안 가져감(0x08000000) — 게임은 계속 앞 창
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x8 | 0x08000000; return cp; }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE → MA_NOACTIVATE
            base.WndProc(ref m);
        }

        public void Open()
        {
            // 해상도·회전이 바뀌었을 수 있어 열 때마다 지금 화면에 맞춰 자리 다시 잡기 (예전엔 처음 자리 그대로라 해상도를 줄이면 화면 밖에 떠서 안 보였음)
            var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            int w = QuickPanel.PanelWidth();
            targetX = sc.Right - w;
            Bounds = new Rectangle(sc.Right, sc.Top, w, sc.Height);
            qp.Show();       // 상태(밝기·소리 등) 다시 읽기
            qp.Open = true; qp.T = 1f;
            Show();
            SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);   // 맨 위, 포커스 안 뺏음
            slide.Start();
            refresh.Tick -= RefreshTick; refresh.Tick += RefreshTick;
            refresh.Start();
            outside.Tick -= OutsideTick; outside.Tick += OutsideTick;
            outside.Start();
        }

        void RefreshTick(object o, EventArgs e) { if (Visible) Invalidate(); }

        // 포커스를 안 가져가니 '바깥 누르면 닫기'는 직접 봄: 팝업 밖(게임 쪽)을 누르면 닫음
        readonly Timer outside = new Timer { Interval = 60 };
        bool wasDown;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        void OutsideTick(object o, EventArgs e)
        {
            if (!Visible) { outside.Stop(); return; }
            bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (down && !wasDown && !Bounds.Contains(Cursor.Position) && !shell.PopupClosing) shell.ClosePopup(false);
            wasDown = down;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintAll(e); } catch (Exception ex) { Log.Error("팝업 그리기", ex); }
        }

        void PaintAll(PaintEventArgs e)
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
                int gw = hn.Button == Pad.A || hn.Button == Pad.B || hn.Button == Pad.X || hn.Button == Pad.Y ? Theme.S(22) : Theme.S(30);
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
            int dx = e.X - down.X, dy = e.Y - down.Y;
            if (!dragged && (Math.Abs(dx) > Theme.S(10) || Math.Abs(dy) > Theme.S(10)))
            {
                dragged = true;
                vScroll = Math.Abs(dy) > Math.Abs(dx) && !qp.OnSlider(down);   // 위아래 = 목록 밀기, 슬라이더 위 옆으로 = 값 바꾸기
                if (vScroll) qp.ScrollDrag(0, true);
            }
            if (dragged) { if (vScroll) qp.ScrollDrag(dy, false); else qp.OnDrag(e.Location); Invalidate(); }
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

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { outside.Stop(); refresh.Stop(); }
        }
    }
}
