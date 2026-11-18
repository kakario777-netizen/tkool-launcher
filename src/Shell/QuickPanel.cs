using System;
using System.Collections.Generic;
using System.Linq;
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
        public Control Host;        // 그려질 곳 (런처 화면 또는 오른쪽 팝업 창)
        public bool Popup;          // 게임 위에 뜬 오른쪽 팝업인지
        public bool Open;

        void Redraw() { if (Host != null) Host.Invalidate(); else Shell.Invalidate(); }
        public float T;
        int focus;
        float scroll;
        Rectangle panel;

        enum Kind { Slider, Toggle, Choice, Action }

        class Row
        {
            public Kind Kind;
            public string Title, Sub; public bool Keep;
            public int Num;                 // 슬라이더 값 0~100
            public bool On;
            public string[] Choices;
            public int ChoiceIndex;
            public Action<int> Change;      // 슬라이더·선택: 새 값 / 토글: 1·0
            public Action Act;
            public Rectangle Rect;
            public bool Danger;
            public int Cat;
            public int FixCat = -1;         // 정해진 탭 (제목으로 분류하지 않음)
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
            if (Popup) { Shell.ClosePopup(); return; }
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
                    Redraw();
                }));
            });
        }

        void Build()
        {
            rows = new List<Row>();
            bool game = GameWatch.Running;
            if (Popup)
                rows.Add(new Row { Kind = Kind.Action, Title = "런처 열기", Sub = "게임 목록·설정 화면으로", Act = () => { Shell.ClosePopup(false); Shell.SmallAction(); } });
            bool fgApp = !game && Popup && Shell.CanQuitForeground;   // 런처로 안 켠 프로그램이 앞에 있었음
            if (game || fgApp)
            {
                var pg = Shell.PlayingGame;
                if (game) rows.Add(new Row { Kind = Kind.Action, Title = "게임으로 돌아가기", Sub = pg != null ? pg.Title + " · " + PlayText(Shell.PlayingFor) + " 하는 중" : null, Act = Shell.ReturnToGame });
                rows.Add(new Row
                {
                    Kind = Kind.Action, Danger = true,
                    Title = confirmQuit ? "한 번 더 누르면 게임을 끝내요" : Shell.GameProblem != null ? "게임 끝내기 (문제 생김)" : "게임 끝내기",
                    Keep = confirmQuit, Sub = confirmQuit ? "저장 안 한 내용은 사라져요" : Shell.GameProblem ?? (fgApp ? "앞에 있던 프로그램을 끝내요" : null),
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
            if (Hardware.Ready)
            {
                int pm = PerfMode.Mode;
                rows.Add(new Row
                {
                    Kind = Kind.Choice, Title = "성능 모드", Choices = PerfMode.Names, ChoiceIndex = pm,
                    Sub = PerfMode.Describe(pm),
                    Change = v => { PerfMode.Set(v); power = SystemControls.GetPowerMode(); }
                });
                rows.Add(new Row { Kind = Kind.Action, Title = "지금 상태", Keep = true, Sub = PerfMode.Live(), Act = () => { } });
            }
            else
                rows.Add(new Row { Kind = Kind.Choice, Title = "윈도우 전원 모드", Choices = SystemControls.PowerNames, ChoiceIndex = power, Change = v => { power = v; SystemControls.SetPowerMode(v); } });
            rows.Add(new Row { Kind = Kind.Toggle, Title = "성능 표시 (화면 위)", On = OverlayForm.IsOn, Change = v => OverlayForm.Toggle(v == 1) });
            AddOverlayRows();
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
            rows.Add(new Row { Kind = Kind.Action, Title = "치트 에디터", Sub = "미니 치트 켜기", Act = () => Shell.OpenCheat() });
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
                            Redraw();
                        }));
                    });
                }
            });
            // ── 팬·조명·TDP (PawnIO) ──
            if (Hardware.SmuReady)
            {
                var tdpNames = new List<string> { "기본" };
                for (int w = Hardware.TdpMin; w <= Hardware.TdpMax; w++) tdpNames.Add(w + "W");
                int ti = PerfMode.Mode != PerfMode.Custom ? PerfMode.BaseTdp - Hardware.TdpMin + 1 : Settings.Tdp <= 0 ? 0 : Settings.Tdp - Hardware.TdpMin + 1;
                rows.Add(new Row
                {
                    Kind = Kind.Choice, Title = "TDP (전력 한계)", Choices = tdpNames.ToArray(), ChoiceIndex = ti,
                    Change = v =>
                    {
                        Settings.Tdp = v == 0 ? 0 : Hardware.TdpMin + v - 1;
                        if (PerfMode.Mode != PerfMode.Custom) { Settings.PerfMode = PerfMode.Custom; PerfMode.Apply(); }   // 직접 고르면 '직접 설정'으로
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
                if (PerfMode.Mode != PerfMode.Custom) { fanNames[0] = "모드가 조절"; fi = 0; }
                rows.Add(new Row
                {
                    Kind = Kind.Choice, Title = "팬 속도", Choices = fanNames.ToArray(), ChoiceIndex = fi,
                    Change = v =>
                    {
                        Settings.Fan = v == 0 ? -1 : 20 + (v - 1) * 10;
                        if (PerfMode.Mode != PerfMode.Custom) { Settings.PerfMode = PerfMode.Custom; PerfMode.Apply(); }
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
                    Sub = Settings.HardwareControl ? Hardware.Problem : "설정에서 하드웨어 제어를 켜세요",
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
            if (!(Popup && GameWatch.Running))   // 게임 위 팝업에선 위쪽 큰 단추로 따로 뺌
                rows.Add(new Row { Kind = Kind.Action, Title = "전체 화면 전환", Act = () => Shell.SendToGame(() => KeySender.Combo(KeySender.Alt, KeySender.Enter)) });
            rows.Add(new Row { Kind = Kind.Action, Title = "스크린샷", Sub = "사진 › 스크린샷 폴더에 저장돼요", Act = () => Shell.SendToGame(() => KeySender.Combo(KeySender.Win, KeySender.PrtSc)) });
            rows.Add(new Row { Kind = Kind.Action, Title = "화상 키보드", Act = () => Shell.SendToGame(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("osk.exe") { UseShellExecute = true })) });
            rows.Add(new Row { Kind = Kind.Action, Title = "작업 전환", Act = () => Shell.SendToGame(() => KeySender.Combo(KeySender.Win, KeySender.Tab)) });
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

        // ── 오버레이 탭: 모양·위치·크기·색·진하기·보일 항목 ──
        void AddOverlayRows()
        {
            Action apply = () => { Settings.Save(); OverlayForm.ApplyStyle(); };
            rows.Add(new Row { FixCat = 6, Kind = Kind.Toggle, Title = "성능 표시 켜기", On = OverlayForm.IsOn, Change = v => OverlayForm.Toggle(v == 1) });
            rows.Add(new Row { FixCat = 6, Kind = Kind.Choice, Title = "모양", Choices = OverlayForm.StyleNames, ChoiceIndex = Clamp(Settings.OvStyle, OverlayForm.StyleNames), Change = v => { Settings.OvStyle = v; apply(); } });
            rows.Add(new Row { FixCat = 6, Kind = Kind.Choice, Title = "위치", Choices = OverlayForm.PosNames, ChoiceIndex = Clamp(Settings.OvPos, OverlayForm.PosNames), Change = v => { Settings.OvPos = v; apply(); } });
            rows.Add(new Row { FixCat = 6, Kind = Kind.Choice, Title = "가장자리 여백", Choices = OverlayForm.MarginNames, ChoiceIndex = Clamp(Settings.OvMargin, OverlayForm.MarginNames), Change = v => { Settings.OvMargin = v; apply(); } });
            rows.Add(new Row
            {
                FixCat = 6, Kind = Kind.Action, Title = OverlayForm.Editing ? "옮기는 중 — 끝내기" : "끌어서 위치 옮기기",
                Sub = "마우스나 손가락으로 끌어 놓고, 한 번 톡 누르면 고정",
                Act = () => { if (OverlayForm.Editing) OverlayForm.StopMove(); else { OverlayForm.StartMove(); Hide(); } }
            });
            rows.Add(new Row { FixCat = 6, Kind = Kind.Choice, Title = "크기", Choices = OverlayForm.SizeNames, ChoiceIndex = Clamp(Settings.OvSize, OverlayForm.SizeNames), Change = v => { Settings.OvSize = v; apply(); } });
            rows.Add(new Row { FixCat = 6, Kind = Kind.Choice, Title = "글자 색", Choices = OverlayForm.ColorNames, ChoiceIndex = Clamp(Settings.OvColor, OverlayForm.ColorNames), Change = v => { Settings.OvColor = v; apply(); } });
            rows.Add(new Row { FixCat = 6, Kind = Kind.Slider, Title = "배경 진하기", Num = Settings.OvBg, Change = v => { Settings.OvBg = v; apply(); } });
            rows.Add(new Row { FixCat = 6, Kind = Kind.Slider, Title = "글자 진하기", Num = Settings.OvText, Change = v => { Settings.OvText = Math.Max(20, v); apply(); } });
            for (int i = 0; i < OverlayForm.ItemNames.Length; i++)
            {
                int bit = 1 << i;
                rows.Add(new Row
                {
                    FixCat = 6, Kind = Kind.Toggle, Title = "보이기 · " + OverlayForm.ItemNames[i], On = (Settings.OvItems & bit) != 0,
                    Sub = i == 0 && Settings.OvStyle == 2 ? "'FPS만 크게' 모양에선 FPS만 보여요" : null,
                    Change = v => { Settings.OvItems = v == 1 ? Settings.OvItems | bit : Settings.OvItems & ~bit; apply(); }
                });
            }
        }

        public static void GoShortcuts() { tab = 5; }

        static string PlayText(TimeSpan t) => t.TotalMinutes < 1 ? "방금 시작" : t.TotalHours >= 1 ? (int)t.TotalHours + "시간 " + t.Minutes + "분째" : (int)t.TotalMinutes + "분째";

        static int Clamp(int v, string[] a) => Math.Max(0, Math.Min(a.Length - 1, v));

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

        // ═════════ AYASpace 모양: 위 빠른 타일 줄 + 왼쪽 탭 + 오른쪽 기능 ═════════
        public static readonly string[] TabNames = { "성능", "화면", "소리·연결", "컨트롤러", "조명", "바로가기", "오버레이" };
        static int tab = 5;             // 지금 탭 (껐다 켜도 기억) — 처음은 바로가기
        // 왼쪽 탭 보이는 순서: 바로가기 → 성능 → 화면 → 소리·연결 → 컨트롤러 → 조명 → 오버레이
        static readonly int[] TabOrder = { 5, 0, 1, 2, 3, 4, 6 };
        static int TabPos(int t) { int p = Array.IndexOf(TabOrder, t); return p < 0 ? 0 : p; }
        int zone = 1;                   // 0 = 위 타일 줄, 1 = 오른쪽 기능
        int tileFocus;
        readonly List<Rectangle> tabRects = new List<Rectangle>();
        readonly List<Rectangle> tileRects = new List<Rectangle>();
        List<Row> view = new List<Row>();     // 지금 탭에 보이는 줄
        List<Row> tiles = new List<Row>();

        static readonly string[][] Order =
        {
            new[] { "성능 모드", "지금 상태", "팬 속도", "TDP", "스마트 TDP", "윈도우 전원 모드", "성능 표시", "팬 · 조명 · TDP" },
            new[] { "밝기", "화면 확대", "해상도", "화면 회전" },
            new[] { "소리", "음소거", "와이파이", "블루투스", "비행기 모드" },
            new[] { "가상 패드", "컨트롤러 끄기", "진동 세기", "조이스틱 데드존", "조이스틱 감도", "연사 (", "연사 속도", "자이로 (", "자이로 감도", "자이로 켜는 법", "좌우 회전 축", "자이로 좌우", "자이로 상하", "조이스틱 마우스 모드" },
            new[] { "조이스틱 조명", "조명 색", "조명 밝기", "조명 효과" },
            new[] { "게임으로 돌아가기", "게임 끝내기", "한 번 더 누르면", "치트 에디터", "지금 화면을 표지로", "런처 열기", "스크린샷", "ESC 보내기", "전체 화면 전환", "화상 키보드", "작업 전환", "윈도우 시작 메뉴", "메모리 정리", "미니 모드" },
            new string[0],   // 오버레이 (FixCat으로 들어감)
        };

        static int Rank(int cat, string title)
        {
            var o = Order[cat];
            for (int i = 0; i < o.Length; i++) if (title.StartsWith(o[i])) return i;
            return -1;
        }

        void Categorize()
        {
            foreach (var r in rows)
            {
                r.Cat = r.FixCat;
                if (r.Cat >= 0) continue;
                for (int c = 0; c < Order.Length && r.Cat < 0; c++) if (Rank(c, r.Title) >= 0) r.Cat = c;
                if (r.Cat < 0) r.Cat = r.Kind == Kind.Action ? 5 : 0;
            }
            view = rows.Where(r => r.Cat == tab).OrderBy(r => { int k = Rank(tab, r.Title); return k < 0 ? 999 : k; }).ToList();
            if (tab == 4 && view.Count == 0)
                view.Add(new Row { Kind = Kind.Action, Title = "조명 쓰려면", Sub = "설정 → 고급 하드웨어 제어를 켜 주세요", Cat = 4,
                                   Act = () => { Hide(); Shell.GoHome(); Shell.Push(new SettingsScreen()); } });
            // 위 빠른 타일: 와이파이·블루투스·음소거·성능 표시·마우스 모드
            tiles = new List<Row>();
            foreach (var t in new[] { "와이파이", "블루투스", "비행기 모드", "음소거", "성능 표시", "조이스틱 마우스 모드" })
            {
                var r = rows.FirstOrDefault(x => x.Title.StartsWith(t) && x.Kind == Kind.Toggle);
                if (r != null) tiles.Add(r);
            }
            // 게임 중 팝업: 타일 아래 줄 전체 폭 "전체 화면 ↔ 창" 단추 (창으로 돌아오면 화면 가운데로)
            if (Popup && GameWatch.Running)
                tiles.Add(new Row { Kind = Kind.Action, Title = "전체 화면 ↔ 창 모드", Act = () => Shell.ToggleFullscreen() });
            if (focus >= view.Count) focus = Math.Max(0, view.Count - 1);
            if (tileFocus >= tiles.Count) tileFocus = Math.Max(0, tiles.Count - 1);
        }

        static readonly string[] TileNames = { "와이파이", "블루투스", "비행기 모드", "음소거", "성능 표시", "마우스" };

        bool IsGrid => tab == 5;   // 바로가기 탭은 2열 단추판

        /// <summary>빠른 설정판 폭 — 런처 안이든 게임 위 팝업이든 똑같이 (화면 가로의 약 26%)</summary>
        public static int PanelWidth()
        {
            int sw = 1920;
            try { sw = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width; } catch { }
            return Math.Max(Theme.S(360), (int)(sw * 0.26f));
        }

        public void Paint(Graphics g, Rectangle full)
        {
            if (T <= 0) return;
            try { Build(); Categorize(); } catch { }
            using (var b = new SolidBrush(Color.FromArgb((int)(140 * T), 0, 0, 0))) g.FillRectangle(b, full);
            int w = Popup ? full.Width : Math.Min(full.Width, PanelWidth());
            float e = 1 - (1 - T) * (1 - T) * (1 - T);
            int x = (int)(full.Width - w * e);
            panel = new Rectangle(x, 0, w, full.Height);
            using (var b = new SolidBrush(Color.FromArgb(14, 20, 27))) g.FillRectangle(b, panel);
            using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawLine(p, x, 0, x, full.Height);

            // ── 머리: 시간·배터리·온도·전압 ──
            Draw.Text(g, DateTime.Now.ToString("HH:mm"), Draw.H1, new Rectangle(x + S(24), S(14), S(120), S(36)), Theme.Text);
            string head = SystemControls.BatteryText();
            try { if (Hardware.TempReady) { float tc = Hardware.CpuTemp(); if (tc > 0) head += " · CPU " + (int)tc + "°C"; } } catch { }
            if (voltNext < Environment.TickCount) { try { volt = SystemControls.BatteryVolts(); } catch { volt = 0; } voltNext = Environment.TickCount + 5000; }
            if (volt > 0) head += " · " + volt.ToString("0.00") + "V";
            if (Settings.Tdp > 0 || (Settings.SmartFps > 0 && GameWatch.Running))
                head += " · " + (Settings.SmartFps > 0 && GameWatch.Running ? SmartTdp.Current : Settings.Tdp) + "W";
            Draw.Text(g, head, Draw.Small, new Rectangle(x + S(24), S(50), w - S(40), S(20)), Theme.SubText);

            // ── 빠른 타일 줄 ──
            tileRects.Clear();
            // 두 줄 × 3개
            int ty = S(80), tg = S(8), th1 = S(58);
            int tw = (w - S(32) - tg * 2) / 3;
            int nToggle = tiles.Count(t => t.Kind == Kind.Toggle);
            int trows = (nToggle + 2) / 3;
            int th = trows * th1 + Math.Max(0, trows - 1) * tg;
            int bigH = S(48);
            if (tiles.Count > nToggle) th += tg + bigH;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i].Kind != Kind.Toggle)
                {
                    // 전체 폭 큰 단추
                    var br = new Rectangle(x + S(16), ty + trows * (th1 + tg), w - S(32), bigH);
                    tileRects.Add(br);
                    Draw.Fill(g, br, S(8), Color.FromArgb(255, 52, 62, 78));
                    Draw.Text(g, tiles[i].Title, Draw.BodyBold, br, Color.White, Draw.Center);
                    if (zone == 0 && i == tileFocus) Draw.Ring(g, Rectangle.Inflate(br, S(2), S(2)), S(9), Theme.Focus, S(2.5f));
                    continue;
                }
                var r = new Rectangle(x + S(16) + (i % 3) * (tw + tg), ty + (i / 3) * (th1 + tg), tw, th1);
                tileRects.Add(r);
                bool on = tiles[i].On;
                Draw.Fill(g, r, S(8), on ? Theme.Accent : Color.FromArgb(35, 41, 51));
                int ti = Array.FindIndex(new[] { "와이파이", "블루투스", "비행기 모드", "음소거", "성능 표시", "조이스틱 마우스 모드" }, t => tiles[i].Title.StartsWith(t));
                string name = ti >= 0 ? TileNames[ti] : tiles[i].Title;
                Draw.Text(g, name, Draw.Label, new Rectangle(r.X + S(4), r.Y + S(6), r.Width - S(8), S(24)), Color.White, Draw.Center | TextFormatFlags.EndEllipsis);
                // 켜짐/꺼짐 글자 대신 토글 스위치 모양
                var sw = new Rectangle(r.X + (r.Width - S(40)) / 2, r.Y + S(32), S(40), S(20));
                Draw.Fill(g, sw, S(10), on ? Color.FromArgb(255, 255, 255) : Color.FromArgb(70, 78, 90));
                using (var kb = new SolidBrush(on ? Theme.Accent : Color.FromArgb(200, 205, 212))) g.FillEllipse(kb, on ? sw.Right - S(18) : sw.X + S(2), sw.Y + S(2), S(16), S(16));
                if (zone == 0 && i == tileFocus) Draw.Ring(g, Rectangle.Inflate(r, S(2), S(2)), S(9), Theme.Focus, S(2.5f));
            }

            // ── 왼쪽 탭 ──
            int top = ty + th + S(14), bottom = full.Height - S(60);
            int railW = w < S(480) ? S(76) : S(92);
            tabRects.Clear();
            int tabH = Math.Min(S(64), (bottom - top) / TabNames.Length);
            for (int k = 0; k < TabOrder.Length; k++)
            {
                int i = TabOrder[k];
                var r = new Rectangle(x + S(8), top + k * tabH, railW - S(8), tabH - S(6));
                tabRects.Add(r);
                bool on = i == tab;
                if (on)
                {
                    Draw.Fill(g, r, S(6), Color.FromArgb(48, 56, 68));
                    Draw.Fill(g, new Rectangle(r.X, r.Y + S(10), S(4), r.Height - S(20)), S(2), Theme.Accent);
                }
                TabIcon(g, i, new Rectangle(r.X, r.Y + S(6), r.Width, S(22)), on ? Theme.Text : Theme.SubText);
                Draw.Text(g, TabNames[i], on ? Theme.Px(12, true) : Draw.Tiny, new Rectangle(r.X, r.Y + S(30), r.Width, S(20)), on ? Theme.Text : Theme.SubText, Draw.Center);
            }

            // ── 오른쪽 기능 ──
            int cx = x + railW + S(6), cw = w - railW - S(20);
            var area = new Rectangle(cx, top, cw, bottom - top);
            var clip = g.Clip;
            g.SetClip(area);
            if (IsGrid) PaintGrid(g, area); else PaintList(g, area);
            g.Clip = clip;
        }

        void TabIcon(Graphics g, int i, Rectangle r, Color c)
        {
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var p = new Pen(c, S(2)))
            using (var b = new SolidBrush(c))
            {
                switch (i)
                {
                    case 0: // 성능: 번개
                        g.FillPolygon(b, new[] { new Point(cx + S(2), cy - S(10)), new Point(cx - S(6), cy + S(1)), new Point(cx, cy + S(1)), new Point(cx - S(2), cy + S(10)), new Point(cx + S(6), cy - S(2)), new Point(cx, cy - S(2)) });
                        break;
                    case 1: // 화면: 해
                        g.DrawEllipse(p, cx - S(5), cy - S(5), S(10), S(10));
                        for (int k = 0; k < 8; k++)
                        {
                            double a = k * Math.PI / 4;
                            g.DrawLine(p, cx + (float)Math.Cos(a) * S(8), cy + (float)Math.Sin(a) * S(8), cx + (float)Math.Cos(a) * S(11), cy + (float)Math.Sin(a) * S(11));
                        }
                        break;
                    case 2: // 소리: 스피커
                        g.FillPolygon(b, new[] { new Point(cx - S(9), cy - S(4)), new Point(cx - S(5), cy - S(4)), new Point(cx + S(1), cy - S(9)), new Point(cx + S(1), cy + S(9)), new Point(cx - S(5), cy + S(4)), new Point(cx - S(9), cy + S(4)) });
                        g.DrawArc(p, cx - S(2), cy - S(6), S(10), S(12), -50, 100);
                        break;
                    case 3: // 컨트롤러
                        g.DrawArc(p, cx - S(12), cy - S(7), S(24), S(14), 0, 360);
                        g.DrawLine(p, cx - S(7), cy, cx - S(3), cy); g.DrawLine(p, cx - S(5), cy - S(2), cx - S(5), cy + S(2));
                        g.FillEllipse(b, cx + S(4), cy - S(2), S(4), S(4));
                        break;
                    case 4: // 조명: 전구
                        g.DrawEllipse(p, cx - S(6), cy - S(9), S(12), S(12));
                        g.DrawLine(p, cx - S(3), cy + S(6), cx + S(3), cy + S(6)); g.DrawLine(p, cx - S(2), cy + S(9), cx + S(2), cy + S(9));
                        break;
                    case 6: // 오버레이: 화면 속 그래프
                        g.DrawRectangle(p, cx - S(11), cy - S(8), S(22), S(16));
                        g.DrawLines(p, new[] { new Point(cx - S(7), cy + S(4)), new Point(cx - S(2), cy - S(1)), new Point(cx + S(2), cy + S(2)), new Point(cx + S(7), cy - S(4)) });
                        break;
                    default: // 바로가기: 격자
                        for (int k = 0; k < 4; k++) g.FillRectangle(b, cx - S(9) + (k % 2) * S(10), cy - S(9) + (k / 2) * S(10), S(8), S(8));
                        break;
                }
            }
        }

        void PaintList(Graphics g, Rectangle area)
        {
            int fy = 0;
            for (int i = 0; i < view.Count && i < focus; i++) fy += RowH(view[i]) + S(8);
            int fh = view.Count > 0 ? RowH(view[Math.Min(focus, view.Count - 1)]) : 0;
            if (fy - scroll < 0) scroll = fy;
            if (fy + fh - scroll > area.Height) scroll = fy + fh - area.Height;
            if (scroll < 0) scroll = 0;

            int y = area.Y - (int)scroll;
            for (int i = 0; i < view.Count; i++)
            {
                var row = view[i];
                int h = RowH(row);
                var r = new Rectangle(area.X, y, area.Width, h);
                row.Rect = r;
                bool f = zone == 1 && i == focus;
                Draw.Fill(g, r, S(6), f ? Theme.TileHot : Color.FromArgb(255, 23, 29, 37));
                var tc = row.Act == null && row.Kind == Kind.Action ? Theme.Dim : row.Danger ? Color.FromArgb(240, 140, 140) : Theme.Text;
                int ix = r.X + S(16);
                switch (row.Kind)
                {
                    case Kind.Slider:
                        Draw.Text(g, TitleOf(row), Draw.Body, new Rectangle(ix, r.Y + S(8), r.Width, S(22)), tc);
                        Draw.Text(g, row.Num + "%", Draw.BodyBold, new Rectangle(r.Right - S(80), r.Y + S(8), S(64), S(22)), Theme.SubText, Draw.Right);
                        var track = new Rectangle(ix, r.Y + S(40), r.Width - S(32), S(6));
                        Draw.Fill(g, track, S(3), Color.FromArgb(70, 78, 90));
                        Draw.Fill(g, new Rectangle(track.X, track.Y, track.Width * row.Num / 100, track.Height), S(3), Theme.Accent);
                        int kx = track.X + track.Width * row.Num / 100;
                        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - S(9), track.Y + S(3) - S(9), S(18), S(18));
                        break;
                    case Kind.Toggle:
                        Draw.Text(g, TitleOf(row), Draw.Body, new Rectangle(ix, r.Y, r.Width - S(90), SubOf(row) != null ? h - S(18) : h), tc);
                        if (SubOf(row) != null) Draw.Text(g, SubOf(row), Draw.Small, new Rectangle(ix, r.Y + h - S(26), r.Width - S(80), S(20)), Theme.SubText);
                        var t = new Rectangle(r.Right - S(16) - S(50), r.Y + (h - S(26)) / 2, S(50), S(26));
                        Draw.Fill(g, t, S(13), row.On ? Theme.Accent : Color.FromArgb(70, 78, 90));
                        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, row.On ? t.Right - S(23) : t.X + S(3), t.Y + S(3), S(20), S(20));
                        break;
                    case Kind.Choice:
                        Draw.Text(g, TitleOf(row), Draw.Body, new Rectangle(ix, r.Y, r.Width / 2, SubOf(row) != null ? h - S(18) : h), tc);
                        if (SubOf(row) != null) Draw.Text(g, SubOf(row), Draw.Small, new Rectangle(ix, r.Y + h - S(26), r.Width - S(30), S(20)), Theme.SubText);
                        string v = row.Choices[Math.Max(0, Math.Min(row.Choices.Length - 1, row.ChoiceIndex))];
                        int vw = Math.Min(S(176), r.Width / 2);
                        var vr = new Rectangle(r.Right - vw - S(12), r.Y, vw, SubOf(row) != null ? h - S(18) : h);
                        Draw.Text(g, "‹", Draw.H2, new Rectangle(vr.X, vr.Y, S(22), vr.Height), f ? Theme.Text : Theme.Dim, Draw.Center);
                        Draw.Text(g, v, Draw.BodyBold, new Rectangle(vr.X + S(22), vr.Y, vr.Width - S(44), vr.Height), Theme.Text, Draw.Center);
                        Draw.Text(g, "›", Draw.H2, new Rectangle(vr.Right - S(22), vr.Y, S(22), vr.Height), f ? Theme.Text : Theme.Dim, Draw.Center);
                        break;
                    default:
                        Draw.Text(g, TitleOf(row), Draw.Body, new Rectangle(ix, r.Y, r.Width - S(20), SubOf(row) != null ? h - S(18) : h), tc);
                        if (SubOf(row) != null) Draw.Text(g, SubOf(row), Draw.Small, new Rectangle(ix, r.Y + h - S(26), r.Width - S(30), S(20)), Theme.SubText);
                        break;
                }
                y += h + S(8);
            }
        }

        void PaintGrid(Graphics g, Rectangle area)
        {
            int gap = S(8), cw = (area.Width - gap) / 2, ch = S(74);
            int row0 = focus / 2;
            float fyTop = row0 * (ch + gap);
            if (fyTop - scroll < 0) scroll = fyTop;
            if (fyTop + ch - scroll > area.Height) scroll = fyTop + ch - area.Height;
            if (scroll < 0) scroll = 0;
            for (int i = 0; i < view.Count; i++)
            {
                var row = view[i];
                var r = new Rectangle(area.X + (i % 2) * (cw + gap), area.Y + (i / 2) * (ch + gap) - (int)scroll, cw, ch);
                row.Rect = r;
                bool f = zone == 1 && i == focus;
                Draw.Fill(g, r, S(8), f ? Theme.TileHot : Color.FromArgb(255, 23, 29, 37));
                var tc = row.Danger ? Color.FromArgb(240, 140, 140) : Theme.Text;
                // 한 줄로만 — 길면 글자를 줄여서 (어중간한 줄바꿈 없음)
                var tr = new Rectangle(r.X + S(8), r.Y + S(6), r.Width - S(16), SubOf(row) != null ? S(36) : r.Height - S(12));
                var tf = Draw.BodyBold;
                if (Draw.Measure(g, TitleOf(row), tf) > tr.Width) tf = Draw.Body;
                if (Draw.Measure(g, TitleOf(row), tf) > tr.Width) tf = Draw.Small;
                Draw.Text(g, TitleOf(row), tf, tr, tc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (SubOf(row) != null) Draw.Text(g, SubOf(row), Draw.Tiny, new Rectangle(r.X + S(8), r.Y + S(44), r.Width - S(16), S(24)), Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (f) Draw.Ring(g, r, S(8), Theme.Focus, S(2));
            }
        }

        int RowH(Row r) => r.Kind == Kind.Slider ? S(62) : SubOf(r) != null ? S(62) : S(50);
        /// <summary>미세 설명은 빼고, 성능 "지금 상태"·끝내기 확인만 보임</summary>
        string SubOf(Row r)
        {
            if (r.Keep) return r.Sub;
            // "가상 패드 (감도·연사…)" → 둘째 줄에 괄호 안 내용
            int k = r.Title.IndexOf(" (");
            return k > 0 && r.Title.EndsWith(")") ? r.Title.Substring(k + 2, r.Title.Length - k - 3) : null;
        }
        string TitleOf(Row r)
        {
            if (r.Keep) return r.Title;
            int k = r.Title.IndexOf(" (");
            return k > 0 && r.Title.EndsWith(")") ? r.Title.Substring(0, k) : r.Title;
        }

        /// <summary>보이는 순서로 다음/이전 탭</summary>
        void StepTab(int d) => SetTab(TabOrder[(TabPos(tab) + d + TabOrder.Length) % TabOrder.Length]);

        void SetTab(int t)
        {
            tab = (t + TabNames.Length) % TabNames.Length;
            focus = 0; scroll = 0; zone = 1;
            Redraw();
        }

        void Adjust(int dir)
        {
            if (focus < 0 || focus >= view.Count) return;
            var r = view[focus];
            if (r.Kind == Kind.Slider) r.Change(Math.Max(0, Math.Min(100, r.Num + dir * 5)));
            else if (r.Kind == Kind.Choice) r.Change((r.ChoiceIndex + dir + r.Choices.Length) % r.Choices.Length);
            else if (r.Kind == Kind.Toggle && (dir > 0) != r.On) r.Change(dir > 0 ? 1 : 0);
            Redraw();
        }

        void Activate(Row r)
        {
            if (r.Kind == Kind.Toggle) r.Change(r.On ? 0 : 1);
            else if (r.Kind == Kind.Choice) r.Change((r.ChoiceIndex + 1) % r.Choices.Length);
            else if (r.Kind == Kind.Action) r.Act?.Invoke();
            Redraw();
        }

        public void OnPad(Pad b)
        {
            try { Build(); Categorize(); } catch { }
            switch (b)
            {
                case Pad.LB: StepTab(-1); return;
                case Pad.RB: StepTab(1); return;
                case Pad.B: case Pad.Start: case Pad.Guide:
                    if (Popup) { Shell.ClosePopup(); return; }
                    if (GameWatch.Running && Shell.CameFromGame) Shell.ReturnToGame(); else Hide();
                    return;
            }
            if (zone == 0)
            {
                switch (b)
                {
                    case Pad.Left: if (tileFocus % 3 > 0) tileFocus--; break;
                    case Pad.Right: if (tileFocus % 3 < 2 && tileFocus + 1 < tiles.Count) tileFocus++; break;
                    case Pad.Up: if (tileFocus >= 3) tileFocus -= 3; break;
                    case Pad.Down: if (tileFocus + 3 < tiles.Count) tileFocus += 3; else if (tileFocus != tiles.Count - 1 && tileFocus / 3 < (tiles.Count - 1) / 3) tileFocus = tiles.Count - 1; else zone = 1; break;
                    case Pad.A: if (tileFocus < tiles.Count) Activate(tiles[tileFocus]); break;
                }
                Redraw();
                return;
            }
            int step = IsGrid ? 2 : 1;
            switch (b)
            {
                case Pad.Up:
                    if (focus - step < 0) { zone = 0; } else focus -= step;
                    confirmQuit = false; break;
                case Pad.Down: if (focus + step < view.Count) focus += step; confirmQuit = false; break;
                case Pad.Left: if (IsGrid) { if (focus % 2 == 1) focus--; } else Adjust(-1); break;
                case Pad.Right: if (IsGrid) { if (focus % 2 == 0 && focus + 1 < view.Count) focus++; } else Adjust(1); break;
                case Pad.A: if (focus < view.Count) Activate(view[focus]); break;
            }
            Redraw();
        }

        public void OnTap(Point p)
        {
            if (!panel.Contains(p))
            {
                if (Popup) { Shell.ClosePopup(); return; }
                if (GameWatch.Running && Shell.CameFromGame) Shell.ReturnToGame(); else Hide();
                return;
            }
            for (int i = 0; i < tabRects.Count; i++) if (tabRects[i].Contains(p)) { SetTab(TabOrder[i]); return; }
            for (int i = 0; i < tileRects.Count && i < tiles.Count; i++)
                if (tileRects[i].Contains(p)) { zone = 0; tileFocus = i; Activate(tiles[i]); return; }
            for (int i = 0; i < view.Count; i++)
            {
                var r = view[i];
                if (!r.Rect.Contains(p)) continue;
                zone = 1; focus = i;
                if (r.Kind == Kind.Slider)
                {
                    int tx = r.Rect.X + S(16), tw = r.Rect.Width - S(32);
                    r.Change(Math.Max(0, Math.Min(100, (p.X - tx) * 100 / Math.Max(1, tw))));
                }
                else if (r.Kind == Kind.Choice)
                {
                    if (p.X < r.Rect.Right - Math.Min(S(176), r.Rect.Width / 2) / 2 - S(12)) r.Change((r.ChoiceIndex - 1 + r.Choices.Length) % r.Choices.Length);
                    else r.Change((r.ChoiceIndex + 1) % r.Choices.Length);
                }
                else Activate(r);
                Redraw();
                return;
            }
        }

        /// <summary>슬라이더 끌기</summary>
        public void OnDrag(Point p)
        {
            foreach (var r in view)
            {
                if (r.Kind != Kind.Slider || p.X < r.Rect.X || p.X > r.Rect.Right || p.Y < r.Rect.Y - S(20) || p.Y > r.Rect.Bottom + S(20)) continue;
                int tx = r.Rect.X + S(16), tw = r.Rect.Width - S(32);
                int v = Math.Max(0, Math.Min(100, (p.X - tx) * 100 / Math.Max(1, tw)));
                if (v != r.Num) r.Change(v);
                Redraw();
                return;
            }
        }

        public List<Hint> Hints()
        {
            return new List<Hint>
            {
                new Hint(Pad.RB, "탭", () => StepTab(1)),
                new Hint(Pad.A, "선택", () => OnPad(Pad.A)),
                new Hint(Pad.B, Popup || (GameWatch.Running && Shell.CameFromGame) ? (GameWatch.Running ? "게임으로" : "닫기") : "닫기", () => OnPad(Pad.B)),
            };
        }
    }
}
