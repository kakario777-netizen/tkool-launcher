using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace TkoolLauncher
{
    /// <summary>윈도우 방화벽 "이 앱에 액세스하도록 허용하시겠습니까?" 창이 게임 위에 안 뜨게: 켜기 전에 게임 폴더 exe를 미리 허용 (관리자 권한일 때)</summary>
    static class Firewall
    {
        static HashSet<string> done;
        static string ListPath => Path.Combine(Settings.Dir, "cache", "firewall.txt");

        public static void Allow(GameInfo g)
        {
            try
            {
                string dir = g?.InstallDir ?? g?.Folder;
                if (dir == null || !Directory.Exists(dir)) return;
                lock (typeof(Firewall))
                {
                    if (done == null) { done = new HashSet<string>(StringComparer.OrdinalIgnoreCase); try { if (File.Exists(ListPath)) foreach (var l in File.ReadAllLines(ListPath, Encoding.UTF8)) done.Add(l.Trim()); } catch { } }
                    var exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories).Take(30).Where(x => !done.Contains(x)).ToList();
                    if (exes.Count == 0) return;
                    foreach (var exe in exes)
                    {
                        string name = "쯔꾸르 런처 - " + Path.GetFileNameWithoutExtension(exe);
                        var psi = new ProcessStartInfo("netsh", "advfirewall firewall add rule name=\"" + name.Replace("\"", "") + "\" dir=in action=allow program=\"" + exe + "\" enable=yes profile=any")
                        { UseShellExecute = false, CreateNoWindow = true };
                        using (var p = Process.Start(psi)) { p.WaitForExit(5000); if (p.HasExited && p.ExitCode == 0) done.Add(exe); }
                    }
                    try { Directory.CreateDirectory(Path.GetDirectoryName(ListPath)); File.WriteAllLines(ListPath, done.ToArray(), Encoding.UTF8); } catch { }
                }
            }
            catch (Exception ex) { Log.Info("방화벽 허용 실패: " + ex.Message); }
        }
    }
}
