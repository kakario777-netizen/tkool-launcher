using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TkoolLauncher
{
    /// <summary>
    /// 게임 폴더에서 표지로 쓸 그림(타이틀 화면) 꺼내기.
    /// MV·MZ 암호화 그림, XP·VX·VX Ace 암호화 묶음 파일, 2000/2003 XYZ 그림까지 직접 풀어서 읽는다.
    /// </summary>
    static class ArtExtract
    {
        static readonly string[] ImgExt = { ".png", ".jpg", ".jpeg", ".bmp" };

        public static Bitmap FindTitleImage(GameInfo g)
        {
            try
            {
                switch (g.Version)
                {
                    case TkoolVersion.MV:
                    case TkoolVersion.MZ: return FromMvMz(g);
                    case TkoolVersion.RM2000: return From2k(g);
                    default: return FromRgss(g);
                }
            }
            catch { return null; }
        }

        static Bitmap Load(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch { return null; }
        }

        static Bitmap LoadFile(string path)
        {
            try { return Load(File.ReadAllBytes(path)); } catch { return null; }
        }

        // ───────── MV / MZ ─────────
        static Bitmap FromMvMz(GameInfo g)
        {
            string baseDir = Directory.Exists(Path.Combine(g.Folder, "www")) ? Path.Combine(g.Folder, "www") : g.Folder;
            string sys = TextDecode.ReadFile(Path.Combine(baseDir, "data", "System.json"), out _);
            string name = TextDecode.JsonString(sys, "title1Name");
            string key = TextDecode.JsonString(sys, "encryptionKey");
            string dir = Path.Combine(baseDir, "img", "titles1");

            Bitmap b = null;
            if (!string.IsNullOrEmpty(name)) b = MvImage(dir, name, key);
            if (b == null && Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir).OrderBy(x => x))
                {
                    b = MvImage(dir, Path.GetFileNameWithoutExtension(f), key);
                    if (b != null) break;
                }
            }
            return b;
        }

        static Bitmap MvImage(string dir, string name, string key)
        {
            string png = Path.Combine(dir, name + ".png");
            if (File.Exists(png)) return LoadFile(png);
            foreach (var ext in new[] { ".rpgmvp", ".png_" })
            {
                string enc = Path.Combine(dir, name + ext);
                if (File.Exists(enc)) return Load(MvDecrypt(File.ReadAllBytes(enc), key));
            }
            return null;
        }

        /// <summary>MV·MZ 암호화: 16바이트 머리 다음, 첫 16바이트만 키로 XOR</summary>
        static byte[] MvDecrypt(byte[] data, string keyHex)
        {
            if (data.Length < 32) return null;
            var body = new byte[data.Length - 16];
            Buffer.BlockCopy(data, 16, body, 0, body.Length);
            byte[] key = new byte[16];
            if (!string.IsNullOrEmpty(keyHex) && keyHex.Length >= 32)
            {
                for (int i = 0; i < 16; i++) key[i] = Convert.ToByte(keyHex.Substring(i * 2, 2), 16);
            }
            else
            {
                // 키를 모르면 PNG 머리 모양으로 키를 거꾸로 알아냄
                byte[] pngHead = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };
                for (int i = 0; i < 16; i++) key[i] = (byte)(body[i] ^ pngHead[i]);
            }
            for (int i = 0; i < 16; i++) body[i] ^= key[i];
            return body;
        }

        // ───────── 2000 / 2003 ─────────
        static Bitmap From2k(GameInfo g)
        {
            string dir = Path.Combine(g.Folder, "Title");
            if (!Directory.Exists(dir)) return null;
            foreach (var f in Directory.GetFiles(dir).OrderBy(x => x))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                Bitmap b = null;
                if (ext == ".xyz") b = LoadXyz(f);
                else if (ImgExt.Contains(ext)) b = LoadFile(f);
                if (b != null) return b;
            }
            return null;
        }

        /// <summary>XYZ: "XYZ1" + 가로·세로 + zlib(팔레트 768 + 픽셀 인덱스)</summary>
        static Bitmap LoadXyz(string path)
        {
            var d = File.ReadAllBytes(path);
            if (d.Length < 10 || d[0] != 'X' || d[1] != 'Y' || d[2] != 'Z' || d[3] != '1') return null;
            int w = d[4] | (d[5] << 8), h = d[6] | (d[7] << 8);
            if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return null;
            byte[] raw = new byte[768 + w * h];
            using (var ms = new MemoryStream(d, 10, d.Length - 10)) // zlib 머리 2바이트 건너뜀
            using (var z = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int n = z.Read(raw, read, raw.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var px = new int[w * h];
            for (int i = 0; i < w * h; i++)
            {
                int c = raw[768 + i];
                px[i] = (255 << 24) | (raw[c * 3] << 16) | (raw[c * 3 + 1] << 8) | raw[c * 3 + 2];
            }
            for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, bd.Scan0 + y * bd.Stride, w);
            bmp.UnlockBits(bd);
            return bmp;
        }

        // ───────── XP / VX / VX Ace ─────────
        static Bitmap FromRgss(GameInfo g)
        {
            // 1) 풀려 있는 그림 폴더
            string gfx = Path.Combine(g.Folder, "Graphics");
            foreach (var sub in new[] { "Titles1", "Titles", "System" })
            {
                string dir = Path.Combine(gfx, sub);
                if (!Directory.Exists(dir)) continue;
                var files = Directory.GetFiles(dir).Where(f => ImgExt.Contains(Path.GetExtension(f).ToLowerInvariant()));
                if (sub == "System") files = files.Where(f => Path.GetFileNameWithoutExtension(f).Equals("Title", StringComparison.OrdinalIgnoreCase));
                foreach (var f in files.OrderBy(x => x))
                {
                    var b = LoadFile(f);
                    if (b != null) return b;
                }
            }
            // 2) 암호화 묶음 파일
            foreach (var arc in new[] { "Game.rgss3a", "Game.rgss2a", "Game.rgssad" })
            {
                string p = Path.Combine(g.Folder, arc);
                if (!File.Exists(p)) continue;
                var data = RgssFindTitle(p);
                var b = Load(data);
                if (b != null) return b;
            }
            return null;
        }

        static bool IsTitleName(string n)
        {
            n = n.Replace('/', '\\').ToLowerInvariant();
            return n.StartsWith("graphics\\titles1\\") || n.StartsWith("graphics\\titles\\") ||
                   n.StartsWith("graphics\\system\\title.");
        }

        static byte[] RgssFindTitle(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                var head = br.ReadBytes(8);
                if (head.Length < 8 || Encoding.ASCII.GetString(head, 0, 6) != "RGSSAD") return null;
                return head[7] == 3 ? Rgss3(fs, br) : Rgss1(fs, br);
            }
        }

        // RGSS1·2 (XP .rgssad / VX .rgss2a)
        static byte[] Rgss1(FileStream fs, BinaryReader br)
        {
            uint key = 0xDEADCAFE;
            while (fs.Position + 8 <= fs.Length)
            {
                int nameLen = (int)(br.ReadUInt32() ^ key); key = key * 7 + 3;
                if (nameLen <= 0 || nameLen > 1024) return null;
                var nb = br.ReadBytes(nameLen);
                for (int i = 0; i < nb.Length; i++) { nb[i] ^= (byte)(key & 0xFF); key = key * 7 + 3; }
                int size = (int)(br.ReadUInt32() ^ key); key = key * 7 + 3;
                if (size < 0) return null;
                long dataPos = fs.Position;
                string name = Encoding.UTF8.GetString(nb);
                if (IsTitleName(name) && size < 20000000)
                    return XorStream(br.ReadBytes(size), key);
                fs.Position = dataPos + size;
            }
            return null;
        }

        // RGSS3 (VX Ace .rgss3a)
        static byte[] Rgss3(FileStream fs, BinaryReader br)
        {
            uint key = br.ReadUInt32() * 9 + 3;
            while (fs.Position + 16 <= fs.Length)
            {
                uint offset = br.ReadUInt32() ^ key;
                if (offset == 0) break;
                int size = (int)(br.ReadUInt32() ^ key);
                uint fileKey = br.ReadUInt32() ^ key;
                int nameLen = (int)(br.ReadUInt32() ^ key);
                if (nameLen <= 0 || nameLen > 1024) return null;
                var nb = br.ReadBytes(nameLen);
                for (int i = 0; i < nb.Length; i++) nb[i] ^= (byte)((key >> (8 * (i % 4))) & 0xFF);
                string name = Encoding.UTF8.GetString(nb);
                if (IsTitleName(name) && size > 0 && size < 20000000)
                {
                    long back = fs.Position;
                    fs.Position = offset;
                    var data = br.ReadBytes(size);
                    fs.Position = back;
                    return XorStream(data, fileKey);
                }
            }
            return null;
        }

        static byte[] XorStream(byte[] data, uint key)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] ^= (byte)((key >> (8 * (i % 4))) & 0xFF);
                if (i % 4 == 3) key = key * 7 + 3;
            }
            return data;
        }
    }
}
