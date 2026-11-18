using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>글자 입력 창 (태그 이름 등). 열리면 윈도우 터치 키보드를 같이 띄움.</summary>
    class InputDialog : Form
    {
        readonly TextBox box;
        public string Value => box.Text;

        InputDialog(string title, string init)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(34, 40, 50);
            TopMost = true; ShowInTaskbar = false; KeyPreview = true;
            var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            Size = new Size(Math.Min(sc.Width - 40, Theme.S(620)), Theme.S(210));
            Location = new Point((sc.Width - Width) / 2, Theme.S(90));   // 위쪽에 (아래는 터치 키보드 자리)
            var lab = new Label { Text = title, ForeColor = Color.White, Font = Theme.Px(17, true), AutoSize = false, Bounds = new Rectangle(Theme.S(22), Theme.S(14), Width - Theme.S(44), Theme.S(36)) };
            box = new TextBox { Text = init ?? "", Font = Theme.Px(20, false), Bounds = new Rectangle(Theme.S(22), Theme.S(58), Width - Theme.S(44), Theme.S(44)), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(23, 29, 37), ForeColor = Color.White };
            var ok = new Button { Text = "확인", Font = Theme.Px(16, true), Bounds = new Rectangle(Width - Theme.S(22) - Theme.S(150), Theme.S(126), Theme.S(150), Theme.S(60)), BackColor = Theme.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "취소", Font = Theme.Px(16, true), Bounds = new Rectangle(ok.Left - Theme.S(12) - Theme.S(150), Theme.S(126), Theme.S(150), Theme.S(60)), BackColor = Color.FromArgb(52, 62, 78), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
            ok.FlatAppearance.BorderSize = 0; cancel.FlatAppearance.BorderSize = 0;
            Controls.AddRange(new Control[] { lab, box, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
            Shown += (s, e) => { box.Focus(); box.SelectAll(); ShowTouchKeyboard(); };
        }

        static void ShowTouchKeyboard()
        {
            try
            {
                string tip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), @"microsoft shared\ink\TabTip.exe");
                if (File.Exists(tip)) Process.Start(new ProcessStartInfo(tip) { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>물어보고 입력한 글 (취소면 null)</summary>
        public static string Ask(IWin32Window owner, string title, string init)
        {
            using (var d = new InputDialog(title, init))
                return d.ShowDialog(owner) == DialogResult.OK ? d.Value : null;
        }
    }
}
