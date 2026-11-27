using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace TkoolLauncher
{
    /// <summary>
    /// PawnIO(서명된 하드웨어 접근 드라이버)로 팬·조명·TDP·CPU 온도 다루기.
    /// AYANEO AIR Plus (AMD 7320U, 보드명 AB05-Mendocino) 전용 값만 씀 — 다른 기기면 아무것도 안 건드림.
    /// 레지스터·명령 값은 Handheld Companion(AYANEODeviceCEii, AYANEOAIRPlusAMDMendocino, RyzenSmuService)에서 확인한 것.
    /// </summary>
    static class Hardware
    {
        // ───────── PawnIO 기본 ─────────
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, uint inSize, byte[] outBuf, uint outSize, out uint ret, IntPtr ov);

        const uint IOCTL_LOAD = 0xA1B22084, IOCTL_EXEC = 0xA1B22104;

        class Module
        {
            public SafeFileHandle H;
            public bool Exec(string fn, ulong[] input, ulong[] output)
            {
                var buf = new byte[32 + (input?.Length ?? 0) * 8];
                var nb = Encoding.ASCII.GetBytes(fn);
                Buffer.BlockCopy(nb, 0, buf, 0, Math.Min(31, nb.Length));
                if (input != null) Buffer.BlockCopy(input, 0, buf, 32, input.Length * 8);
                var ob = new byte[(output?.Length ?? 0) * 8];
                if (!DeviceIoControl(H, IOCTL_EXEC, buf, (uint)buf.Length, ob, (uint)ob.Length, out uint ret, IntPtr.Zero)) return false;
                if (output != null) Buffer.BlockCopy(ob, 0, output, 0, (int)Math.Min(ret, (uint)ob.Length));
                return true;
            }
        }

        static Module Open(string resource)
        {
            foreach (var path in new[] { @"\\?\GLOBALROOT\Device\PawnIO", @"\\.\PawnIO" })
            {
                IntPtr raw = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (raw == IntPtr.Zero || raw.ToInt64() == -1) continue;
                var h = new SafeFileHandle(raw, true);
                byte[] blob;
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (s == null) { h.Dispose(); return null; }
                    blob = new byte[s.Length];
                    s.Read(blob, 0, blob.Length);
                }
                if (DeviceIoControl(h, IOCTL_LOAD, blob, (uint)blob.Length, null, 0, out _, IntPtr.Zero))
                    return new Module { H = h };
                h.Dispose();
            }
            return null;
        }

        // ───────── 상태 ─────────
        public static bool PawnInstalled;
        public static bool IsAdmin = AdminNow();
        public static bool IsAirPlusMendocino;
        public static bool EcReady, SmuReady, TempReady;
        public static string Problem = "";

        static Module ec, smu, smn;
        static Mutex isaMutex, pciMutex;
        static readonly object lk = new object();

        public static bool Ready => EcReady || SmuReady;

        public static void Init()
        {
            try { IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { }
            PawnInstalled = DetectPawn();
            string vendor = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer", "") as string ?? "";
            string board = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct", "") as string ?? "";
            IsAirPlusMendocino = vendor.Trim().Equals("AYANEO", StringComparison.OrdinalIgnoreCase) && board.Trim() == "AB05-Mendocino";

            if (!PawnInstalled) { Problem = "PawnIO 드라이버가 없어요 (설정에서 설치)"; return; }
            if (!IsAdmin) { Problem = "관리자 권한이 필요해요 (설정에서 켜기)"; return; }
            if (!IsAirPlusMendocino) { Problem = "AIR Plus 7320U가 아니라서 꺼 뒀어요 (보드: " + board + ")"; return; }

            try { isaMutex = new Mutex(false, @"Global\Access_ISABUS.HTP.Method"); } catch { }
            try { pciMutex = new Mutex(false, @"Global\Access_PCI"); } catch { }

            try
            {
                ec = Open("TkoolLauncher.res.LpcIO.bin");
                EcReady = ec != null && ec.Exec("ioctl_select_slot", new ulong[] { 1 }, null);
            }
            catch { EcReady = false; }

            try
            {
                smu = Open("TkoolLauncher.res.RyzenSMU.bin");
                var o = new ulong[1];
                // 25 = Mendocino (모듈이 알려주는 칩 이름 번호). 다르면 TDP는 안 건드림
                SmuReady = smu != null && smu.Exec("ioctl_get_code_name", null, o) && o[0] == 25;
            }
            catch { SmuReady = false; }

            try
            {
                smn = Open("TkoolLauncher.res.AMDFamily17.bin");
                TempReady = smn != null && CpuTemp() > 0;
            }
            catch { TempReady = false; }

            Problem = Ready ? "" : "드라이버를 열지 못했어요";
        }

        static bool DetectPawn()
        {
            foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (k == null) continue;
                        foreach (var n in k.GetSubKeyNames())
                            using (var s = k.OpenSubKey(n))
                                if ((s?.GetValue("DisplayName") as string ?? "").IndexOf("PawnIO", StringComparison.OrdinalIgnoreCase) >= 0)
                                    return true;
                    }
                }
                catch { }
            }
            return false;
        }

        static bool Lock(Mutex m)
        {
            if (m == null) return true;
            try { return m.WaitOne(500); } catch (AbandonedMutexException) { return true; }
        }

        static void Unlock(Mutex m) { try { m?.ReleaseMutex(); } catch { } }

        // ───────── EC (0x4E/0x4F 포트, CEii 방식) ─────────
        static void Out(byte port, byte v) => ec.Exec("ioctl_pio_outb", new ulong[] { port, v }, null);

        static byte In(byte port)
        {
            var o = new ulong[1];
            ec.Exec("ioctl_pio_inb", new ulong[] { port }, o);
            return (byte)o[0];
        }

        static void EcAddr(ushort reg)
        {
            Out(0x4E, 0x87); Out(0x4E, 0x01); Out(0x4E, 0x55); Out(0x4E, 0xAA); // 설정 모드 진입
            Out(0x4E, 0x2E); Out(0x4F, 0x11); Out(0x4E, 0x2F); Out(0x4F, (byte)(reg >> 8));
            Out(0x4E, 0x2E); Out(0x4F, 0x10); Out(0x4E, 0x2F); Out(0x4F, (byte)(reg & 0xFF));
            Out(0x4E, 0x2E); Out(0x4F, 0x12); Out(0x4E, 0x2F);
        }

        static bool EcWrite(ushort reg, byte v)
        {
            if (!EcReady) return false;
            lock (lk)
            {
                if (!Lock(isaMutex)) return false;
                try
                {
                    ec.Exec("ioctl_select_slot", new ulong[] { 1 }, null);
                    EcAddr(reg);
                    Out(0x4F, v);
                    return true;
                }
                catch { return false; }
                finally { Unlock(isaMutex); }
            }
        }

        static byte EcRead(ushort reg)
        {
            if (!EcReady) return 0;
            lock (lk)
            {
                if (!Lock(isaMutex)) return 0;
                try
                {
                    ec.Exec("ioctl_select_slot", new ulong[] { 1 }, null);
                    EcAddr(reg);
                    return In(0x4F);
                }
                catch { return 0; }
                finally { Unlock(isaMutex); }
            }
        }

        // ───────── 팬 ─────────
        public static bool FanManual;

        /// <summary>percent &lt; 0 이면 자동(기기에 맡김)</summary>
        public static void SetFan(int percent)
        {
            if (!EcReady) return;
            if (percent < 0)
            {
                EcWrite(0xD1C8, 0x00);
                FanManual = false;
                return;
            }
            percent = Math.Max(20, Math.Min(100, percent)); // 너무 낮으면 과열 위험이라 20% 아래로는 안 내림
            EcWrite(0xD1C8, 0xA5);
            EcWrite(0x1802, (byte)(percent * 255 / 100));
            FanManual = true;
        }

        // ───────── 조이스틱 조명 ─────────
        static void RgbWrite(bool left, byte cmd, byte arg)
        {
            byte apply = left ? (byte)0xC6 : (byte)0x86;
            byte group = left ? (byte)0xB0 : (byte)0x70;
            EcWrite(0xD187, 0xA5);
            EcWrite((ushort)(0xD100 | (byte)(group + cmd)), arg);
            EcWrite((ushort)(0xD100 | apply), 0x01);
            Thread.Sleep(10);
        }

        public static void SetLed(bool on, int r, int g, int b, int brightness)
        {
            if (!EcReady) return;
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                if (!on) { RgbWrite(left, 0x02, 0xC0); continue; }
                RgbWrite(left, 0x02, 0x80);
                for (int zone = 1; zone <= 4; zone++)
                {
                    byte z = (byte)(zone * 3);
                    RgbWrite(left, z, Scale(r, brightness));
                    RgbWrite(left, (byte)(z + 1), Scale(g, brightness));
                    RgbWrite(left, (byte)(z + 2), Scale(b, brightness));
                }
            }
            EcWrite(0xD187, 0x00);
        }

        // ───────── 조명 효과 (숨쉬기·무지개) ─────────
        // 기기 자체 효과 명령은 공개된 게 없어서 런처가 색을 천천히 바꿔줌 (EC에 무리 안 가게 1.5초 간격)
        static Thread fx;

        public static void UpdateEffect()
        {
            if (Settings.LedEffect == 0 || !EcReady || !Settings.LedOn) return;
            if (fx != null && fx.IsAlive) return;
            fx = new Thread(() =>
            {
                double t = 0;
                while (Settings.LedEffect != 0 && Settings.LedOn && EcReady)
                {
                    int r = Settings.LedR, g = Settings.LedG, b = Settings.LedB, br = Settings.LedBright;
                    if (Settings.LedEffect == 1)
                    {
                        br = (int)(Settings.LedBright * (0.25 + 0.75 * (0.5 + 0.5 * Math.Sin(t))));
                        t += 0.9;
                    }
                    else
                    {
                        var c = Hsv(t);
                        r = c.R; g = c.G; b = c.B;
                        t += 25;
                    }
                    try { SetLed(true, r, g, b, br); } catch { }
                    Thread.Sleep(1500);
                }
            }) { IsBackground = true, Priority = ThreadPriority.Lowest };
            fx.Start();
        }

        static System.Drawing.Color Hsv(double h)
        {
            h = ((h % 360) + 360) % 360;
            double x = 1 - Math.Abs((h / 60) % 2 - 1);
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = 1; g = x; } else if (h < 120) { r = x; g = 1; } else if (h < 180) { g = 1; b = x; }
            else if (h < 240) { g = x; b = 1; } else if (h < 300) { r = x; b = 1; } else { r = 1; b = x; }
            return System.Drawing.Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        static byte Scale(int c, int bright) => (byte)(c / 255f * 192f / 100f * Math.Max(0, Math.Min(100, bright)));

        // ───────── TDP (SMU MP1 우편함) ─────────
        const uint MP1_CMD = 0x03B10528, MP1_RSP = 0x03B10578, MP1_ARG = 0x03B10998;
        public const int TdpMin = 5, TdpMax = 15;

        static bool SmuRead(uint addr, out uint v)
        {
            var o = new ulong[1];
            bool ok = smu.Exec("ioctl_read_smu_register", new ulong[] { addr }, o);
            v = (uint)o[0];
            return ok;
        }

        static bool SmuWrite(uint addr, uint v) => smu.Exec("ioctl_write_smu_register", new ulong[] { addr, v }, null);

        static bool WaitReady()
        {
            for (int i = 0; i < 8096; i++)
                if (SmuRead(MP1_RSP, out uint r) && r != 0) return true;
            return false;
        }

        static bool SmuCommand(uint cmd, uint arg)
        {
            if (!SmuReady) return false;
            lock (lk)
            {
                if (!Lock(pciMutex)) return false;
                try
                {
                    if (!WaitReady()) return false;
                    SmuWrite(MP1_RSP, 0);
                    for (uint i = 0; i < 6; i++) SmuWrite(MP1_ARG + i * 4, i == 0 ? arg : 0);
                    SmuWrite(MP1_CMD, cmd);
                    if (!WaitReady()) return false;
                    SmuRead(MP1_RSP, out uint rsp);
                    return rsp == 1;
                }
                catch { return false; }
                finally { Unlock(pciMutex); }
            }
        }

        /// <summary>STAPM·빠른·느린 한계를 같은 와트로</summary>
        public static bool SetTdp(int watts)
        {
            watts = Math.Max(TdpMin, Math.Min(TdpMax, watts));
            uint mw = (uint)watts * 1000;
            bool ok = SmuCommand(0x14, mw);
            ok &= SmuCommand(0x15, mw);
            ok &= SmuCommand(0x16, mw);
            return ok;
        }

        // ───────── CPU 온도 ─────────
        public static float CpuTemp()
        {
            if (smn == null) return 0;
            lock (lk)
            {
                if (!Lock(pciMutex)) return 0;
                try
                {
                    var o = new ulong[1];
                    if (!smn.Exec("ioctl_read_smn", new ulong[] { 0x00059800 }, o)) return 0;
                    uint v = (uint)o[0];
                    float t = (v >> 21) * 0.125f;
                    if ((v & (1 << 19)) != 0) t -= 49f; // 범위 확장 표시가 켜져 있으면 49도 보정
                    return t;
                }
                catch { return 0; }
                finally { Unlock(pciMutex); }
            }
        }

        // ───────── 설정 값 다시 적용 (켤 때, 주기적으로) ─────────
        public static void ApplySaved()
        {
            if (!Ready) return;
            try
            {
                PerfMode.Apply();   // 성능 모드(또는 직접 설정)가 TDP·팬을 맡음
                if (EcReady)
                {
                    if (Settings.LedSet) SetLed(Settings.LedOn, Settings.LedR, Settings.LedG, Settings.LedB, Settings.LedBright);
                    UpdateEffect();
                }
            }
            catch { }
        }

        /// <summary>런처 끌 때 팬은 기기 자동으로 돌려놓음 (고정된 채로 남지 않게)</summary>
        public static void Shutdown()
        {
            try { if (FanManual) SetFan(-1); } catch { }
        }

        // ───────── 관리자 권한으로 켜기 (작업 스케줄러: 한 번만 허락받고 그다음부턴 창 없이) ─────────
        const string TaskName = "TkoolLauncherAdmin";

        public static bool AdminTaskExists()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/query /tn " + TaskName) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 작업 스케줄러 작업을 XML로 만듦 — 기본값이면 배터리일 때 안 켜지고(휴대 기기라 치명적), 3일 지나면 강제로 꺼지고,
        /// 우선순위가 낮아서(7) 런처가 굼떠짐. 그래서: 배터리여도 켜기·시간 제한 없음·보통 우선순위.
        /// logon = 윈도우 로그인할 때 자동으로(자동 실행용).
        /// </summary>
        static string TaskXml(string exe, string args, bool logon)
        {
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            Func<string, string> X = v => System.Security.SecurityElement.Escape(v);
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                (logon ? "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + X(user) + "</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>" : "") +
                "<Principals><Principal id=\"Author\"><UserId>" + X(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable>" +
                "<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>" +
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>" + X(exe) + "</Command><Arguments>" + X(args) + "</Arguments></Exec></Actions></Task>";
        }

        /// <summary>작업 만들기. 이미 관리자면 창 없이, 아니면 허락 창 한 번</summary>
        static bool MakeTask(string name, string xml, bool admin)
        {
            string tmp = Path.Combine(Path.GetTempPath(), name + ".xml");
            try
            {
                File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
                var psi = new ProcessStartInfo("schtasks.exe", "/create /f /tn " + name + " /xml \"" + tmp + "\"");
                if (admin) { psi.UseShellExecute = false; psi.CreateNoWindow = true; }
                else { psi.UseShellExecute = true; psi.Verb = "runas"; psi.WindowStyle = ProcessWindowStyle.Hidden; }
                var p = Process.Start(psi);
                p.WaitForExit(15000);
                if (p.ExitCode != 0) Log.Info("작업 만들기 실패(" + name + "): " + p.ExitCode);
                return p.ExitCode == 0;
            }
            catch (Exception ex) { Log.Info("작업 만들기 실패(" + name + "): " + ex.Message); return false; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        static bool AdminNow()
        {
            try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        /// <summary>윈도우 허락 창이 한 번 뜨고, 관리자 작업을 만든 뒤 바로 관리자 런처로 다시 켬</summary>
        public static bool CreateAdminTask()
        {
            string exe = System.Windows.Forms.Application.ExecutablePath;
            return MakeTask(TaskName, TaskXml(exe, "--elevated", false), AdminNow());
        }

        // ───── 윈도우 켤 때 자동 실행: 로그인 작업(관리자·배터리 상관없이) ─────
        public const string StartTask = "TkoolLauncherStart";

        public static bool StartTaskExists()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/query /tn " + StartTask) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static bool CreateStartTask()
        {
            string exe = System.Windows.Forms.Application.ExecutablePath;
            return MakeTask(StartTask, TaskXml(exe, "--elevated --tray", true), AdminNow());
        }

        public static void DeleteStartTask()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", "/delete /f /tn " + StartTask);
                if (AdminNow()) { psi.UseShellExecute = false; psi.CreateNoWindow = true; }
                else { psi.UseShellExecute = true; psi.Verb = "runas"; psi.WindowStyle = ProcessWindowStyle.Hidden; }
                Process.Start(psi)?.WaitForExit(10000);
            }
            catch { }
        }

        public static void DeleteAdminTask()
        {
            try { Process.Start(new ProcessStartInfo("schtasks.exe", "/delete /f /tn " + TaskName) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }); } catch { }
        }

        public static bool RunAdminTask()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/run /tn " + TaskName) { UseShellExecute = false, CreateNoWindow = true });
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static void InstallPawn()
        {
            try
            {
                Process.Start(new ProcessStartInfo("winget.exe", "install --id namazso.PawnIO -e --accept-package-agreements --accept-source-agreements")
                { UseShellExecute = true, Verb = "runas" });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo("https://pawnio.eu") { UseShellExecute = true }); } catch { }
            }
        }
    }
}
