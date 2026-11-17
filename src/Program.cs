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
            string trayFlag = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "쯔꾸르 런처", "tray.flag");
            bool tray = args.Contains("--tray");
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
                // 그냥 더블클릭해도 관리자로: 처음 한 번만 허락 창으로 작업을 만들고, 그 뒤로는 창 없이 작업으로 다시 켬
                if (!updated && !IsAdmin())
                {
                    string exe = Application.ExecutablePath;
                    if (!string.Equals(Settings.AdminTaskExe, exe, StringComparison.OrdinalIgnoreCase) && Settings.AdminTaskExe != "거절:" + exe)
                    {
                        if (Hardware.CreateAdminTask()) { Settings.AdminTaskExe = exe; Settings.HardwareControl = true; }
                        else Settings.AdminTaskExe = "거절:" + exe;   // 허락 안 하면 다시 묻지 않음
                        Settings.Save();
                    }
                    // 관리자로 다시 켤 때 '알림 영역으로 시작'을 넘겨줌 (작업엔 인수를 못 붙여서 표시 파일로)
                    if (tray) try { System.IO.File.WriteAllText(trayFlag, "1"); } catch { }
                    if (string.Equals(Settings.AdminTaskExe, exe, StringComparison.OrdinalIgnoreCase) && Hardware.RunAdminTask())
                    { Log.Info("관리자 권한으로 다시 켬"); return; }
                }
                if (System.IO.File.Exists(trayFlag)) { tray = true; try { System.IO.File.Delete(trayFlag); } catch { } }
                ShellForm.StartInTray = tray;
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
