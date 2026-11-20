using System;
using System.Drawing;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임 위 팝업 뒤에 까는 거의 투명한 화면 덮개 — 팝업 바깥을 누른 터치·클릭이 게임(멈춘 동안 쌓였다가 풀리면 눌림)에 가지 않고
    /// 여기서 받아 팝업만 닫음. 포커스는 안 가져감.
    /// </summary>
    class PopupShield : Form
    {
        readonly Action onTap;
        public PopupShield(Action onTap)
        {
            this.onTap = onTap;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = Color.Black; Opacity = 0.01;
            Bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000; return cp; } }   // TOOLWINDOW | NOACTIVATE
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE → MA_NOACTIVATE
            base.WndProc(ref m);
        }
        protected override void OnMouseUp(MouseEventArgs e) { onTap?.Invoke(); }
        public void Cover() { Bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds; if (!Visible) Show(); }
    }
}
