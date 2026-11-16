using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TkoolLauncher
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool updated = args.Contains("--updated");
            using (var mutex = new Mutex(true, "TkoolLauncher_SingleInstance", out bool first))
            {
                if (!first)
                {
                    // 업데이트로 다시 켜진 경우: 이전 런처가 꺼질 때까지 잠깐 기다림
                    if (!updated) return;
                    try { if (!mutex.WaitOne(15000)) return; }
                    catch (AbandonedMutexException) { }
                }
                Updater.CleanupOld();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Settings.Load();
                Gamepad.Start();
                Application.Run(new MainForm());
            }
        }
    }
}
