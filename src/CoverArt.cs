using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임마다 세 가지 그림을 만들어 내 문서\쯔꾸르 런처\cache 에 저장해 두고 필요할 때만 불러옴.
    ///  - 세로 표지(300x450): 목록용
    ///  - 가로 히어로(최대 960폭): 상세 화면 위쪽
    ///  - 작은 흐림용(24x36): 배경 은은한 색
    /// 직접 바꾸고 싶으면 내 문서\쯔꾸르 런처\covers\(게임 폴더 이름).png 또는 게임 폴더 안 cover.png
    /// </summary>
    class GameArt
    {
        public Bitmap Capsule;
        public Bitmap Hero;     // 없으면 null
        public Bitmap Blur;
    }

    static class CoverArt
    {
        public const int CW = 300, CH = 450;
        static readonly string CacheDir = Path.Combine(Settings.Dir, "cache");
        public static readonly string CoverDir = Path.Combine(Settings.Dir, "covers");
        const int Version = 1; // 표지 만드는 방식이 바뀌면 올려서 다시 만들게 함

        static readonly Dictionary<string, GameArt> mem = new Dictionary<string, GameArt>();
        static readonly LinkedList<string> lru = new LinkedList<string>();
        static readonly HashSet<string> pending = new HashSet<string>();
        static readonly Queue<GameInfo> queue = new Queue<GameInfo>();
        static readonly object lk = new object();
        static Thread worker;
        static Control ui;
        public static Action Changed;
        const int MaxInMemory = 30;

        public static void Init(Control uiControl)
        {
            ui = uiControl;
            worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            worker.Start();
        }

        /// <summary>메모리에 있으면 바로 주고, 없으면 만들기를 예약하고 null</summary>
        public static GameArt Get(GameInfo g)
        {
            string id = GameData.IdOf(g);
            lock (lk)
            {
                if (mem.TryGetValue(id, out GameArt a))
                {
                    lru.Remove(id);
                    lru.AddFirst(id);
                    return a;
                }
                if (pending.Add(id))
                {
                    queue.Enqueue(g);
                    Monitor.Pulse(lk);
                }
            }
            return null;
        }

        /// <summary>표지를 바꿨을 때 다시 만들기</summary>
        public static void Invalidate(GameInfo g)
        {
            string id = GameData.IdOf(g);
            lock (lk)
            {
                if (mem.TryGetValue(id, out GameArt a)) { mem.Remove(id); lru.Remove(id); Dispose(a); }
            }
            foreach (var suf in new[] { "_c.png", "_h.png", "_b.png", "_n.png" })
                try { File.Delete(Path.Combine(CacheDir, id + suf)); } catch { }
        }

        static void Dispose(GameArt a)
        {
            a.Capsule?.Dispose(); a.Hero?.Dispose(); a.Blur?.Dispose();
        }

        static void Work()
        {
            while (true)
            {
                GameInfo g;
                lock (lk)
                {
                    while (queue.Count == 0) Monitor.Wait(lk);
                    g = queue.Dequeue();
                }
                GameArt art = null;
                try { art = Build(g); } catch { }
                string id = GameData.IdOf(g);
                lock (lk)
                {
                    pending.Remove(id);
                    if (art != null)
                    {
                        mem[id] = art;
                        lru.AddFirst(id);
                        while (lru.Count > MaxInMemory)
                        {
                            string old = lru.Last.Value;
                            lru.RemoveLast();
                            if (mem.TryGetValue(old, out GameArt oa)) { mem.Remove(old); Dispose(oa); }
                        }
                    }
                }
                try { ui?.BeginInvoke(new Action(() => Changed?.Invoke())); } catch { }
            }
        }

        static string CustomCover(GameInfo g)
        {
            string name = Path.GetFileName(g.Folder);
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string p = Path.Combine(CoverDir, name + ext);
                if (File.Exists(p)) return p;
            }
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string p = Path.Combine(g.Folder, "cover" + ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static Bitmap LoadPng(string p)
        {
            try
            {
                if (!File.Exists(p)) return null;
                using (var fs = File.OpenRead(p))
                using (var img = Image.FromStream(fs))
                    return new Bitmap(img);
            }
            catch { return null; }
        }

        static GameArt Build(GameInfo g)
        {
            string id = GameData.IdOf(g);
            string custom = CustomCover(g);
            string stamp = Version + "|" + (custom ?? "") + "|" + (custom != null ? File.GetLastWriteTimeUtc(custom).Ticks.ToString() : "") + "|" + g.Title;
            string cPath = Path.Combine(CacheDir, id + "_c.png");
            string hPath = Path.Combine(CacheDir, id + "_h.png");
            string bPath = Path.Combine(CacheDir, id + "_b.png");
            string nPath = Path.Combine(CacheDir, id + "_n.png"); // '히어로 없음' 표시 + 도장

            // 저장된 게 있고 조건이 같으면 그대로
            try
            {
                if (File.Exists(cPath) && File.Exists(bPath) && File.Exists(nPath) && File.ReadAllText(nPath) == stamp)
                {
                    return new GameArt { Capsule = LoadPng(cPath), Hero = LoadPng(hPath), Blur = LoadPng(bPath) };
                }
            }
            catch { }

            Bitmap src = custom != null ? LoadPng(custom) : ArtExtract.FindTitleImage(g);
            var art = new GameArt
            {
                Capsule = MakeCapsule(g, src, custom != null),
                Hero = src != null ? MakeHero(src) : null,
            };
            art.Blur = Shrink(art.Capsule, 24, 36);
            src?.Dispose();

            try
            {
                Directory.CreateDirectory(CacheDir);
                art.Capsule.Save(cPath, ImageFormat.Png);
                art.Blur.Save(bPath, ImageFormat.Png);
                if (art.Hero != null) art.Hero.Save(hPath, ImageFormat.Png);
                else try { File.Delete(hPath); } catch { }
                File.WriteAllText(nPath, stamp);
            }
            catch { }
            return art;
        }

        static bool IsPixelArt(Bitmap b) => b.Width <= 816;

        static Bitmap MakeCapsule(GameInfo g, Bitmap src, bool isCustom)
        {
            var bmp = new Bitmap(CW, CH, PixelFormat.Format32bppArgb);
            using (var gr = Graphics.FromImage(bmp))
            {
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                gr.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                gr.PixelOffsetMode = PixelOffsetMode.Half;
                var full = new Rectangle(0, 0, CW, CH);

                if (src != null)
                {
                    // 늘이지 않고 가운데를 잘라서 꽉 채움
                    gr.InterpolationMode = IsPixelArt(src) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    DrawCover(gr, src, full);
                }
                else
                {
                    var c = Theme.VersionColor(g.Version);
                    using (var lg = new LinearGradientBrush(full, Mix(c, Color.Black, 0.35f), Mix(c, Color.FromArgb(10, 12, 18), 0.85f), 60f))
                        gr.FillRectangle(lg, full);
                    // 은은한 원 장식
                    using (var b = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
                    {
                        gr.FillEllipse(b, -80, -60, 300, 300);
                        gr.FillEllipse(b, 140, 260, 260, 260);
                    }
                }

                if (isCustom) return bmp; // 직접 넣은 표지는 글씨 안 얹음

                // 아래쪽 어둡게 + 제목
                var shade = new Rectangle(0, CH * 45 / 100, CW, CH - CH * 45 / 100);
                using (var lg = new LinearGradientBrush(new Rectangle(shade.X, shade.Y - 1, shade.Width, shade.Height + 1),
                    Color.FromArgb(0, 0, 0, 0), Color.FromArgb(225, 0, 0, 0), 90f))
                    gr.FillRectangle(lg, shade);

                bool noImg = src == null;
                using (var f = new Font(Theme.FontName, noImg ? 26f : 21f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tagFont = new Font(Theme.FontName, 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var textRect = new Rectangle(18, CH / 2, CW - 36, CH / 2 - 44);
                    var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisWord };
                    using (var sh = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                        gr.DrawString(g.Title, f, sh, new RectangleF(textRect.X + 2, textRect.Y + 2, textRect.Width, textRect.Height), sf);
                    gr.DrawString(g.Title, f, Brushes.White, textRect, sf);
                    using (var tb = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
                        gr.DrawString("RPG MAKER " + g.VersionLabel.ToUpperInvariant(), tagFont, tb, 18, CH - 34);
                }
            }
            return bmp;
        }

        static Bitmap MakeHero(Bitmap src)
        {
            int w = Math.Min(960, Math.Max(src.Width, 640));
            int h = w * src.Height / src.Width;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var gr = Graphics.FromImage(bmp))
            {
                gr.PixelOffsetMode = PixelOffsetMode.Half;
                gr.InterpolationMode = IsPixelArt(src) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                gr.DrawImage(src, 0, 0, w, h);
            }
            return bmp;
        }

        public static Bitmap Shrink(Bitmap b, int w, int h)
        {
            var s = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var gr = Graphics.FromImage(s))
            {
                gr.InterpolationMode = InterpolationMode.HighQualityBilinear;
                gr.DrawImage(b, 0, 0, w, h);
            }
            return s;
        }

        /// <summary>비율 그대로, 영역을 꽉 채우도록 가운데 잘라 그리기</summary>
        public static void DrawCover(Graphics gr, Image img, Rectangle dst, float focusY = 0.5f)
        {
            float sr = (float)img.Width / img.Height, dr = (float)dst.Width / dst.Height;
            RectangleF s;
            if (sr > dr)
            {
                float w = img.Height * dr;
                s = new RectangleF((img.Width - w) / 2f, 0, w, img.Height);
            }
            else
            {
                float h = img.Width / dr;
                s = new RectangleF(0, (img.Height - h) * focusY, img.Width, h);
            }
            gr.DrawImage(img, dst, s, GraphicsUnit.Pixel);
        }

        public static Color Mix(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }
}
