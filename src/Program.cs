using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TkoolLauncher
{
    static class Program
    {
        static bool IsAdmin()
        {
            try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool updated = args.Contains("--updated") || args.Contains("--elevated");
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
                Log.Hook();
                Log.Start();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Settings.Load();
                // 하드웨어 제어를 켜 뒀는데 관리자가 아니면, 관리자 작업으로 다시 켜고 이건 끝냄 (허락 창 없음)
                if (Settings.HardwareControl && !updated && !IsAdmin() && Hardware.RunAdminTask()) { Log.Info("관리자 권한으로 다시 켬"); return; }
                GameData.Load();
                Gamepad.Start();
                Application.ApplicationExit += (s, e) => { try { Log.Info("프로그램 종료 신호"); } catch { } };
                try { Application.Run(new ShellForm()); }
                catch (Exception ex) { Log.Error("실행", ex); throw; }
                finally { Log.Stop(); }
            }
        }
    }
}
