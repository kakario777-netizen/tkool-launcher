using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TkoolLauncher
{
    /// <summary>
    /// 쯔꾸르 게임이 실행 안 될 때 원인 찾기 + 고치기.
    /// - 켜기 전: 확실히 문제인 것(빠진 dll·RTP·막힌 파일·package.json 등)을 미리 보고 물어봄
    /// - 켠 뒤: 금방 꺼지거나 오류 창이 뜨거나(MV/MZ 폴더 목록 화면 포함) 하면 그 글을 읽어 원인을 찾아 물어봄
    /// </summary>
    static class GameDoctor
    {
        public class Issue
        {
            public string Title, Explain, FixLabel;
            public Func<string> Fix;     // 고치고 결과 글 돌려줌 (null이면 고칠 방법 없음, 안내만)
            public bool Sure = true;     // 켜기 전에도 물어볼 만큼 확실한지
        }

        public static List<GameInfo> Library = new List<GameInfo>();
        const string RtpPage = "https://www.rpgmakerweb.com/run-time-package";

        // ───────── 원인 찾기 ─────────
        public static List<Issue> Check(GameInfo g, string seen = "")
        {
            var list = new List<Issue>();
            if (g == null || g.Folder == null || !Directory.Exists(g.Folder)) return list;
            seen = seen ?? "";
            string low = seen.ToLowerInvariant();
            try { var vc = VcIssue(g, low); if (vc != null) list.Add(vc); } catch { }
            try { var dx = DxIssue(low); if (dx != null) list.Add(dx); } catch { }
            if (!g.IsTkool) return list;   // 쯔꾸르가 아니면 런타임(VC++·DirectX) 안내만
            try { CheckBlocked(g, list); } catch { }
            try
            {
                switch (g.Version)
                {
                    case TkoolVersion.RM2000: Check2k(g, list, low); break;
                    case TkoolVersion.XP:
                    case TkoolVersion.VX:
                    case TkoolVersion.VXAce: CheckRgss(g, list, low); break;
                    case TkoolVersion.MV:
                    case TkoolVersion.MZ: CheckMv(g, list, low); break;
                }
            }
            catch (Exception ex) { Log.Error("실행 문제 확인", ex); }
            try { CheckFont(g, list, low); } catch { }
            return list;
        }

        // ───────── Visual C++ 런타임 (msvcr120.dll 없음 같은 것) ─────────
        static readonly System.Text.RegularExpressions.Regex VcDll = new System.Text.RegularExpressions.Regex(@"(msvcr|msvcp|mfc)(71|80|90|100|110|120|140)\w*\.dll|vcruntime140(_1)?\.dll|concrt140\.dll", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static Issue VcIssue(GameInfo g, string low)
        {
            var m = VcDll.Match(low ?? "");
            if (!m.Success) return null;
            string ver = m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : "140";
            string year = ver == "71" ? "2003" : ver == "80" ? "2005" : ver == "90" ? "2008" : ver == "100" ? "2010" : ver == "110" ? "2012" : ver == "120" ? "2013" : "2015~2022";
            bool x64 = Is64(g.Exe);
            string arch = x64 ? "x64" : "x86";
            string args = ver == "100" ? "/q /norestart" : ver == "90" ? "/q" : "/install /quiet /norestart";
            Func<string, string> urlOf = a =>
            {
                switch (ver)
                {
                    case "140": return "https://aka.ms/vs/17/release/vc_redist." + a + ".exe";
                    case "120": return a == "x64" ? "https://aka.ms/highdpimfc2013x64enu" : "https://aka.ms/highdpimfc2013x86enu";
                    case "110": return "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_" + a + ".exe";
                    case "100": return "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_" + a + ".exe";
                    case "90": return "https://download.microsoft.com/download/5/D/8/5D8C65CB-C849-4025-8E95-C3966CAFD8AE/vcredist_" + a + ".exe";
                }
                return null;
            };
            string url = urlOf(arch);
            string dll = m.Value;
            const string page = "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist";
            return new Issue
            {
                Title = "Visual C++ " + year + " 런타임이 없어요 (" + dll + ")",
                Explain = "이 게임은 마이크로소프트 'Visual C++ " + year + " 재배포 패키지'(" + arch + ")가 있어야 돌아가요. 받아서 조용히 설치할게요 (1분 정도).",
                FixLabel = url != null ? "받아서 설치하기" : "받는 곳 열기",
                Fix = () =>
                {
                    if (url == null) { Open(page); return "받는 페이지를 열었어요. Visual C++ " + year + " " + arch + "를 설치해 주세요"; }
                    try
                    {
                        string dir = Path.Combine(Settings.Dir, "tools");
                        Directory.CreateDirectory(dir);
                        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                        // 게임 비트와 다른 프로그램(설정기·동봉 실행 파일)도 있어서 32비트·64비트 둘 다 설치
                        var archs = new List<string> { arch };
                        if (Environment.Is64BitOperatingSystem) archs.Add(arch == "x64" ? "x86" : "x64");
                        int okN = 0;
                        foreach (var a in archs)
                        {
                            try
                            {
                                string f = Path.Combine(dir, "vcredist_" + ver + "_" + a + ".exe");
                                if (!File.Exists(f) || new FileInfo(f).Length < 100000)
                                    using (var wc = new System.Net.WebClient()) { wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "Mozilla/5.0"; wc.DownloadFile(urlOf(a), f); }
                                var pr = Process.Start(new ProcessStartInfo(f, args) { UseShellExecute = true });
                                pr.WaitForExit(180000);
                                int code = pr.HasExited ? pr.ExitCode : -1;
                                Log.Info("VC++ " + year + " " + a + " 설치 끝 (코드 " + code + ")");
                                if (code == 0 || code == 3010 || code == 1638 || code == 5100) okN++;   // 1638 = 이미 더 새 판 있음
                            }
                            catch (Exception ex) { Log.Error("VC++ " + a + " 설치", ex); if (a == arch) throw; }
                        }
                        return "Visual C++ " + year + " (" + string.Join("·", archs) + ") 설치를 마쳤어요";
                    }
                    catch (Exception ex)
                    {
                        Log.Error("VC++ 설치", ex);
                        Open(page);
                        return "자동 설치가 안 돼서 받는 페이지를 열었어요. Visual C++ " + year + " " + arch + "를 설치해 주세요";
                    }
                },
            };
        }

        // ───────── DirectX 옛 부품 (d3dx9_43.dll·xinput1_3.dll·d3dcompiler_43.dll 등) ─────────
        public static readonly System.Text.RegularExpressions.Regex DxDll = new System.Text.RegularExpressions.Regex(@"d3dx(9|10|11)\w*\.dll|d3dcompiler_\d+\.dll|xinput1_[123]\.dll|xaudio2_\d\.dll|x3daudio1_\d\.dll|xactengine\w*\.dll|d3dx9|directx", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static Issue DxIssue(string low)
        {
            var m = DxDll.Match(low ?? "");
            if (!m.Success) return null;
            const string web = "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe";
            const string page = "https://www.microsoft.com/download/details.aspx?id=35";
            return new Issue
            {
                Title = "DirectX 옛 부품이 없어요 (" + m.Value + ")",
                Explain = "이 게임은 'DirectX 최종 사용자 런타임'(d3dx9·xinput1_3 같은 옛 파일)이 있어야 돌아가요. 윈도우 11에도 기본으로는 없어요. 받아서 조용히 설치할게요 (몇 분 걸려요).",
                FixLabel = "받아서 설치하기",
                Fix = () =>
                {
                    try
                    {
                        string dir = Path.Combine(Settings.Dir, "tools");
                        Directory.CreateDirectory(dir);
                        string f = Path.Combine(dir, "dxwebsetup.exe");
                        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                        using (var wc = new System.Net.WebClient()) { wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "Mozilla/5.0"; wc.DownloadFile(web, f); }
                        var pr = Process.Start(new ProcessStartInfo(f, "/Q") { UseShellExecute = true });
                        pr.WaitForExit(600000);
                        Log.Info("DirectX 런타임 설치 끝 (코드 " + (pr.HasExited ? pr.ExitCode.ToString() : "?") + ")");
                        return "DirectX 런타임 설치를 마쳤어요";
                    }
                    catch (Exception ex)
                    {
                        Log.Error("DirectX 설치", ex);
                        Open(page);
                        return "자동 설치가 안 돼서 받는 페이지를 열었어요. 'DirectX 최종 사용자 런타임'을 설치해 주세요";
                    }
                },
            };
        }

        /// <summary>실행 파일이 64비트인지 (PE 머리 읽기)</summary>
        static bool Is64(string exe)
        {
            try
            {
                using (var fs = File.OpenRead(exe))
                using (var br = new BinaryReader(fs))
                {
                    fs.Seek(0x3C, SeekOrigin.Begin);
                    int pe = br.ReadInt32();
                    fs.Seek(pe + 4, SeekOrigin.Begin);
                    ushort machine = br.ReadUInt16();
                    return machine == 0x8664;
                }
            }
            catch { return false; }
        }

        // 인터넷에서 받은 압축을 풀면 파일에 '다운로드한 파일' 표시가 붙어 dll을 못 읽는 경우가 있음
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetFileAttributes(string p);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool DeleteFile(string p);
        static bool Blocked(string f) => GetFileAttributes(f + ":Zone.Identifier") != 0xFFFFFFFF;

        static void CheckBlocked(GameInfo g, List<Issue> list)
        {
            bool any = Blocked(g.Exe);
            if (!any)
                foreach (var f in SafeFiles(g.Folder, "*.dll", false).Take(30))
                    if (Blocked(f)) { any = true; break; }
            if (!any) return;
            list.Add(new Issue
            {
                Title = "윈도우가 게임 파일을 막아 두었어요",
                Explain = "인터넷에서 받은 압축을 풀면 '다운로드한 파일' 표시가 붙어서, 게임이 dll을 못 읽고 꺼지는 일이 있어요. 표시를 지우면 돼요.",
                FixLabel = "막힘 풀기",
                Fix = () =>
                {
                    int n = 0;
                    foreach (var f in SafeFiles(g.Folder, "*", true).Take(50000))
                        if (Blocked(f) && DeleteFile(f + ":Zone.Identifier")) n++;
                    return "파일 " + n + "개의 막힘을 풀었어요";
                },
            });
        }

        static void Check2k(GameInfo g, List<Issue> list, string low)
        {
            // harmony.dll: 게임 폴더나 시스템 폴더에 있어야 함 (RTP 설치 때 같이 들어감)
            if (!File.Exists(Path.Combine(g.Folder, "harmony.dll")) && SysFile("harmony.dll") == null)
                list.Add(CopyIssue(g, "harmony.dll", Path.Combine(g.Folder, "harmony.dll"),
                    "2000/2003 게임은 harmony.dll이 있어야 켜져요."));
            bool rtpOk = Rtp2kInstalled();
            if (g.NeedsRtp && !rtpOk && (low == "" || low.Contains("rtp") || low.Contains("見つかりません") || low.Contains("not found") || low.Contains("찾을 수")))
                list.Add(RtpIssue("RPG 쯔꾸르 2000/2003 RTP", "이 게임은 그림·소리 같은 기본 자료(RTP)를 따로 설치해야 돌아가요."));
        }

        static void CheckRgss(GameInfo g, List<Issue> list, string low)
        {
            string ini = TextDecode.ReadFile(Path.Combine(g.Folder, "Game.ini"), out _);
            string lib = TextDecode.IniValue(ini, "Game", "Library");
            if (string.IsNullOrWhiteSpace(lib))
                lib = g.Version == TkoolVersion.XP ? "RGSS104J.dll" : g.Version == TkoolVersion.VX ? "RGSS202J.dll" : "System\\RGSS301.dll";
            lib = lib.Trim();
            string want = Path.Combine(g.Folder, lib);
            string name = Path.GetFileName(lib);
            if (!File.Exists(want) && SysFile(name) == null)
                list.Add(CopyIssue(g, name, want, "게임 엔진 파일(" + name + ")이 없어서 실행이 안 돼요."));
            if (g.NeedsRtp && !RgssRtpInstalled(g.Version, g.RtpName))
                list.Add(RtpIssue("RPG 쯔꾸르 " + g.VersionLabel + " RTP", "이 게임은 기본 자료(RTP: " + g.RtpName + ")를 따로 설치해야 돌아가요."));
        }

        static void CheckMv(GameInfo g, List<Issue> list, string low)
        {
            string dir = g.Folder;
            bool www = Directory.Exists(Path.Combine(dir, "www"));
            string main = www ? "www/index.html" : "index.html";
            if (!File.Exists(Path.Combine(dir, "package.json")))
                list.Add(new Issue
                {
                    Title = "시작 설정 파일(package.json)이 없어요",
                    Explain = "이 파일이 없으면 게임 대신 폴더 목록이나 빈 화면이 떠요. 기본 설정 파일을 만들면 돼요.",
                    FixLabel = "설정 파일 만들기",
                    Fix = () =>
                    {
                        File.WriteAllText(Path.Combine(dir, "package.json"),
                            "{\n  \"name\": \"rmmv-game\",\n  \"main\": \"" + main + "\",\n  \"js-flags\": \"--expose-gc\",\n  \"window\": { \"title\": \"\", \"toolbar\": false, \"width\": 816, \"height\": 624, \"icon\": \"" + (www ? "www/" : "") + "icon/icon.png\" }\n}\n",
                            new UTF8Encoding(false));
                        return "package.json을 만들었어요";
                    },
                });
            else
            {
                // main 이 가리키는 파일이 없으면 폴더 목록이 뜸
                string pkg = TextDecode.ReadFile(Path.Combine(dir, "package.json"), out _) ?? "";
                string m = TextDecode.JsonString(pkg, "main");
                if (!string.IsNullOrEmpty(m) && !m.Contains("://") && !File.Exists(Path.Combine(dir, m.Replace('/', '\\'))) && File.Exists(Path.Combine(dir, main.Replace('/', '\\'))))
                    list.Add(new Issue
                    {
                        Title = "시작 설정이 엉뚱한 파일을 가리켜요",
                        Explain = "package.json의 시작 파일(" + m + ")이 없어서 게임 대신 폴더 목록이 떠요. " + main + "로 바로잡으면 돼요.",
                        FixLabel = "바로잡기",
                        Fix = () =>
                        {
                            string p = Path.Combine(dir, "package.json");
                            File.Copy(p, p + ".bak", true);
                            File.WriteAllText(p, pkg.Replace("\"" + m + "\"", "\"" + main + "\""), new UTF8Encoding(false));
                            return "시작 파일을 " + main + "로 바꿨어요 (원래 파일은 package.json.bak)";
                        },
                    });
            }
            // 폴더 이름에 # % ? 가 있으면 nw.js가 주소를 잘못 읽음 → 폴더 이름은 건드리지 않고,
            // 실행할 때 그 글자가 없는 연결 경로(정션)로 켬 (SafeRunDir)
            if (!File.Exists(Path.Combine(dir, "nw.pak")) && !File.Exists(Path.Combine(dir, "resources.pak")) && Path.GetFileName(g.Exe).Equals("Game.exe", StringComparison.OrdinalIgnoreCase))
                list.Add(new Issue
                {
                    Title = "게임 실행 부품(nw.js)이 빠져 있어요",
                    Explain = "Game.exe 옆에 있어야 할 nw.pak 같은 파일이 없어요. 배포 파일이 덜 풀렸거나 빠진 거라, 게임을 다시 받아야 해요.",
                    Sure = false,
                });
        }

        static void CheckFont(GameInfo g, List<Issue> list, string low)
        {
            bool fontMsg = low.Contains("font") || low.Contains("フォント") || low.Contains("글꼴");
            bool needJp = g.Japanese && (g.Version == TkoolVersion.RM2000 || g.Version == TkoolVersion.XP);
            if (!fontMsg && !needJp) return;
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (File.Exists(Path.Combine(fonts, "msgothic.ttc")) && !fontMsg) return;
            if (File.Exists(Path.Combine(fonts, "msgothic.ttc")) && fontMsg && !needJp) return;
            list.Add(new Issue
            {
                Title = "일본어 글꼴(MS 고딕)이 없어요",
                Explain = "옛날 일본 쯔꾸르 게임은 MS 고딕 글꼴이 없으면 글자가 안 보이거나 오류로 꺼져요. 윈도우 '일본어 추가 글꼴'을 설치하면 돼요 (몇 분 걸려요).",
                FixLabel = "일본어 글꼴 설치",
                Sure = fontMsg,
                Fix = () =>
                {
                    var p = Process.Start(new ProcessStartInfo("dism.exe", "/online /add-capability /capabilityname:Language.Fonts.Jpan~~~und-JPAN~0.0.1.0 /norestart")
                    { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                    p.WaitForExit(15 * 60 * 1000);
                    return p.HasExited && (p.ExitCode == 0 || p.ExitCode == 3010) ? "일본어 글꼴을 설치했어요" : "글꼴 설치가 끝나지 않았어요 (코드 " + (p.HasExited ? p.ExitCode.ToString() : "시간 초과") + ")";
                },
            });
        }

        // ── 빠진 파일은 내 라이브러리의 다른 게임에서 같은 파일을 찾아 복사 ──
        static Issue CopyIssue(GameInfo g, string name, string dest, string why)
        {
            string src = FindInLibrary(name, g);
            if (src != null)
                return new Issue
                {
                    Title = name + " 파일이 없어요",
                    Explain = why + " 다른 게임 폴더(" + Path.GetFileName(Path.GetDirectoryName(src)) + ")에 같은 파일이 있어서 복사해 넣을 수 있어요.",
                    FixLabel = "파일 복사해 넣기",
                    Fix = () =>
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(src, dest, true);
                        return name + " 파일을 넣었어요";
                    },
                };
            return new Issue
            {
                Title = name + " 파일이 없어요",
                Explain = why + " 내 게임들 중엔 이 파일이 없어요. RTP를 설치하면 같이 들어와요.",
                FixLabel = "RTP 받는 곳 열기",
                Fix = () => { Open(RtpPage); return "RTP 받는 페이지를 열었어요. 설치 후 다시 실행해 보세요"; },
            };
        }

        static Issue RtpIssue(string name, string why) => new Issue
        {
            Title = name + "가 설치돼 있지 않아요",
            Explain = why + " 공식 페이지에서 받아 설치하면 돼요.",
            FixLabel = "RTP 받는 곳 열기",
            Fix = () => { Open(RtpPage); return "RTP 받는 페이지를 열었어요. 설치 후 다시 실행해 보세요"; },
            Sure = false,   // RTP 없이도 도는 게임이 많아서, 켜 보고 오류가 났을 때만 물어봄
        };

        static string FindInLibrary(string name, GameInfo self)
        {
            foreach (var o in Library)
            {
                if (o == self || o.Folder == null) continue;
                foreach (var c in new[] { Path.Combine(o.Folder, name), Path.Combine(o.Folder, "System", name) })
                    try { if (File.Exists(c) && new FileInfo(c).Length > 10000) return c; } catch { }
            }
            return null;
        }

        static string SysFile(string name)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var d in new[] { "SysWOW64", "System32" })
            {
                string p = Path.Combine(win, d, name);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static bool Rtp2kInstalled()
        {
            foreach (var k in new[] { @"Software\ASCII\RPG2000", @"Software\ASCII\RPG2003", @"Software\Enterbrain\RPG2003" })
                foreach (var h in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    try
                    {
                        using (var key = h.OpenSubKey(k) ?? h.OpenSubKey(k.Replace("Software\\", "Software\\WOW6432Node\\")))
                        {
                            string p = key?.GetValue("RuntimePackagePath") as string;
                            if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return true;
                        }
                    }
                    catch { }
                }
            return false;
        }

        static bool RgssRtpInstalled(TkoolVersion v, string rtpName)
        {
            string sub = v == TkoolVersion.XP ? "RGSS" : v == TkoolVersion.VX ? "RGSS2" : "RGSS3";
            foreach (var h in new[] { Registry.LocalMachine, Registry.CurrentUser })
                foreach (var path in new[] { @"SOFTWARE\WOW6432Node\Enterbrain\" + sub + @"\RTP", @"SOFTWARE\Enterbrain\" + sub + @"\RTP" })
                {
                    try
                    {
                        using (var key = h.OpenSubKey(path))
                        {
                            if (key == null) continue;
                            string p = key.GetValue(rtpName ?? "") as string;
                            if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return true;
                        }
                    }
                    catch { }
                }
            return false;
        }

        /// <summary>MV·MZ 게임 경로에 # % ? 가 있으면, 그 글자 없는 연결 폴더(정션)를 만들어 그 경로를 돌려줌. 폴더 이름 자체는 안 바꿈</summary>
        public static string SafeRunDir(GameInfo g)
        {
            try
            {
                if (g == null || !(g.Version == TkoolVersion.MV || g.Version == TkoolVersion.MZ)) return null;
                if (g.Folder.IndexOfAny(new[] { '#', '%', '?' }) < 0) return null;
                string root = Path.Combine(Settings.Dir, "run");
                Directory.CreateDirectory(root);
                string link = Path.Combine(root, "g" + ((uint)g.Folder.ToLowerInvariant().GetHashCode()).ToString("x8"));
                if (!Directory.Exists(link))
                {
                    var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + g.Folder + "\"")
                    { CreateNoWindow = true, UseShellExecute = false });
                    p.WaitForExit(5000);
                }
                if (!Directory.Exists(link)) return null;
                Log.Info("경로에 # % ? 가 있어 연결 경로로 실행: " + link);
                return link;
            }
            catch (Exception ex) { Log.Error("연결 경로 만들기", ex); return null; }
        }

        /// <summary>
        /// 예전 버전(0.1.21~)이 # % ? 를 _ 로 바꾸며 게임 폴더 위쪽(설정한 게임 폴더까지) 이름을 바꿔 버린 것 되돌리기.
        /// 설정한 폴더가 없고, 같은 자리에 글자만 _ 로 바뀐 폴더가 있으면 원래 이름으로 돌려놓음.
        /// </summary>
        public static void RestoreRenamed(IEnumerable<string> roots)
        {
            foreach (var root in roots)
            {
                try
                {
                    if (string.IsNullOrEmpty(root) || Directory.Exists(root)) continue;
                    // 위에서부터 한 칸씩: 없는 칸이면 바뀐 이름을 찾아 되돌림
                    string full = Path.GetFullPath(root).TrimEnd('\\');
                    string cur = Path.GetPathRoot(full);
                    foreach (var part in full.Substring(cur.Length).Split('\\'))
                    {
                        if (part.Length == 0) continue;
                        string want = Path.Combine(cur, part);
                        if (!Directory.Exists(want) && part.IndexOfAny(new[] { '#', '%', '?' }) >= 0)
                        {
                            string fixedName = part.Replace('#', '_').Replace('%', '_').Replace('?', '_');
                            foreach (var cand in new[] { Path.Combine(cur, fixedName), Path.Combine(cur, fixedName + "_") })
                                if (Directory.Exists(cand))
                                {
                                    try { Directory.Move(cand, want); Log.Info("바뀌었던 폴더 이름 되돌림: " + cand + " → " + want); }
                                    catch (Exception ex) { Log.Error("폴더 이름 되돌리기", ex); }
                                    break;
                                }
                        }
                        cur = want;
                    }
                }
                catch (Exception ex) { Log.Error("폴더 이름 되돌리기", ex); }
            }
        }

        static IEnumerable<string> SafeFiles(string dir, string pattern, bool deep)
        {
            try { return Directory.EnumerateFiles(dir, pattern, deep ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly); }
            catch { return new string[0]; }
        }

        static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

        // ───────── 켠 뒤 지켜보기 ─────────
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);

        static string Text(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
        static string Cls(IntPtr h) { var sb = new StringBuilder(128); GetClassName(h, sb, 128); return sb.ToString(); }

        /// <summary>게임 프로세스를 잠깐 지켜봄: 오류 창 글을 모으고, 금방 꺼지거나 폴더 목록이 뜨면 onProblem(본 글, 아직 켜져 있는지)</summary>
        public static void Watch(GameInfo g, Process p, Action<string, bool> onProblem)
        {
            if (p == null || g == null) return;
            string exeName = Path.GetFileName(g.Exe ?? "").ToLowerInvariant();
            var th = new Thread(() =>
            {
                try
                {
                    var start = DateTime.Now;
                    string seen = "";
                    bool box = false;
                    bool mv = g.Version == TkoolVersion.MV || g.Version == TkoolVersion.MZ;
                    while (true)
                    {
                        Thread.Sleep(500);
                        bool exited;
                        try { exited = p.HasExited; } catch { exited = true; }
                        double sec = (DateTime.Now - start).TotalSeconds;
                        if (exited)
                        {
                            int code = 0; try { code = p.ExitCode; } catch { }
                            if (sec < 20 && !box) { Thread.Sleep(800); string cr = Fixer.CrashInfo(g.InstallDir ?? g.Folder, start); if (cr.Length > 0) { seen += cr + "\n"; box = true; } }
                            if ((box || sec < 12) && (g.IsTkool || VcDll.IsMatch(seen) || DxDll.IsMatch(seen))) { Log.Info("게임이 금방 꺼짐 (" + (int)sec + "초, 코드 " + code + ") " + seen.Replace("\n", " / ")); onProblem(seen, false); }
                            return;
                        }
                        // 게임 창들 읽기
                        int pid = p.Id;
                        EnumWindows((h, l) =>
                        {
                            GetWindowThreadProcessId(h, out uint wp);
                            if (!IsWindowVisible(h)) return true;
                            string cls = Cls(h), title = Text(h);
                            // "Game.exe - 시스템 오류"(dll 없음)는 윈도우가 따로 띄워서 게임 프로그램 창이 아님 → 제목으로 알아봄
                            bool sysErr = wp != pid && cls == "#32770" && ((exeName.Length > 0 && title.ToLowerInvariant().StartsWith(exeName)) || Fixer.IsGameSysErr(title, g.InstallDir ?? g.Folder));
                            if (wp != pid && !sysErr) return true;
                            if (cls == "#32770")   // 메시지 상자
                            {
                                box = true;
                                var sb = new StringBuilder(title + "\n");
                                EnumChildWindows(h, (c, l2) => { string t = Text(c); if (t.Length > 2 && Cls(c) == "Static") sb.Append(t + "\n"); return true; }, IntPtr.Zero);
                                if (!seen.Contains(sb.ToString())) seen += sb.ToString();
                            }
                            else if (mv)
                            {
                                string lt = title.ToLowerInvariant();
                                if (lt.StartsWith("index of") || lt.Contains("file:///") || lt.Contains(":\\") || lt.Contains("chrome-extension://"))
                                {
                                    seen += "폴더 목록 화면: " + title + "\n";
                                    Log.Info("MV/MZ 폴더 목록 화면이 뜸: " + title);
                                    onProblem(seen, true);
                                    sec = 9999;
                                }
                            }
                            return true;
                        }, IntPtr.Zero);
                        if (sec >= 9999) return;
                        // dll 없음(Visual C++ 런타임) 창이면 바로 알려 줌 (창을 닫을 때까지 안 기다림)
                        if (box && (VcDll.IsMatch(seen) || DxDll.IsMatch(seen) || JpLocale.Unmangle(seen) != null)) { Log.Info("런타임 없음: " + seen.Replace("\n", " / ")); onProblem(seen, true); return; }
                        if (sec > 15 && !box) return;   // 잘 켜졌음
                        if (sec > 600) return;
                    }
                }
                catch (Exception ex) { Log.Error("실행 지켜보기", ex); }
            }) { IsBackground = true };
            th.Start();
        }
    }

    /// <summary>실행 문제 안내 + 고칠지 묻는 창</summary>
    class FixDialog : TouchDialog
    {
        public bool RunAfter;

        public FixDialog(GameInfo g, List<GameDoctor.Issue> issues, bool beforeRun, string seen)
            : base(beforeRun ? "실행 전에 확인할 게 있어요" : "게임이 제대로 켜지지 않았어요")
        {
            SubHeader.Text = g.Title + (beforeRun ? " — 이대로면 실행이 안 될 수 있어요. 고칠까요?" : " — 원인과 해결 방법이에요");
            Grid.TileHeight = 120;
            var items = new List<TileItem>();
            foreach (var i in issues)
                items.Add(new TileItem { Title = i.Title, Sub = i.Explain + (i.Fix != null ? "  → " + i.FixLabel : "  (직접 해결 필요)"), Tag = i });
            if (issues.Count == 0)
                items.Add(new TileItem
                {
                    Title = "원인을 찾지 못했어요",
                    Sub = (string.IsNullOrWhiteSpace(seen) ? "게임이 아무 말 없이 꺼졌어요." : "게임이 보여준 글: " + seen.Replace("\n", " ").Trim()) +
                          "  내 문서\\쯔꾸르 런처\\logs\\log.txt 를 보내 주시면 고쳐 볼게요.",
                });
            Grid.SetItems(items);

            var fixable = issues.Where(i => i.Fix != null).ToList();
            AddButton("닫기", () => { DialogResult = DialogResult.Cancel; Close(); });
            if (beforeRun) AddButton("그냥 실행", () => { RunAfter = true; DialogResult = DialogResult.OK; Close(); });
            if (fixable.Count > 0)
            {
                var b = AddButton(beforeRun ? "고치고 실행" : "고쳐 주세요", () =>
                {
                    var res = new List<string>();
                    Cursor = Cursors.WaitCursor;
                    foreach (var i in fixable)
                    {
                        try { res.Add(i.Fix()); }
                        catch (Exception ex) { Log.Error("실행 문제 고치기", ex); res.Add(i.Title + " — 고치지 못했어요: " + ex.Message); }
                    }
                    Cursor = Cursors.Default;
                    Log.Info("실행 문제 고침: " + string.Join(" / ", res));
                    Result = string.Join("\n", res);
                    RunAfter = beforeRun;
                    DialogResult = DialogResult.OK;
                    Close();
                }, Theme.Accent);
                SetPrimary(b);
            }
        }

        public string Result = "";
    }
}
