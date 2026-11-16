# 쯔꾸르 런처 (tkool-launcher)

- C# WinForms, .NET Framework 4.8, 단일 exe. 외부 패키지(NuGet 런타임 의존성) 쓰지 않기 — 가볍게 유지.
- 사용자는 코드를 직접 보지 않음. main에 push하면 GitHub Actions가 빌드해 릴리스를 만든다. 결과는 릴리스 페이지 링크만 전달.
- 터치 + 게임패드만으로 조작 가능해야 함. 윈도우 기본 대화상자(파일 선택 등) 대신 TouchDialog 계열 사용.
- 로컬 확인: mono `mcs`로 컴파일 체크 (nuget 접속 불가). mcs는 foreach 튜플 분해 등 일부 최신 문법을 못 읽으니 피하기.
- 자동 업데이트: 켤 때 releases/latest 확인 → exe 교체(.old) → --updated 로 재시작. 버전은 워크플로가 -p:Version=0.1.<run_number> 로 넣음.
- 패드: XInput 직접 읽기(Gamepad.cs). 런처 화면은 IPadTarget, 게임 중엔 2000~VX Ace만 키보드로 변환(A=Z, B=X, X=Shift, Y=Esc, LB=Q, RB=W, Start=Enter).
- 화면: src/Shell (스팀OS 게임 모드 모양 — 홈 선반·라이브러리 탭·상세·왼쪽 메뉴). 표지는 CoverArt(캐시: 내 문서\쯔꾸르 런처\cache), 타이틀 그림 추출은 ArtExtract(MV·MZ 암호화, rgssad/rgss2a/rgss3a, XYZ). 기록은 GameData(games.ini).
- 미리보기: Xvfb + mono로 ShellForm.DrawToBitmap 스크린샷 가능(배터리 읽기는 mono에서 예외 → try로 감쌈).
- 게임 종류: GameKind(Tkool·Steam·Epic·Gog·Pc). 스토어는 StoreScanner, PC 폴더 게임은 GameScanner.DetectPc(폴더 하나=게임 하나), 직접 추가는 Settings.ManualGames. 실행 감시는 GameWatch(게임 폴더 안 프로세스가 다 꺼질 때까지). 검색은 SearchScreen(초성·알파벳·가나 줄).
- 기기: 사용자 기기는 AYANEO AIR Plus (AMD 7320U 멘도시노, 보드명 AB05-Mendocino). 팬은 리눅스 oxp-sensors와 같은 EC 레지스터(pwm 0~100) — 윈도우에선 EC 접근 드라이버 필요.
- 빠른 설정판: Shell/QuickPanel(메뉴 버튼·오른쪽 위 탭·위에서 쓸어내리기), 기능은 SystemControls(WMI 밝기, Core Audio, PowerShell WinRT 무선, 전원 오버레이, DEVMODE). 성능 표시 OverlayForm. AYA 버튼은 Hotkeys(저수준 키보드 훅 배우기), 패드 가이드 버튼은 XInput #100, 게임 중 보기+메뉴 = 홈.
- 팬·조명·TDP·온도: Hardware.cs (PawnIO, res/*.bin 서명 모듈 내장: LpcIO=EC 0x4E/0x4F CEii 방식, RyzenSMU=MP1 우편함 0x3B10528/578/998 명령 0x14·0x15·0x16 mW, AMDFamily17=SMN 0x59800 온도). 값 출처는 Handheld Companion. 보드명 AB05-Mendocino일 때만 동작. 관리자 권한은 작업 스케줄러 TkoolLauncherAdmin(--elevated)로 재실행.
- AYA 버튼 기본값: 큰 버튼 Ctrl+Win+F17(빠른 설정), 작은 버튼 Win+D(홈). 위 LC/RC는 Ctrl+Win+F15/F16.
- 진행 순서: 1) 폴더 검색·제목/버전 인식·목록 2) 스팀식 표지 화면·플레이 시간 3) 게임패드·전체 화면·일본어 환경 실행·RTP 확인 4) 상태 표시줄·빠른 설정판 5) 최소화 반복 감시.
