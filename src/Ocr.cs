using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TkoolLauncher
{
    /// <summary>윈도우 기본 글자 인식(Windows.Media.Ocr)으로 타이틀 화면의 게임 이름 읽기 — 설치된 언어(한국어·일본어·영어)마다 읽고 가장 큰 글씨 줄을 고름</summary>
    static class Ocr
    {
        const string Script = @"
[Console]::OutputEncoding=[Text.Encoding]::UTF8
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$m=([System.WindowsRuntimeSystemExtensions].GetMethods()|?{$_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'})[0]
function A($o,$t){$k=$m.MakeGenericMethod($t).Invoke($null,@($o));$k.Wait(-1)|Out-Null;$k.Result}
[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]|Out-Null
[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]|Out-Null
[Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics,ContentType=WindowsRuntime]|Out-Null
$f=A ([Windows.Storage.StorageFile]::GetFileFromPathAsync($env:TK_OCR)) ([Windows.Storage.StorageFile])
$s=A ($f.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
$d=A ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($s)) ([Windows.Graphics.Imaging.BitmapDecoder])
$b=A ($d.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
foreach($l in [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages){
 $e=[Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($l); if(-not $e){continue}
 $r=A ($e.RecognizeAsync($b)) ([Windows.Media.Ocr.OcrResult])
 foreach($n in $r.Lines){ $h=($n.Words|%{$_.BoundingRect.Height}|Measure -Maximum).Maximum; ""$($l.LanguageTag)`t$h`t$($n.Text)"" }
}";

        static readonly Regex Hangul = new Regex(@"[가-힣]"), Japanese = new Regex(@"[぀-ヿ一-鿿]");
        static readonly Regex Menu = new Regex(@"^(new ?game|continue|load|start|options?|config|settings?|exit|quit|shut ?down|end|press|ニューゲーム|コンティニュー|はじめから|つづきから|続きから|始めから|ロード|終了|オプション|設定|새로 ?시작|새 ?게임|이어 ?하기|불러오기|종료|설정|옵션|게임 ?시작|처음부터)", RegexOptions.IgnoreCase);

        /// <summary>그림에서 이름 후보 (좋은 것 먼저, 최대 4개). 실패면 빈 목록</summary>
        public static List<string> Names(string png)
        {
            var res = new List<string>();
            try
            {
                if (!File.Exists(png)) return res;
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
                psi.EnvironmentVariables["TK_OCR"] = Path.GetFullPath(png);
                string outp;
                using (var p = Process.Start(psi))
                {
                    var task = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(25000)) { try { p.Kill(); } catch { } return res; }
                    outp = task.Result;
                }
                var lines = new List<KeyValuePair<double, string>>();
                foreach (var raw in outp.Split('\n'))
                {
                    var c = raw.TrimEnd('\r').Split('\t');
                    if (c.Length < 3) continue;
                    double h; if (!double.TryParse(c[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h) && !double.TryParse(c[1], out h)) continue;
                    string lang = c[0].ToLowerInvariant(), t = Tidy(c[2]);
                    if (t.Count(char.IsLetter) < 2 || Menu.IsMatch(t)) continue;
                    // 그 언어 글자가 들어 있으면 그 언어 인식 결과를 조금 더 믿음
                    double bonus = (lang.StartsWith("ko") && Hangul.IsMatch(t)) || (lang.StartsWith("ja") && Japanese.IsMatch(t)) ? 1.1 :
                                   (lang.StartsWith("en") && !Hangul.IsMatch(t) && !Japanese.IsMatch(t)) ? 1.05 : 1.0;
                    lines.Add(new KeyValuePair<double, string>(h * bonus, t));
                }
                foreach (var kv in lines.OrderByDescending(x => x.Key))
                    if (!res.Contains(kv.Value) && res.Count < 4) res.Add(kv.Value);
                Log.Info("타이틀 글자 읽기: " + string.Join(" / ", res));
            }
            catch (Exception ex) { Log.Info("타이틀 글자 읽기 실패: " + ex.Message); }
            return res;
        }

        static string Tidy(string t)
        {
            t = (t ?? "").Trim();
            // 일본어·한자는 글자마다 띄어 나옴 → 붙임
            t = Regex.Replace(t, @"(?<=[぀-ヿ一-鿿]) (?=[぀-ヿ一-鿿])", "");
            t = Regex.Replace(t, @"^[\W_]+|[\W_]+$", "");
            return t;
        }

        // 게임마다 읽은 후보 (이름 바꾸기 메뉴에 보임)
        static readonly Dictionary<string, List<string>> found = new Dictionary<string, List<string>>();
        public static List<string> Get(GameInfo g) { lock (found) { List<string> l; return found.TryGetValue(GameData.IdOf(g), out l) ? l : new List<string>(); } }

        /// <summary>게임을 안 켜고 가진 그림(찍은 표지 → 게임 안 타이틀 그림)에서 이름 읽기</summary>
        public static string NameFromPictures(GameInfo g)
        {
            string shot = CoverArt.AutoShotPath(g);
            if (File.Exists(shot)) { var n = NameFromShot(g, shot, true); if (n != null) return n; }
            string tmp = Path.Combine(Path.GetTempPath(), "tk_ocr_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (var b = ArtExtract.FindTitleImage(g)) { if (b == null) return null; b.Save(tmp, System.Drawing.Imaging.ImageFormat.Png); }
                return NameFromShot(g, tmp, true);
            }
            catch { return null; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>찍은 그림에서 이름 읽어 두고, 지금 이름이 뜻 없음·외국어면 넣음 (백그라운드에서 부를 것). 넣은 이름 돌려줌</summary>
        public static string NameFromShot(GameInfo g, string png, bool apply)
        {
            var c = Names(png);
            if (c.Count == 0) return null;
            lock (found) found[GameData.IdOf(g)] = c;
            if (!apply) return null;
            return NameFinder.ApplyScreenName(g, c[0]) ? g.Title : null;
        }
    }
}
