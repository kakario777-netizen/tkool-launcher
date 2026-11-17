using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace TkoolLauncher
{
    /// <summary>
    /// 가상 패드 (AYASpace·Handheld Companion과 같은 방식)
    /// - HidHide로 진짜 패드를 게임에서 숨기고 (런처만 보이게 허용)
    /// - ViGEmBus로 가상 엑스박스 패드를 만들어
    /// - 진짜 패드 입력에 데드존·감도·연사·자이로를 입혀 가상 패드로 보냄
    /// - 게임의 진동은 세기를 조절해 진짜 패드로 돌려보냄
    /// </summary>
    static class VirtualPad
    {
        // ───────── ViGEmClient (MIT, res/ViGEmClient.dll.bin) ─────────
        [StructLayout(LayoutKind.Sequential)]
        struct XUSB_REPORT { public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void RumbleCb(IntPtr client, IntPtr target, byte large, byte small, byte led, IntPtr user);

        [DllImport("ViGEmClient.dll")] static extern IntPtr vigem_alloc();
        [DllImport("ViGEmClient.dll")] static extern uint vigem_connect(IntPtr c);
        [DllImport("ViGEmClient.dll")] static extern void vigem_disconnect(IntPtr c);
        [DllImport("ViGEmClient.dll")] static extern void vigem_free(IntPtr c);
        [DllImport("ViGEmClient.dll")] static extern IntPtr vigem_target_x360_alloc();
        [DllImport("ViGEmClient.dll")] static extern uint vigem_target_add(IntPtr c, IntPtr t);
        [DllImport("ViGEmClient.dll")] static extern uint vigem_target_remove(IntPtr c, IntPtr t);
        [DllImport("ViGEmClient.dll")] static extern void vigem_target_free(IntPtr t);
        [DllImport("ViGEmClient.dll")] static extern uint vigem_target_x360_update(IntPtr c, IntPtr t, XUSB_REPORT r);
        [DllImport("ViGEmClient.dll")] static extern uint vigem_target_x360_get_user_index(IntPtr c, IntPtr t, out uint idx);
        [DllImport("ViGEmClient.dll")] static extern uint vigem_target_x360_register_notification(IntPtr c, IntPtr t, RumbleCb cb, IntPtr user);
        [DllImport("ViGEmClient.dll")] static extern void vigem_target_x360_unregister_notification(IntPtr t);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);

        // ───────── XInput (진짜 패드 읽기·진동) ─────────
        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
        [StructLayout(LayoutKind.Sequential)]
        struct XVib { public ushort L, R; }
        [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern int XGetEx(int i, out XState s);
        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")] static extern int XSet(int i, ref XVib v);
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint p);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint p);

        // ───────── 상태 ─────────
        public static volatile bool Running;
        public static volatile bool ControllerOff;      // 컨트롤러 끄기 (게임에 아무 입력도 안 보냄)
        public static int VirtualIndex = -1;            // 가상 패드의 XInput 번호 (런처는 이 번호를 안 읽음)
        public static string Problem = "";

        static IntPtr client, target;
        static RumbleCb rumbleCb;
        static int physIndex = -1;
        static Thread loop;
        static readonly List<string> hidden = new List<string>();

        // ───────── 드라이버 확인·설치 ─────────
        static bool ServiceExists(string name)
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name)) return k != null; }
            catch { return false; }
        }

        public static bool HasViGEm => ServiceExists("ViGEmBus");
        public static bool HasHidHide => ServiceExists("HidHide");

        static string HidHideCli
        {
            get
            {
                foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"C:\Program Files" })
                {
                    string p = Path.Combine(pf, @"Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe");
                    if (File.Exists(p)) return p;
                }
                return null;
            }
        }

        public static void InstallDrivers()
        {
            // winget으로 설치 (관리자 허락 창 한 번). 실패하면 받는 곳을 열어줌
            string args = "";
            if (!HasViGEm) args += "winget install --id ViGEm.ViGEmBus -e --accept-package-agreements --accept-source-agreements & ";
            if (!HasHidHide) args += "winget install --id Nefarius.HidHide -e --accept-package-agreements --accept-source-agreements & ";
            if (args == "") return;
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c " + args + "echo done") { UseShellExecute = true, Verb = "runas" });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo("https://github.com/nefarius/HidHide/releases") { UseShellExecute = true }); } catch { }
                try { Process.Start(new ProcessStartInfo("https://github.com/nefarius/ViGEmBus/releases") { UseShellExecute = true }); } catch { }
            }
        }

        static string Cli(string args)
        {
            string cli = HidHideCli;
            if (cli == null) return null;
            try
            {
                var p = Process.Start(new ProcessStartInfo(cli, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return o;
            }
            catch { return null; }
        }

        // ───────── 켜기·끄기 ─────────
        public static bool Start()
        {
            if (Running) return true;
            Problem = "";
            if (!HasViGEm || !HasHidHide) { Problem = "드라이버 설치가 필요해요 (ViGEmBus, HidHide)"; return false; }
            if (!Hardware.IsAdmin) { Problem = "관리자 권한이 필요해요 (고급 하드웨어 제어 켜기)"; return false; }

            // 1) 진짜 패드 번호 찾기 (가상 패드 만들기 전에)
            physIndex = -1;
            for (int i = 0; i < 4; i++)
            {
                try { if (XGetEx(i, out XState s) == 0) { physIndex = i; break; } } catch { Problem = "XInput을 못 읽어요"; return false; }
            }
            if (physIndex < 0) { Problem = "패드를 못 찾았어요"; return false; }

            // 2) 진짜 패드 숨기기 (런처는 볼 수 있게 등록)
            string list = Cli("--dev-gaming") ?? "";
            hidden.Clear();
            foreach (Match m in Regex.Matches(list, "\"(?:deviceInstancePath|baseContainerDeviceInstancePath)\"\\s*:\\s*\"([^\"]+)\""))
            {
                string path = m.Groups[1].Value.Replace("\\\\", "\\");
                if (path.IndexOf("ROOT\\", StringComparison.OrdinalIgnoreCase) == 0) continue; // 가상 장치 제외
                if (!hidden.Contains(path)) hidden.Add(path);
            }
            Cli("--app-reg \"" + System.Windows.Forms.Application.ExecutablePath + "\"");
            foreach (var h in hidden) Cli("--dev-hide \"" + h + "\"");
            Cli("--cloak-on");

            // 3) 가상 패드 만들기
            try
            {
                string dll = ExtractClient();
                if (dll == null) { Problem = "가상 패드 도구를 못 풀었어요"; Unhide(); return false; }
                LoadLibrary(dll);
                client = vigem_alloc();
                if (client == IntPtr.Zero || vigem_connect(client) != 0x20000000) { Problem = "ViGEmBus에 연결 못 했어요"; Cleanup(); return false; }
                target = vigem_target_x360_alloc();
                if (vigem_target_add(client, target) != 0x20000000) { Problem = "가상 패드를 못 만들었어요"; Cleanup(); return false; }
                rumbleCb = OnRumble;
                vigem_target_x360_register_notification(client, target, rumbleCb, IntPtr.Zero);
                if (vigem_target_x360_get_user_index(client, target, out uint vi) == 0x20000000) VirtualIndex = (int)vi;
            }
            catch (Exception ex) { Problem = "가상 패드 오류: " + ex.Message; Cleanup(); return false; }

            Running = true;
            loop = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "VirtualPad" };
            loop.SetApartmentState(ApartmentState.MTA);
            loop.Start();
            return true;
        }

        public static void Stop()
        {
            Running = false;
            try { loop?.Join(500); } catch { }
            Cleanup();
        }

        static void Cleanup()
        {
            try
            {
                if (target != IntPtr.Zero)
                {
                    try { vigem_target_x360_unregister_notification(target); } catch { }
                    try { vigem_target_remove(client, target); } catch { }
                    try { vigem_target_free(target); } catch { }
                }
                if (client != IntPtr.Zero) { try { vigem_disconnect(client); } catch { } try { vigem_free(client); } catch { } }
            }
            catch { }
            target = client = IntPtr.Zero;
            VirtualIndex = -1;
            Unhide();
        }

        static void Unhide()
        {
            if (hidden.Count == 0)
            {
                // 이전에 런처가 비정상 종료돼 숨김이 남았을 수 있음 → 관리자이고 HidHide가 있으면 숨김만 끔
                if (Hardware.IsAdmin && HasHidHide && !Running) Cli("--cloak-off");
                return;
            }
            foreach (var h in hidden) Cli("--dev-unhide \"" + h + "\"");
            Cli("--cloak-off");
            hidden.Clear();
        }

        static string ExtractClient()
        {
            string dir = Path.Combine(Settings.Dir, "tools");
            string dll = Path.Combine(dir, "ViGEmClient.dll");
            try
            {
                Directory.CreateDirectory(dir);
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TkoolLauncher.res.ViGEmClient.dll.bin"))
                {
                    if (s == null) return null;
                    if (!File.Exists(dll) || new FileInfo(dll).Length != s.Length)
                        using (var f = File.Create(dll)) s.CopyTo(f);
                }
                return dll;
            }
            catch { return File.Exists(dll) ? dll : null; }
        }

        // ───────── 진동: 게임 → 세기 조절 → 진짜 패드 ─────────
        static void OnRumble(IntPtr c, IntPtr t, byte large, byte small, byte led, IntPtr user)
        {
            if (physIndex < 0) return;
            float k = Settings.Vibration / 100f;
            var v = new XVib { L = (ushort)Math.Min(65535, large * 257 * k), R = (ushort)Math.Min(65535, small * 257 * k) };
            try { XSet(physIndex, ref v); } catch { }
        }

        // ───────── 입력 가공 ─────────
        static short Stick(short v, float dz, float sens)
        {
            float f = v / 32767f;
            float a = Math.Abs(f);
            if (a < dz) return 0;
            a = (a - dz) / (1 - dz);             // 데드존 밖을 0~1로 다시 펴기
            a = Math.Min(1f, a * sens);
            return (short)(Math.Sign(f) * a * 32767);
        }

        static void StickPair(ref short x, ref short y, float dz, float sens)
        {
            // 원형 데드존 (대각선도 고르게)
            float fx = x / 32767f, fy = y / 32767f;
            float m = (float)Math.Sqrt(fx * fx + fy * fy);
            if (m < dz) { x = y = 0; return; }
            float nm = Math.Min(1f, (m - dz) / (1 - dz) * sens);
            x = (short)Math.Max(-32767, Math.Min(32767, fx / m * nm * 32767));
            y = (short)Math.Max(-32767, Math.Min(32767, fy / m * nm * 32767));
        }

        static void Loop()
        {
            timeBeginPeriod(1);
            Gyro gyro = null;
            int gyroTry = 0;
            var sw = Stopwatch.StartNew();
            long last = sw.ElapsedTicks;
            float gx = 0, gy = 0; // 자이로 → 마우스 남은 값
            try
            {
                while (Running)
                {
                    long now = sw.ElapsedTicks;
                    float dt = (now - last) / (float)Stopwatch.Frequency;
                    last = now;

                    var rep = new XUSB_REPORT();
                    if (!ControllerOff && XGetEx(physIndex, out XState s) == 0)
                    {
                        rep.Buttons = s.Buttons;
                        rep.LT = s.LT; rep.RT = s.RT;
                        float dz = Settings.StickDeadzone / 100f, sens = Settings.StickSens / 100f;
                        short lx = s.LX, ly = s.LY, rx = s.RX, ry = s.RY;
                        StickPair(ref lx, ref ly, dz, sens);
                        StickPair(ref rx, ref ry, dz, sens);

                        // 연사: 지정한 버튼을 누르고 있으면 켜고 끄기를 반복
                        if (Settings.TurboMask != 0)
                        {
                            int period = 1000 / Math.Max(2, Settings.TurboHz);
                            bool on = (Environment.TickCount / (period / 2)) % 2 == 0;
                            if (!on) rep.Buttons = (ushort)(rep.Buttons & ~Settings.TurboMask);
                        }

                        // 자이로
                        if (Settings.GyroMode != 0)
                        {
                            if (gyro == null && gyroTry++ % 500 == 0) gyro = Gyro.Open();
                            bool hold = Settings.GyroHold == 0 || (Settings.GyroHold == 1 && (s.Buttons & 0x0100) != 0) || (Settings.GyroHold == 2 && s.LT > 60);
                            if (gyro != null && hold && gyro.Read(out double ax, out double ay, out double az))
                            {
                                double yaw = Settings.GyroAxis == 0 ? az : ay;   // 좌우 돌리기
                                double pitch = ax;                                 // 앞뒤 기울이기
                                if (Settings.GyroInvertX) yaw = -yaw;
                                if (Settings.GyroInvertY) pitch = -pitch;
                                float gs = Settings.GyroSens;
                                if (Settings.GyroMode == 1)
                                {
                                    // 오른쪽 스틱에 더하기
                                    int addX = (int)(-yaw * gs * 90), addY = (int)(pitch * gs * 90);
                                    rx = (short)Math.Max(-32767, Math.Min(32767, rx + addX));
                                    ry = (short)Math.Max(-32767, Math.Min(32767, ry + addY));
                                }
                                else
                                {
                                    gx += (float)(-yaw * dt * gs * 4);
                                    gy += (float)(-pitch * dt * gs * 4);
                                    int ix = (int)gx, iy = (int)gy;
                                    if (ix != 0 || iy != 0) { KeySender.MouseMove(ix, iy); gx -= ix; gy -= iy; }
                                }
                            }
                        }
                        rep.LX = lx; rep.LY = ly; rep.RX = rx; rep.RY = ry;
                    }
                    try { vigem_target_x360_update(client, target, rep); } catch { }
                    Thread.Sleep(4);
                }
            }
            catch { }
            finally
            {
                gyro?.Dispose();
                timeEndPeriod(1);
            }
        }
    }
}
