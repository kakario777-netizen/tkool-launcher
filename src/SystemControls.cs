using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TkoolLauncher
{
    /// <summary>
    /// 드라이버 없이 윈도우 기본 기능으로 되는 기기 조절 모음
    /// (밝기·소리·와이파이·블루투스·성능 모드·해상도·회전·메모리 정리·배터리·자동 실행)
    /// </summary>
    static class SystemControls
    {
        // ───────── 밝기 (WMI) ─────────
        public static int GetBrightness()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                    foreach (ManagementObject o in s.Get())
                        return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        public static void SetBrightness(int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            Task.Run(() =>
            {
                try
                {
                    using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                        foreach (ManagementObject o in s.Get())
                            o.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)v });
                }
                catch { }
            });
        }

        // ───────── 소리 (Core Audio) ─────────
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr p);
            int UnregisterControlChangeNotify(IntPtr p);
            int GetChannelCount(out int n);
            int SetMasterVolumeLevel(float db, Guid ctx);
            int SetMasterVolumeLevelScalar(float v, Guid ctx);
            int GetMasterVolumeLevel(out float db);
            int GetMasterVolumeLevelScalar(out float v);
            int SetChannelVolumeLevel(uint ch, float db, Guid ctx);
            int SetChannelVolumeLevelScalar(uint ch, float v, Guid ctx);
            int GetChannelVolumeLevel(uint ch, out float db);
            int GetChannelVolumeLevelScalar(uint ch, out float v);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid ctx);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static IAudioEndpointVolume Endpoint()
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (en.GetDefaultAudioEndpoint(0, 1, out IMMDevice dev) != 0) return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out object o) != 0) return null;
                return (IAudioEndpointVolume)o;
            }
            catch { return null; }
        }

        public static int GetVolume()
        {
            var e = Endpoint();
            if (e == null) return -1;
            e.GetMasterVolumeLevelScalar(out float v);
            return (int)Math.Round(v * 100);
        }

        public static void SetVolume(int v)
        {
            var e = Endpoint();
            if (e == null) return;
            e.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(100, v)) / 100f, Guid.Empty);
            if (v > 0) e.SetMute(false, Guid.Empty);
        }

        public static bool GetMute()
        {
            var e = Endpoint();
            if (e == null) return false;
            e.GetMute(out bool m);
            return m;
        }

        public static void SetMute(bool m) => Endpoint()?.SetMute(m, Guid.Empty);

        // ───────── 와이파이·블루투스 (윈도우 무선 장치 API를 PowerShell로) ─────────
        const string RadioScriptHead =
            "Add-Type -AssemblyName System.Runtime.WindowsRuntime;" +
            "$g=([System.WindowsRuntimeSystemExtensions].GetMethods()|?{$_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'})[0];" +
            "function A($t,$r){$m=$g.MakeGenericMethod($r);$n=$m.Invoke($null,@($t));$n.Wait(-1)|Out-Null;$n.Result};" +
            "[Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime]|Out-Null;" +
            "[Windows.Devices.Radios.RadioAccessStatus,Windows.System.Devices,ContentType=WindowsRuntime]|Out-Null;" +
            "[Windows.Devices.Radios.RadioState,Windows.System.Devices,ContentType=WindowsRuntime]|Out-Null;" +
            "A ([Windows.Devices.Radios.Radio]::RequestAccessAsync()) ([Windows.Devices.Radios.RadioAccessStatus])|Out-Null;" +
            "$rs=A ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]]);";

        static string RunPs(string script)
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    return o;
                }
            }
            catch { return ""; }
        }

        /// <summary>결과: "WiFi"→켜짐 여부, "Bluetooth"→켜짐 여부 (없으면 키 없음)</summary>
        public static Dictionary<string, bool> GetRadios()
        {
            var d = new Dictionary<string, bool>();
            string o = RunPs(RadioScriptHead + "$rs|%{ \"$($_.Kind)=$($_.State)\" }");
            foreach (var line in o.Split('\n'))
            {
                var p = line.Trim().Split('=');
                if (p.Length == 2 && !d.ContainsKey(p[0])) d[p[0]] = p[1] == "On";
            }
            return d;
        }

        public static void SetRadio(string kind, bool on)
        {
            RunPs(RadioScriptHead + "$rs|?{$_.Kind -eq '" + kind + "'}|%{ A ($_.SetStateAsync('" + (on ? "On" : "Off") + "')) ([Windows.Devices.Radios.RadioAccessStatus])|Out-Null }");
        }

        // ───────── 성능 모드 (윈도우 전원 모드) ─────────
        [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid g);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid g);

        public static readonly string[] PowerNames = { "배터리 절약", "균형", "최고 성능" };
        static readonly Guid[] PowerGuids =
        {
            new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"),
            Guid.Empty,
            new Guid("ded574b5-45a0-4f42-8737-46345c09c238"),
        };

        public static int GetPowerMode()
        {
            try
            {
                PowerGetEffectiveOverlayScheme(out Guid g);
                int i = Array.IndexOf(PowerGuids, g);
                return i < 0 ? 1 : i;
            }
            catch { return 1; }
        }

        public static void SetPowerMode(int i)
        {
            try { PowerSetActiveOverlayScheme(PowerGuids[Math.Max(0, Math.Min(2, i))]); } catch { }
        }

        // ───────── 해상도·회전 ─────────
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

        static DEVMODE Current()
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            EnumDisplaySettings(null, -1, ref dm);
            return dm;
        }

        /// <summary>지금 방향 기준 가능한 해상도 목록 (큰 것부터)</summary>
        public static List<string> Resolutions(out int currentIndex)
        {
            var cur = Current();
            bool portrait = cur.dmDisplayOrientation == 1 || cur.dmDisplayOrientation == 3;
            var set = new List<Tuple<int, int>>();
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
            {
                if (dm.dmBitsPerPel != 32) continue;
                int w = dm.dmPelsWidth, h = dm.dmPelsHeight;
                if (portrait) { int t = w; w = h; h = t; }
                if (w < 800) continue;
                if (!set.Any(x => x.Item1 == w && x.Item2 == h)) set.Add(Tuple.Create(w, h));
            }
            set = set.OrderByDescending(x => x.Item1 * x.Item2).ToList();
            currentIndex = set.FindIndex(x => x.Item1 == cur.dmPelsWidth && x.Item2 == cur.dmPelsHeight);
            return set.Select(x => x.Item1 + "×" + x.Item2).ToList();
        }

        public static void SetResolution(string text)
        {
            var p = text.Split('×');
            if (p.Length != 2) return;
            var dm = Current();
            dm.dmPelsWidth = int.Parse(p[0]);
            dm.dmPelsHeight = int.Parse(p[1]);
            dm.dmFields = 0x80000 | 0x100000; // 가로·세로
            ChangeDisplaySettings(ref dm, 0);
        }

        public static readonly string[] RotationNames = { "0°", "90°", "180°", "270°" };

        public static int GetRotation() => Current().dmDisplayOrientation;

        public static void SetRotation(int o)
        {
            var dm = Current();
            o = ((o % 4) + 4) % 4;
            bool swap = (dm.dmDisplayOrientation % 2) != (o % 2);
            if (swap) { int t = dm.dmPelsWidth; dm.dmPelsWidth = dm.dmPelsHeight; dm.dmPelsHeight = t; }
            dm.dmDisplayOrientation = o;
            dm.dmFields = 0x80 | 0x80000 | 0x100000; // 방향·가로·세로
            ChangeDisplaySettings(ref dm, 0);
        }

        // ───────── 메모리 정리 ─────────
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMSTAT
        {
            public uint Length, Load;
            public ulong Total, Avail, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);

        public static MEMSTAT Memory()
        {
            var m = new MEMSTAT { Length = (uint)Marshal.SizeOf(typeof(MEMSTAT)) };
            try { GlobalMemoryStatusEx(ref m); } catch { }
            return m;
        }

        /// <summary>다른 프로그램들이 잡고 있는 메모리를 비우게 함. 늘어난 여유 메모리(MB)</summary>
        public static int CleanMemory()
        {
            ulong before = Memory().Avail;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    IntPtr h = OpenProcess(0x0100 | 0x0400, false, p.Id); // SET_QUOTA | QUERY_INFORMATION
                    if (h != IntPtr.Zero) { EmptyWorkingSet(h); CloseHandle(h); }
                }
                catch { }
                finally { p.Dispose(); }
            }
            ulong after = Memory().Avail;
            return after > before ? (int)((after - before) / 1048576) : 0;
        }

        // ───────── 배터리 ─────────
        public static string BatteryText()
        {
            try
            {
                var ps = SystemInformation.PowerStatus;
                if (ps.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery) return "전원 연결됨";
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                if (ps.PowerLineStatus == PowerLineStatus.Online) return pct + "% · 충전 중";
                int sec = ps.BatteryLifeRemaining;
                if (sec <= 0) return pct + "%";
                return pct + "% · " + (sec / 3600) + "시간 " + (sec % 3600 / 60) + "분 남음";
            }
            catch { return ""; }
        }

        /// <summary>지금 쓰는 전력(W). 모르면 0</summary>
        public static double DischargeWatts()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT DischargeRate FROM BatteryStatus"))
                    foreach (ManagementObject o in s.Get())
                        return Convert.ToDouble(o["DischargeRate"]) / 1000.0;
            }
            catch { }
            return 0;
        }

        /// <summary>충전 전력(W, 배터리로 들어가는 양). 충전 중 아니면 0</summary>
        public static double ChargeWatts()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT ChargeRate, Charging FROM BatteryStatus"))
                    foreach (ManagementObject o in s.Get())
                    {
                        double w = Convert.ToDouble(o["ChargeRate"]) / 1000.0;
                        return w > 0 ? w : 0;
                    }
            }
            catch { }
            return 0;
        }

        /// <summary>배터리 전압(V). 모르면 0</summary>
        public static double BatteryVolts()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT Voltage FROM BatteryStatus"))
                    foreach (ManagementObject o in s.Get())
                        return Convert.ToDouble(o["Voltage"]) / 1000.0;
            }
            catch { }
            return 0;
        }

        // ───────── 윈도우 시작 시 자동 실행 ─────────
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool RunKeyExists()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue("TkoolLauncher") != null; }
            catch { return false; }
        }

        public static bool GetAutoStart() => Settings.AutoStartTask || RunKeyExists();

        /// <summary>
        /// 자동 실행: 로그인 작업(관리자 권한·배터리여도·10초 뒤)으로 등록 — 예전 시작프로그램(Run) 방식은
        /// 관리자 작업을 한 번 더 거쳐야 해서 부팅 때 자주 안 켜졌음. 작업을 못 만들면 예전 방식으로.
        /// </summary>
        public static void SetAutoStart(bool on)
        {
            try
            {
                if (on)
                {
                    if (Hardware.CreateStartTask())
                    {
                        Settings.AutoStartTask = true;
                        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) k?.DeleteValue("TkoolLauncher", false);
                        Log.Info("자동 실행: 로그인 작업으로 등록");
                    }
                    else
                    {
                        Settings.AutoStartTask = false;
                        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) k?.SetValue("TkoolLauncher", "\"" + Application.ExecutablePath + "\" --tray");
                        Log.Info("자동 실행: 작업을 못 만들어 시작프로그램으로 등록");
                    }
                }
                else
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) k?.DeleteValue("TkoolLauncher", false);
                    if (Settings.AutoStartTask || Hardware.StartTaskExists()) Hardware.DeleteStartTask();
                    Settings.AutoStartTask = false;
                }
                Settings.Save();
            }
            catch (Exception ex) { Log.Info("자동 실행 설정 실패: " + ex.Message); }
        }
    }
}
