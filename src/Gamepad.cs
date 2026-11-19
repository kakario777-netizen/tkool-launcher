using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TkoolLauncher
{
    [Flags]
    enum Pad : uint
    {
        None = 0,
        Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008,
        Start = 0x0010, Back = 0x0020, LThumb = 0x0040, RThumb = 0x0080,
        LB = 0x0100, RB = 0x0200, Guide = 0x0400,
        A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
        LT = 0x10000, RT = 0x20000,   // 아날로그 트리거를 버튼처럼 (반 넘게 당기면)
    }

    /// <summary>런처 화면이 패드 입력을 받으려면 구현</summary>
    interface IPadTarget
    {
        void OnPad(Pad button);
    }

    /// <summary>
    /// 아야네오 내장 패드(윈도우에서는 엑스박스 패드로 잡힘)를 XInput으로 직접 읽는다.
    /// - 런처가 앞에 있을 때: 화면 이동·선택
    /// - 쯔꾸르 게임이 앞에 있을 때: 버튼을 키보드 키로 바꿔 보냄
    /// - 둘 다 아닐 때: 아무것도 안 읽고 쉼
    /// </summary>
    static class Gamepad
    {
        // ── XInput ──
        [StructLayout(LayoutKind.Sequential)]
        struct XState
        {
            public uint Packet;
            public ushort Buttons;
            public byte LT, RT;
            public short LX, LY, RX, RY;
        }

        [StructLayout(LayoutKind.Sequential)] struct XVib { public ushort L, R; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")] static extern int XSetVib(int i, ref XVib v);
        /// <summary>패드 진동으로 알리기 (게임이 전체 화면이라 안내 글이 안 보일 때도 느낄 수 있게)</summary>
        public static void Buzz(int ms, int times = 1)
        {
            new System.Threading.Thread(() =>
            {
                try
                {
                    for (int t = 0; t < times; t++)
                    {
                        var on = new XVib { L = 40000, R = 40000 }; var off = new XVib();
                        for (int i = 0; i < 4; i++) XSetVib(i, ref on);
                        System.Threading.Thread.Sleep(ms);
                        for (int i = 0; i < 4; i++) XSetVib(i, ref off);
                        System.Threading.Thread.Sleep(120);
                    }
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern int Get14(int i, out XState s);
        [DllImport("xinput1_4.dll", EntryPoint = "#100")]
        static extern int Get14Ex(int i, out XState s);   // 가이드(홈) 버튼까지 읽는 숨은 함수
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern int Get910(int i, out XState s);

        static int dll = 0; // 0: 1_4, 1: 9_1_0, 2: 없음
        static bool exOk = true;

        static bool Read(int i, out XState s)
        {
            s = default(XState);
            try
            {
                if (dll == 0)
                {
                    if (exOk)
                    {
                        try { return Get14Ex(i, out s) == 0; }
                        catch (EntryPointNotFoundException) { exOk = false; }
                    }
                    return Get14(i, out s) == 0;
                }
                if (dll == 1) return Get910(i, out s) == 0;
            }
            catch (DllNotFoundException) { dll++; }
            catch (EntryPointNotFoundException) { dll++; }
            return false;
        }

        // ── 키보드 보내기 ──
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public int type; public InputUnion u; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }

        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        static void SendKey(Keys k, bool down)
        {
            ushort vk = (ushort)k;
            uint flags = down ? 0u : 0x0002u; // KEYUP
            if (k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right) flags |= 0x0001; // EXTENDED
            var inp = new INPUT
            {
                type = 1,
                u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = flags } }
            };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>게임 안 버튼 → 쯔꾸르 키</summary>
        public static int Profile;   // 0 쯔꾸르식, 1 키보드 게임식(유니티 등)
        public static Dictionary<Pad, Keys> Extra = new Dictionary<Pad, Keys>();   // 읽어보기 키 → 남는 버튼

        static Keys MapGameKey(Pad b)
        {
            Keys ek;
            if (Extra != null && Extra.TryGetValue(b, out ek)) return ek;
            if (Profile == 1)
                switch (b)
                {
                    case Pad.Up: return Keys.Up;
                    case Pad.Down: return Keys.Down;
                    case Pad.Left: return Keys.Left;
                    case Pad.Right: return Keys.Right;
                    case Pad.A: return Keys.Return;     // 결정 (유니티 기본 Submit)
                    case Pad.B: return Keys.Escape;     // 취소·메뉴 (Cancel)
                    case Pad.X: return Keys.Space;
                    case Pad.Y: return Keys.Z;
                    case Pad.LB: return Keys.Q;
                    case Pad.RB: return Keys.E;
                    case Pad.Start: return Keys.Escape;
                    case Pad.Back: return Keys.Tab;
                    default: return Keys.None;
                }
            switch (b)
            {
                case Pad.Up: return Keys.Up;
                case Pad.Down: return Keys.Down;
                case Pad.Left: return Keys.Left;
                case Pad.Right: return Keys.Right;
                case Pad.A: return Keys.Z;          // 결정
                case Pad.B: return Keys.X;          // 취소
                case Pad.Y: return Keys.Escape;     // 메뉴
                case Pad.X: return Keys.ShiftKey;   // 달리기
                case Pad.LB: return Keys.Q;         // 이전 페이지
                case Pad.RB: return Keys.W;         // 다음 페이지
                case Pad.Start: return Keys.Enter;
                default: return Keys.None;
            }
        }

        static readonly Pad[] All =
        {
            Pad.Up, Pad.Down, Pad.Left, Pad.Right, Pad.A, Pad.B, Pad.X, Pad.Y,
            Pad.LB, Pad.RB, Pad.Start, Pad.Back, Pad.Guide, Pad.LT, Pad.RT
        };

        /// <summary>게임 중 홈 버튼(가이드 버튼, 또는 보기+메뉴 동시)</summary>
        public static Action Home;
        public static Action DoubleY;   // 게임 중 Y 빠르게 두 번 (표지 직접 찍기)
        static int lastY;

        // ── 상태 ──
        public static bool MapKeys;         // 그 게임에 키보드 변환을 할지
        public static bool Used;            // 패드를 한 번이라도 썼는지 (선택 테두리 표시용)
        public static bool MouseMode;       // 조이스틱으로 마우스 움직이기
        static XState raw;
        static int lastPress;
        public static int LastPressAge() => Environment.TickCount - lastPress;

        static Timer timer;
        static int index = -1;
        static int nextProbe;
        static Pad prev;
        static Pad sentKeys;                // 게임에 눌러둔 키
        static int repeatAt;
        static Pad repeatDir;

        public static void Start()
        {
            timer = new Timer { Interval = 33 };
            timer.Tick += Tick;
            timer.Start();
            // 패드는 따로 빠르게(8ms) 읽어 눌림을 모아 둠 → 화면이 바빠 타이머가 늦어도 짧게 누른 버튼이 씹히지 않음
            new System.Threading.Thread(PollLoop) { IsBackground = true, Name = "pad" }.Start();
        }

        static int liveBits, edgeBits;
        static volatile bool idle;
        static void PollLoop()
        {
            int last = 0;
            while (true)
            {
                try
                {
                    int p = (int)ReadPad();
                    int add = p & ~last;
                    if (add != 0) { int o; do { o = edgeBits; } while (System.Threading.Interlocked.CompareExchange(ref edgeBits, o | add, o) != o); }
                    last = p;
                    System.Threading.Volatile.Write(ref liveBits, p);
                }
                catch { }
                System.Threading.Thread.Sleep(idle ? 60 : 8);
            }
        }

        /// <summary>포커스를 뺏지 않는 게임 위 팝업 (열려 있는 동안 패드는 여기로, 게임엔 안 감)</summary>
        public static IPadTarget Overlay;

        static bool GameRunning
        {
            get
            {
                return GameWatch.Running;
            }
        }

        static void Tick(object sender, EventArgs e)
        {
            var target = Overlay ?? Form.ActiveForm as IPadTarget;   // 게임 위 팝업은 포커스 없이 패드만 받음
            bool game = GameRunning;

            if (target == null && !game && !MouseMode && DoubleY == null)
            {
                // 런처가 뒤에 있고 변환할 게임도 없음 → 쉬기 (읽기 스레드도 천천히)
                ReleaseGameKeys();
                timer.Interval = 150;
                idle = true;
                prev = Pad.None;
                System.Threading.Interlocked.Exchange(ref edgeBits, 0);
                return;
            }
            idle = false;

            // 지금 누르고 있는 것 + 지난번 이후 잠깐 눌렀다 뗀 것까지 (짧은 탭도 한 번은 눌린 걸로)
            Pad cur = (Pad)(System.Threading.Volatile.Read(ref liveBits) | System.Threading.Interlocked.Exchange(ref edgeBits, 0));
            // 화면 처리 중 새 창(대화상자)이 떠서 타이머가 다시 불려도 같은 버튼을 두 번 받지 않게 먼저 기록
            Pad old = prev;
            prev = cur;
            if ((cur & ~old) != 0) lastPress = Environment.TickCount;

            bool gameMode = target == null;
            if (gameMode != lastGameMode)
            {
                // 런처에서 A로 게임을 켠 그 손가락이 게임 안에서 또 눌리지 않게, 이미 누르고 있던 버튼은 뗄 때까지 무시
                blocked = cur;
                lastGameMode = gameMode;
            }
            blocked &= cur;

            // 표지 찍기: Y 두 번은 게임이 앞이든 런처(가림막)가 앞이든 언제나 받음
            if (DoubleY != null && (cur & ~old & Pad.Y) != 0)
            {
                int now = Environment.TickCount;
                if (now - lastY < 450) { lastY = 0; Log.Info("Y 두 번 감지 (게임 감시 " + game + ", 런처 앞 " + (target != null) + ")"); Buzz(90); DoubleY(); return; }
                lastY = now;
            }
            if (!gameMode)
            {
                timer.Interval = 33;
                ReleaseGameKeys();
                HandleUi(target, cur, old);
            }
            else
            {
                timer.Interval = MapKeys ? 16 : 40;
                Pad fresh = cur & ~old & ~blocked;
                bool combo = (cur & (Pad.Back | Pad.Start)) == (Pad.Back | Pad.Start) && (fresh & (Pad.Back | Pad.Start)) != 0;
                if ((fresh & Pad.Guide) != 0 || combo)
                {
                    ReleaseGameKeys();
                    blocked = cur;
                    Home?.Invoke();
                    return;
                }
                if (MouseMode) { ReleaseGameKeys(); HandleMouse(cur, old); timer.Interval = 16; }
                else if (MapKeys && GameIsForeground()) HandleGame(cur & ~blocked & (DoubleY != null ? ~Pad.Y : ~Pad.None));   // 표지 찍는 동안 Y는 게임에 안 보냄
                else ReleaseGameKeys();
            }
        }

        static bool lastGameMode;
        static Pad blocked;

        static Pad ReadPad()
        {
            int now = Environment.TickCount;
            if (index < 0)
            {
                // 연결 안 된 번호를 계속 묻는 건 느리니 2초마다만 찾기
                if (now - nextProbe < 0) return Pad.None;
                nextProbe = now + 2000;
                for (int i = 0; i < 4; i++)
                    if (i != VirtualPad.VirtualIndex && Read(i, out _)) { index = i; break; }
                if (index < 0) return Pad.None;
            }
            if (index == VirtualPad.VirtualIndex || !Read(index, out XState s)) { index = -1; return Pad.None; }
            raw = s;

            var p = (Pad)s.Buttons;
            const int dead = 14000;
            if (s.LX < -dead) p |= Pad.Left;
            if (s.LX > dead) p |= Pad.Right;
            if (s.LY > dead) p |= Pad.Up;
            if (s.LY < -dead) p |= Pad.Down;
            if (s.LT > 120) p |= Pad.LT;
            if (s.RT > 120) p |= Pad.RT;
            return p;
        }

        static void HandleUi(IPadTarget target, Pad cur, Pad old)
        {
            Pad pressed = cur & ~old & ~blocked;
            int now = Environment.TickCount;
            foreach (var b in All)
            {
                if ((pressed & b) == 0) continue;
                Used = true;
                target.OnPad(b);
                if (b <= Pad.Right) { repeatDir = b; repeatAt = now + 380; }
            }
            // 방향 꾹 누르면 연속 이동
            if (repeatDir != Pad.None)
            {
                if ((cur & repeatDir) == 0 || (Overlay ?? Form.ActiveForm as IPadTarget) != target) repeatDir = Pad.None;
                else if (now - repeatAt >= 0)
                {
                    target.OnPad(repeatDir);
                    repeatAt = now + 110;
                }
            }
        }

        static void HandleGame(Pad cur)
        {
            foreach (var b in All)
            {
                Keys k = MapGameKey(b);
                if (k == Keys.None) continue;
                bool down = (cur & b) != 0;
                bool sent = (sentKeys & b) != 0;
                if (down && !sent) { SendKey(k, true); sentKeys |= b; }
                else if (!down && sent) { SendKey(k, false); sentKeys &= ~b; }
            }
        }

        // ── 조이스틱 마우스: 왼쪽 스틱 이동, A 왼쪽 클릭, B 오른쪽 클릭, 오른쪽 스틱 휠 ──
        static float mx, my;

        static void SendMouse(int dx, int dy, uint flags, int data = 0)
        {
            var inp = new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, flags = flags, data = (uint)data } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }

        static float Curve(short v)
        {
            float f = v / 32767f;
            float a = Math.Abs(f);
            if (a < 0.12f) return 0;
            a = (a - 0.12f) / 0.88f;
            return Math.Sign(f) * a * a;
        }

        static void HandleMouse(Pad cur, Pad old)
        {
            float speed = Theme.S(22);
            mx += Curve(raw.LX) * speed;
            my -= Curve(raw.LY) * speed;
            int ix = (int)mx, iy = (int)my;
            if (ix != 0 || iy != 0) { SendMouse(ix, iy, 0x0001); mx -= ix; my -= iy; }
            float wheel = Curve(raw.RY);
            if (wheel != 0) SendMouse(0, 0, 0x0800, (int)(wheel * 40));
            Pad pressed = cur & ~old, released = old & ~cur;
            if ((pressed & Pad.A) != 0) SendMouse(0, 0, 0x0002);
            if ((released & Pad.A) != 0) SendMouse(0, 0, 0x0004);
            if ((pressed & Pad.B) != 0) SendMouse(0, 0, 0x0008);
            if ((released & Pad.B) != 0) SendMouse(0, 0, 0x0010);
        }

        static void ReleaseGameKeys()
        {
            if (sentKeys == Pad.None) return;
            foreach (var b in All)
                if ((sentKeys & b) != 0) SendKey(MapGameKey(b), false);
            sentKeys = Pad.None;
        }

        static uint fgPid;
        static bool fgIsGame;

        static bool GameIsForeground()
        {
            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                if (pid == fgPid) return fgIsGame;
                fgPid = pid;
                fgIsGame = GameWatch.InDir((int)pid, GameWatch.Dir);
                return fgIsGame;
            }
            catch { return false; }
        }
    }
}
