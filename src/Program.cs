using System;
using System.Threading;
using System.Windows.Forms;

namespace TkoolLauncher
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // 중복 실행 방지
            using (var mutex = new Mutex(true, "TkoolLauncher_SingleInstance", out bool first))
            {
                if (!first) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Settings.Load();
                Application.Run(new MainForm());
            }
        }
    }
}
