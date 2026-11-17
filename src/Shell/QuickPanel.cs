using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 오른쪽에서 나오는 빠른 설정판 (스팀덱 '…' 버튼 메뉴 / AYASpace 빠른 메뉴 같은 것)
    /// </summary>
    class QuickPanel
    {
        public ShellForm Shell;
        public bool Open;
        public float T;
        int focus;
        float scroll;
        Rectangle panel;

        enum Kind { Slider, Toggle, Choice, Action }

        class Row
        {
            public Kind Kind;
            public string Title, Sub;
            public int Num;                 // 슬라이더 값 0~100
            public bool On;
            public string[] Choices;
            public int ChoiceIndex;
            public Action<int> Change;      // 슬라이더·선택: 새 값 / 토글: 1·0
            public Action Act;
            public Rectangle Rect;
            public bool Danger;
        }

        List<Row> rows = new List<Row>();
        int brightness = -1, volume = -1, power = 1, rot;
        bool mute, wifi, bt, hasWifi, hasBt, radiosLoaded;
        List<string> resList = new List<string>();
        int resIndex;
        string memNote;
        bool confirmQuit;

        public void Show()
        {
            Open = true;
            focus = 0;
            confirmQuit = false;
            LoadState();
            Shell.Animate();
        }

        public void Hide()
        {
            Open = false;
            Shell.Animate();
        }

        void LoadState()
        {
            brightness = SystemControls.GetBrightness();
            volume = SystemControls.GetVolume();
            mute = SystemControls.GetMute();
            power = SystemControls.GetPowerMode();
            try { resList = SystemControls.Resolutions(out resIndex); } catch { resList = new List<string>(); }
            try { rot = SystemControls.GetRotation(); } catch { rot = 0; }
            Task.Run(() =>
            {
                var r = SystemControls.GetRadios();
                Shell.BeginInvoke(new Action(() =>
                {
                    hasWifi = r.ContainsKey("WiFi"); wifi = hasWifi && r["WiFi"];
                    hasBt = r.ContainsKey("Bluetooth"); bt = hasBt && r["Bluetooth"];
                    radiosLoaded = true;
                    Shell.Invalidate();
                }));
            });
        }

        void Build()
        {
            rows = new List<Row>();
            bool game = GameWatch.Running;
            if (game)
            {
                rows.Add(new Row { Kind = Kind.Action, Title = "게임으로 돌아가기", Act = Shell.ReturnToGame });
                rows.Add(new Row
                {
                    Kind = Kind.Action, Danger = true,
                    Title = confirmQuit ? "한 번 더 누르면 게임을 끝내요" : "게임 끝내기",
                    Sub = confirmQuit ? "저장 안 한 내용은 사라져요" : null,
                    Act = () => { if (!confirmQuit) { confirmQuit = true; } else { confirmQuit = false; Shell.QuitGame(); } }
                });
            }
            if (brightness >= 0)
                rows.Add(new Row { Kind = Kind.Slider, Title = "밝기", Num = brightness, Change = v => { brightness = v; SystemControls.SetBrightness(v); } });
            if (volume >= 0)
            {
                rows.Add(new Row { Kind = Kind.Slider, Title = mute ? "소리 (음소거)" : "소리", Num = volume, Change = v => { volume = v; mute = false; SystemControls.SetVolume(v); } });
                rows.Add(new Row { Kind = Kind.Toggle, Title = "음소거", On = mute, Change = v => { mute = v == 1; SystemControls.SetMute(mute); } });
            }
            rows.Add(new Row
            {
                Kind = Kind.Toggle, Title = "와이파이", On = wifi, Sub = radiosLoaded ? (hasWifi ? null : "와이파이 장치를 못 찾았어요") : "확인 중...",
                Change = v => { wifi = v == 1; Task.Run(() => SystemControls.SetRadio("WiFi", v == 1)); }
            });
            rows.Add(new Row
            {
                Kind = Kind.Toggle, Title = "블루투스", On = bt, Sub = radiosLoaded ? (hasBt ? null : "블루투스 장치를 못 찾았어요") : "확인 중...",
                Change = v => { bt = v == 1; Task.Run(() => SystemControls.SetRadio("Bluetooth", v == 1)); }
            });
            rows.Add(new Row { Kind = Kind.Choice, Title = "성능 모드", Choices = SystemControls.PowerNames, ChoiceIndex = power, Change = v => { power = v; SystemControls.SetPowerMode(v); } });
            rows.Add(new Row { Kind = Kind.Toggle, Title = "성능 표시 (화면 위)", On = OverlayForm.IsOn, Change = v => OverlayForm.Toggle(v == 1) });
            if (resList.Count > 1)
                rows.Add(new Row
                {
                    Kind = Kind.Choice, Title = "해상도", Choices = resList.ToArray(), ChoiceIndex = Math.Max(0, resIndex),
                    Change = v => { resIndex = v; SystemControls.SetResolution(resList[v]); Shell.AfterDisplayChange(); }
                });
            rows.Add(new Row
            {
                Kind = Kind.Choice, Title = "화면 회전", Choices = SystemControls.RotationNames, ChoiceIndex = rot,
                Change = v => { rot = v; SystemControls.SetRotation(v); try { resList = SystemControls.Resolutions(out resIndex); } catch { } Shell.AfterDisplayChange(); }
            });
            rows.Add(new Row
            {
                Kind = Kind.Action, Title = "메모리 정리", Sub = memNote,
                Act = () =>
                {
                    memNote = "정리하는 중...";
                    Task.Run(() =>
                    {
                        int mb = SystemControls.CleanMemory();
                        var m = SystemControls.Memory();
                        Shell.BeginInvoke(new Action(() =>
                        {
                            memNote = mb + "MB 비웠어요 · 남은 메모리 " + (m.Avail / 1048576) + "MB";
                            Shell.Invalidate();
                        }));
                    });
                }
            });
            // ── 팬·조명·TDP (PawnIO) ──
            if (Hardware.SmuReady)
            {
                var tdpNames = new List<string> { "기본" };
                for (int w = Hardware.TdpMin; w <= Hardware.TdpMax; w++) tdpNames.Add(w + "W");
                int ti = Settings.Tdp <= 0 ? 0 : Settings.Tdp - Hardware.TdpMin + 1;
                rows.Add(new Row
                {
                    Kind = Kind.Choice, Title = "TDP (전력 한계)", Choices = tdpNames.ToArray(), ChoiceIndex = ti,
                    Change = v =>
                    {
                        Settings.Tdp = v == 0 ? 0 : Hardware.TdpMin + v - 1;
                        Settings.Save();
                        int w2 = Settings.Tdp == 0 ? 12 : Settings.Tdp; // 기본 = 이 칩의 기본값 12W
                        Task.Run(() => Hardware.SetTdp(w2));
                    }
                });
            }
            if (Hardware.SmuReady)
            {
                var smartNames = new[] { "끔", "30 FPS", "40 FPS", "45 FPS", "60 FPS" };
                var smartVals = new[] { 0, 30, 40, 45, 60 };
                rows.Add(new Row
                {
                    Kind = Kind.Choice, Title = "스마트 TDP", Sub = Fps.Problem != "" && Settings.SmartFps > 0 ? Fps.Problem : "목표 FPS를 지키며 전력을 아껴요",
                    Choices = smartNames, ChoiceIndex = Math.Max(0, Array.IndexOf(smartVals, Settings.SmartFps)),
                    Change = v => { Settings.SmartFps = smartVals[v]; Settings.Save(); SmartTdp.Reset(); }
                });
            }
            if (Hardware.EcReady)
            {
                var fanNames = new List<string> { "자동" };
                for (int f = 20; f <= 100; f += 10) fanNames.Add(f + "%");
                int fi = Settings.Fan < 0 ? 0 : (Settings.Fan - 20) / 10 + 1;
                rows.Add(new Row
                {
                    Kind = Kind.Choice, Title = "팬 속도", Choices = fanNames.ToArray(), ChoiceIndex = fi,
                    Change = v =>
                    {
                        Settings.Fan = v == 0 ? -1 : 20 + (v - 1) * 10;
                        Settings.Save();
                        int fan = Settings.Fan;
                        Task.Run(() => Hardware.SetFan(fan));
                    }
                });
                rows.Add(new Row { Kind = Kind.Toggle, Title = "조이스틱 조명", On = Settings.LedOn, Change = v => { Settings.LedOn = v == 1; ApplyLed(); } });
                if (Settings.LedOn)
                {
                    int ci = Array.FindIndex(LedColors, c => c.R == Settings.LedR && c.G == Settings.LedG && c.B == Settings.LedB);
                    rows.Add(new Row
                    {
                        Kind = Kind.Choice, Title = "조명 색", Choices = LedNames, ChoiceIndex = Math.Max(0, ci),
                        Change = v => { Settings.LedR = LedColors[v].R; Settings.LedG = LedColors[v].G; Settings.LedB = LedColors[v].B; ApplyLed(); }
                    });
                    rows.Add(new Row { Kind = Kind.Slider, Title = "조명 밝기", Num = Settings.LedBright, Change = v => { Settings.LedBright = v; ApplyLed(); } });
                    rows.Add(new Row
                    {
                        Kind = Kind.Choice, Title = "조명 효과", Choices = new[] { "고정", "숨쉬기", "무지개" }, ChoiceIndex = Settings.LedEffect,
                        Change = v => { Settings.LedEffect = v; Settings.Save(); if (v == 0) ApplyLed(); else Hardware.UpdateEffect(); }
                    });
                }
            }
            if (!Hardware.Ready)
            {
                rows.Add(new Row
                {
                    Kind = Kind.Action, Title = "팬 · 조명 · TDP",
                    Sub = Settings.HardwareControl ? Hardware.Problem : "설정 → 고급 하드웨어 제어를 켜면 쓸 수 있어요",
                    Act = () => { Hide(); Shell.GoHome(); Shell.Push(new SettingsScreen()); }
                });
            }

            // ── 컨트롤러 (가상 패드) ──
            rows.Add(new Row
            {
                Kind = Kind.Toggle, Title = "가상 패드 (감도·연사·자이로·진동)", On = VirtualPad.Running,
                Sub = VirtualPad.Problem != "" ? VirtualPad.Problem : (VirtualPad.Running ? "켜짐 · 게임은 가상 패드를 봐요" : "켜면 컨트롤러 세부 설정이 열려요"),
                Change = v =>
                {
                    Settings.VPadOn = v == 1;
                    Settings.Save();
                    if (v == 1)
                    {
                        if (!VirtualPad.HasViGEm || !VirtualPad.HasHidHide) { VirtualPad.InstallDrivers(); Shell.ShowToast("드라이버 설치가 끝나면 다시 켜 주세요", 6000); Settings.VPadOn = false; Settings.Save(); return; }
                        Task.Run(() => { VirtualPad.Start(); Shell.BeginInvoke(new Action(Shell.Invalidate)); });
                    }
                    else Task.Run(() => { VirtualPad.Stop(); Shell.BeginInvoke(new Action(Shell.Invalidate)); });
                }
            });
            if (VirtualPad.Running)
            {
                rows.Add(new Row { Kind = Kind.Toggle, Title = "컨트롤러 끄기", Sub = "게임에 패드 입력을 안 보내요 (홈 버튼은 됨)", On = VirtualPad.ControllerOff, Change = v => VirtualPad.ControllerOff = v == 1 });
                rows.Add(new Row { Kind = Kind.Slider, Title = "진동 세기", Num = Settings.Vibration, Change = v => { Settings.Vibration = v; Settings.Save(); } });
                AddChoice("조이스틱 데드존", new[] { 0, 3, 5, 8, 10, 15, 20, 25 }, "%", Settings.StickDeadzone, v => Settings.StickDeadzone = v);
                AddChoice("조이스틱 감도", new[] { 60, 70, 80, 90, 100, 110, 120, 135, 150 }, "%", Settings.StickSens, v => Settings.StickSens = v);
                var turboNames = new[] { "끔", "A", "A·B", "A·B·X·Y", "RB" };
                var turboMasks = new[] { 0, 0x1000, 0x3000, 0xF000, 0x0200 };
                rows.Add(new Row
                {
                    Kind = Kind.Choice, Title = "연사 (버튼 오토)", Choices = turboNames, ChoiceIndex = Math.Max(0, Array.IndexOf(turboMasks, Settings.TurboMask)),
                    Change = v => { Settings.TurboMask = turboMasks[v]; Settings.Save(); }
                });
                if (Settings.TurboMask != 0) AddChoice("연사 속도", new[] { 6, 8, 10, 12, 15, 20 }, "회/초", Settings.TurboHz, v => Settings.TurboHz = v);
                rows.Add(new Row
                {
                    Kind = Kind.Choice, Title = "자이로 (모션)", Choices = new[] { "끔", "오른쪽 스틱", "마우스" }, ChoiceIndex = Settings.GyroMode,
                    Change = v => { Settings.GyroMode = v; Settings.Save(); }
                });
                if (Settings.GyroMode != 0)
                {
                    AddChoice("자이로 감도", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, "", Settings.GyroSens, v => Settings.GyroSens = v);
                    rows.Add(new Row { Kind = Kind.Choice, Title = "자이로 켜는 법", Choices = new[] { "항상", "LB 누를 때", "LT 누를 때" }, ChoiceIndex = Settings.GyroHold, Change = v => { Settings.GyroHold = v; Settings.Save(); } });
                    rows.Add(new Row { Kind = Kind.Choice, Title = "좌우 회전 축", Choices = new[] { "기본", "다른 축" }, ChoiceIndex = Settings.GyroAxis, Change = v => { Settings.GyroAxis = v; Settings.Save(); } });
                    rows.Add(new Row { Kind = Kind.Toggle, Title = "자이로 좌우 반전", On = Settings.GyroInvertX, Change = v => { Settings.GyroInvertX = v == 1; Settings.Save(); } });
                    rows.Add(new Row { Kind = Kind.Toggle, Title = "자이로 상하 반전", On = Settings.GyroInvertY, Change = v => { Settings.GyroInvertY = v == 1; Settings.Save(); } });
                }
            }

            if (game && Shell.PlayingGame != null)
            {
                var pg = Shell.PlayingGame;
                rows.Add(new Row
                {
                    Kind = Kind.Action, Title = "지금 화면을 표지로", Sub = pg.Title + " 표지를 지금 게임 화면으로 바꿔요",
                    Act = () => Shell.SendToGame(() =>
                    {
                        var tm = new System.Windows.Forms.Timer { Interval = 300 };
                        tm.Tick += (s2, e2) => { tm.Stop(); tm.Dispose(); bool ok = GameCapture.ShotNow(pg); if (ok) Shell.RefreshLists(); };
                        tm.Start();
                    })
                });
            }

            // ── 화면 확대 (FSR 용도) ──
            rows.Add(new Row
            {
                Kind = Kind.Choice, Title = "화면 확대", Choices = Scaler.ModeNames, ChoiceIndex = Scaler.Mode,
                Sub = Scaler.Problem != "" ? Scaler.Problem : "게임을 작은 창으로 띄운 뒤 켜면 화면에 꽉 차게 키워요",
                Change = v =>
                {
                    if (v == 0) { Scaler.Stop(); return; }
                    if (!GameWatch.Running) { Shell.ShowToast("게임을 켠 다음 여기서 켜 주세요", 3000); return; }
                    Shell.SendToGame(() => Scaler.Start(v, GetFg()));
                }
            });

            // ── 빠른 버튼 ──
            rows.Add(new Row { Kind = Kind.Toggle, Title = "조이스틱 마우스 모드", Sub = "왼쪽 스틱 이동 · A 클릭 · B 오른쪽 클릭 · 오른쪽 스틱 휠", On = Gamepad.MouseMode,
                               Change = v => Gamepad.MouseMode = v == 1 });
            rows.Add(new Row { Kind = Kind.Action, Title = "미니 모드", Sub = "런처를 작은 동그라미로 줄여요", Act = Shell.EnterMini });
            rows.Add(new Row { Kind = Kind.Action, Title = "ESC 보내기", Act = () => Shell.SendToGame(() => KeySender.Combo(KeySender.Esc)) });
            rows.Add(new Row { Kind = Kind.Action, Title = "전체 화면 전환 (Alt+Enter)", Act = () => Shell.SendToGame(() => KeySender.Combo(KeySender.Alt, KeySender.Enter)) });
            rows.Add(new Row { Kind = Kind.Action, Title = "스크린샷", Sub = "사진 › 스크린샷 폴더에 저장돼요", Act = () => Shell.SendToGame(() => KeySender.Combo(KeySender.Win, KeySender.PrtSc)) });
            rows.Add(new Row { Kind = Kind.Action, Title = "화상 키보드", Act = () => Shell.SendToGame(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("osk.exe") { UseShellExecute = true })) });
            rows.Add(new Row { Kind = Kind.Action, Title = "작업 전환 (멀티태스킹)", Act = () => Shell.SendToGame(() => KeySender.Combo(KeySender.Win, KeySender.Tab)) });
            rows.Add(new Row { Kind = Kind.Action, Title = "윈도우 시작 메뉴", Act = () => Shell.SendToGame(() => KeySender.Combo(KeySender.Win)) });
            rows.Add(new Row
            {
                Kind = Kind.Toggle, Title = "비행기 모드", Sub = "와이파이·블루투스 모두 끄기", On = radiosLoaded && !wifi && !bt && (hasWifi || hasBt),
                Change = v =>
                {
                    bool on = v == 0; // 비행기 모드 켜기 = 무선 끄기
                    wifi = hasWifi && on; bt = hasBt && on;
                    Task.Run(() => { SystemControls.SetRadio("WiFi", on); SystemControls.SetRadio("Bluetooth", on); });
                }
            });
            if (focus >= rows.Count) focus = rows.Count - 1;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] static extern IntPtr GetFg();

        void AddChoice(string title, int[] vals, string unit, int cur, Action<int> set)
        {
            var names = new string[vals.Length];
            for (int i = 0; i < vals.Length; i++) names[i] = vals[i] + unit;
            int idx = Array.IndexOf(vals, cur);
            if (idx < 0) { idx = 0; for (int i = 0; i < vals.Length; i++) if (vals[i] <= cur) idx = i; }
            rows.Add(new Row { Kind = Kind.Choice, Title = title, Choices = names, ChoiceIndex = idx, Change = v => { set(vals[v]); Settings.Save(); } });
        }

        static readonly string[] LedNames = { "흰색", "빨강", "주황", "노랑", "초록", "하늘", "파랑", "보라", "분홍" };
        static readonly Color[] LedColors =
        {
            Color.FromArgb(255, 255, 255), Color.FromArgb(255, 0, 0), Color.FromArgb(255, 110, 0), Color.FromArgb(255, 210, 0),
            Color.FromArgb(0, 255, 40), Color.FromArgb(0, 200, 255), Color.FromArgb(0, 40, 255), Color.FromArgb(150, 0, 255), Color.FromArgb(255, 60, 160)
        };

        System.Threading.Timer ledTimer;
        double volt; int voltNext;

        /// <summary>조명은 여러 번 바꿔도 마지막 것만 적용 (밝기 끌 때 EC에 너무 많이 안 쓰게)</summary>
        void ApplyLed()
        {
            Settings.LedSet = true;
            Settings.Save();
            ledTimer?.Dispose();
            ledTimer = new System.Threading.Timer(_ =>
            {
                Hardware.SetLed(Settings.LedOn, Settings.LedR, Settings.LedG, Settings.LedB, Settings.LedBright);
            }, null, 250, System.Threading.Timeout.Infinite);
        }

        public bool Tick(float dt)
        {
            float target = Open ? 1f : 0f;
            float before = T;
            T = Draw.Lerp(T, target, dt * 16f);
            if (Math.Abs(T - target) < 0.01f) T = target;
            return Math.Abs(T - before) > 0.0001f;
        }

        int S(float v) => Theme.S(v);

        public void Paint(Graphics g, Rectangle full)
        {
            if (T <= 0) return;
            Build();
            using (var b = new SolidBrush(Color.FromArgb((int)(140 * T), 0, 0, 0))) g.FillRectangle(b, full);
            int w = S(460);
            float e = 1 - (1 - T) * (1 - T) * (1 - T);
            int x = (int)(full.Width - w * e);
            panel = new Rectangle(x, 0, w, full.Height);
            using (var b = new SolidBrush(Color.FromArgb(14, 20, 27))) g.FillRectangle(b, panel);
            using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawLine(p, x, 0, x, full.Height);

            // 머리: 시간·배터리
            Draw.Text(g, DateTime.Now.ToString("HH:mm"), Draw.H1, new Rectangle(x + S(28), S(18), w, S(36)), Theme.Text);
            string head = SystemControls.BatteryText();
            if (Hardware.TempReady) { float tc = Hardware.CpuTemp(); if (tc > 0) head += " · CPU " + (int)tc + "°C"; }
            if (voltNext < Environment.TickCount) { volt = SystemControls.BatteryVolts(); voltNext = Environment.TickCount + 5000; }
            if (volt > 0) head += " · " + volt.ToString("0.00") + "V";
            Draw.Text(g, head, Draw.Body, new Rectangle(x + S(28), S(56), w - S(40), S(22)), Theme.SubText);

            int top = S(96), bottom = full.Height - S(60);
            var clip = g.Clip;
            g.SetClip(new Rectangle(x, top, w, bottom - top));

            // 포커스가 보이게 스크롤
            int y0 = top + S(6);
            int fy = 0;
            for (int i = 0; i < rows.Count; i++) { if (i == focus) break; fy += RowH(rows[i]) + S(8); }
            int fh = rows.Count > 0 ? RowH(rows[focus]) : 0;
            if (fy - scroll < 0) scroll = fy;
            if (fy + fh - scroll > bottom - y0) scroll = fy + fh - (bottom - y0);
            if (scroll < 0) scroll = 0;

            int y = y0 - (int)scroll;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int h = RowH(row);
                var r = new Rectangle(x + S(14), y, w - S(28), h);
                row.Rect = r;
                bool f = i == focus;
                Draw.Fill(g, r, S(4), f ? Theme.TileHot : Color.FromArgb(255, 23, 29, 37));
                var tc = row.Act == null && row.Kind == Kind.Action ? Theme.Dim : row.Danger ? Color.FromArgb(240, 140, 140) : Theme.Text;
                int ix = r.X + S(18);
                switch (row.Kind)
                {
                    case Kind.Slider:
                        Draw.Text(g, row.Title, Draw.Body, new Rectangle(ix, r.Y + S(8), r.Width, S(22)), tc);
                        Draw.Text(g, row.Num + "%", Draw.BodyBold, new Rectangle(r.Right - S(80), r.Y + S(8), S(62), S(22)), Theme.SubText, Draw.Right);
                        var track = new Rectangle(ix, r.Y + S(40), r.Width - S(36), S(6));
                        Draw.Fill(g, track, S(3), Color.FromArgb(70, 78, 90));
                        Draw.Fill(g, new Rectangle(track.X, track.Y, track.Width * row.Num / 100, track.Height), S(3), Theme.Accent);
                        int kx = track.X + track.Width * row.Num / 100;
                        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - S(9), track.Y + S(3) - S(9), S(18), S(18));
                        break;
                    case Kind.Toggle:
                        Draw.Text(g, row.Title, Draw.Body, new Rectangle(ix, r.Y, r.Width - S(100), row.Sub != null ? h - S(18) : h), tc);
                        if (row.Sub != null) Draw.Text(g, row.Sub, Draw.Small, new Rectangle(ix, r.Y + h - S(26), r.Width, S(20)), Theme.SubText);
                        var t = new Rectangle(r.Right - S(18) - S(50), r.Y + (h - S(26)) / 2, S(50), S(26));
                        Draw.Fill(g, t, S(13), row.On ? Theme.Accent : Color.FromArgb(70, 78, 90));
                        using (var b = new SolidBrush(Color.White))
                            g.FillEllipse(b, row.On ? t.Right - S(23) : t.X + S(3), t.Y + S(3), S(20), S(20));
                        break;
                    case Kind.Choice:
                        Draw.Text(g, row.Title, Draw.Body, new Rectangle(ix, r.Y, r.Width / 2, h), tc);
                        string v = row.Choices[Math.Max(0, Math.Min(row.Choices.Length - 1, row.ChoiceIndex))];
                        var vr = new Rectangle(r.Right - S(210), r.Y, S(192), h);
                        Draw.Text(g, "‹", Draw.H2, new Rectangle(vr.X, vr.Y, S(24), h), f ? Theme.Text : Theme.Dim, Draw.Center);
                        Draw.Text(g, v, Draw.BodyBold, new Rectangle(vr.X + S(24), vr.Y, vr.Width - S(48), h), Theme.Text, Draw.Center);
                        Draw.Text(g, "›", Draw.H2, new Rectangle(vr.Right - S(24), vr.Y, S(24), h), f ? Theme.Text : Theme.Dim, Draw.Center);
                        break;
                    default:
                        Draw.Text(g, row.Title, Draw.Body, new Rectangle(ix, r.Y, r.Width, row.Sub != null ? h - S(18) : h), tc);
                        if (row.Sub != null) Draw.Text(g, row.Sub, Draw.Small, new Rectangle(ix, r.Y + h - S(26), r.Width - S(30), S(20)), Theme.SubText);
                        break;
                }
                y += h + S(8);
            }
            g.Clip = clip;
        }

        int RowH(Row r) => r.Kind == Kind.Slider ? S(62) : r.Sub != null ? S(62) : S(50);

        void Adjust(int dir)
        {
            if (focus < 0 || focus >= rows.Count) return;
            var r = rows[focus];
            if (r.Kind == Kind.Slider) r.Change(Math.Max(0, Math.Min(100, r.Num + dir * 5)));
            else if (r.Kind == Kind.Choice) r.Change((r.ChoiceIndex + dir + r.Choices.Length) % r.Choices.Length);
            else if (r.Kind == Kind.Toggle && (dir > 0) != r.On) r.Change(dir > 0 ? 1 : 0);
            Shell.Invalidate();
        }

        void Activate(Row r)
        {
            if (r.Kind == Kind.Toggle) r.Change(r.On ? 0 : 1);
            else if (r.Kind == Kind.Choice) r.Change((r.ChoiceIndex + 1) % r.Choices.Length);
            else if (r.Kind == Kind.Action) r.Act?.Invoke();
            Shell.Invalidate();
        }

        public void OnPad(Pad b)
        {
            Build();
            switch (b)
            {
                case Pad.Up: focus = Math.Max(0, focus - 1); confirmQuit = false; break;
                case Pad.Down: focus = Math.Min(rows.Count - 1, focus + 1); confirmQuit = false; break;
                case Pad.Left: Adjust(-1); break;
                case Pad.Right: Adjust(1); break;
                case Pad.A: if (focus < rows.Count) Activate(rows[focus]); break;
                case Pad.B: case Pad.Start:
                    if (GameWatch.Running && Shell.CameFromGame) Shell.ReturnToGame(); else Hide();
                    return;
            }
            Shell.Invalidate();
        }

        public void OnTap(Point p)
        {
            if (!panel.Contains(p))
            {
                if (GameWatch.Running && Shell.CameFromGame) Shell.ReturnToGame(); else Hide();
                return;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (!r.Rect.Contains(p)) continue;
                focus = i;
                if (r.Kind == Kind.Slider)
                {
                    int tx = r.Rect.X + S(18), tw = r.Rect.Width - S(36);
                    r.Change(Math.Max(0, Math.Min(100, (p.X - tx) * 100 / Math.Max(1, tw))));
                }
                else if (r.Kind == Kind.Choice)
                {
                    if (p.X < r.Rect.Right - S(105)) r.Change((r.ChoiceIndex - 1 + r.Choices.Length) % r.Choices.Length);
                    else r.Change((r.ChoiceIndex + 1) % r.Choices.Length);
                }
                else Activate(r);
                Shell.Invalidate();
                return;
            }
        }

        /// <summary>슬라이더 끌기</summary>
        public void OnDrag(Point p)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Kind != Kind.Slider || !r.Rect.Contains(p.X, r.Rect.Y + 1) || p.Y < r.Rect.Y - S(20) || p.Y > r.Rect.Bottom + S(20)) continue;
                int tx = r.Rect.X + S(18), tw = r.Rect.Width - S(36);
                int v = Math.Max(0, Math.Min(100, (p.X - tx) * 100 / Math.Max(1, tw)));
                if (v != r.Num) r.Change(v);
                Shell.Invalidate();
                return;
            }
        }

        public List<Hint> Hints()
        {
            return new List<Hint>
            {
                new Hint(Pad.A, "선택", () => OnPad(Pad.A)),
                new Hint(Pad.B, GameWatch.Running && Shell.CameFromGame ? "게임으로" : "닫기", () => OnPad(Pad.B)),
            };
        }
    }
}
