using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TkoolLauncher
{
    class MainForm : Form
    {
        readonly TouchGrid grid;
        readonly Label countLabel;
        List<GameInfo> games = new List<GameInfo>();
        bool scanning;

        public MainForm()
        {
            Theme.Init(this);
            Text = "쯔꾸르 런처";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Theme.Bg;
            KeyPreview = true;

            var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(84), BackColor = Theme.Bar, Padding = new Padding(Theme.S(24), Theme.S(14), Theme.S(16), Theme.S(14)) };
            var title = new Label { Text = "쯔꾸르 런처", Dock = DockStyle.Left, AutoSize = false, Width = Theme.S(220), ForeColor = Theme.Text, Font = Theme.F(18f, true), TextAlign = ContentAlignment.MiddleLeft };
            countLabel = new Label { Dock = DockStyle.Fill, ForeColor = Theme.SubText, Font = Theme.F(11f), TextAlign = ContentAlignment.MiddleLeft };

            var btns = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, BackColor = Theme.Bar };
            btns.Controls.Add(Btn("종료", Close, Theme.Danger));
            btns.Controls.Add(Btn("폴더 설정", OpenFolders));
            btns.Controls.Add(Btn("새로고침", Rescan));

            top.Controls.Add(countLabel);
            top.Controls.Add(btns);
            top.Controls.Add(title);

            grid = new TouchGrid { Dock = DockStyle.Fill, MinTileWidth = 300, TileHeight = 150, Gap = 16, ShowSelection = true };
            grid.ItemClicked += it => Launch((GameInfo)it.Tag);

            Controls.Add(grid);
            Controls.Add(top);

            Shown += (s, e) =>
            {
                if (Settings.GameFolders.Count == 0) OpenFolders(true);
                else Rescan();
            };
        }

        Button Btn(string text, Action a, Color? back = null)
        {
            var b = Theme.MakeButton(text, back);
            b.Margin = new Padding(Theme.S(8), 0, 0, 0);
            b.Click += (s, e) => a();
            return b;
        }

        void OpenFolders() => OpenFolders(false);

        void OpenFolders(bool firstRun)
        {
            using (var f = new FolderManagerForm(firstRun))
            {
                f.ShowDialog(this);
                if (f.Changed || firstRun) Rescan();
            }
        }

        void Rescan()
        {
            if (scanning) return;
            scanning = true;
            countLabel.Text = "게임 찾는 중...";
            grid.EmptyText = "";
            grid.SetItems(new List<TileItem>());
            var roots = Settings.GameFolders.ToList();
            Task.Run(() =>
            {
                List<GameInfo> found;
                try { found = GameScanner.ScanAll(roots); } catch { found = new List<GameInfo>(); }
                BeginInvoke(new Action(() => ShowGames(found)));
            });
        }

        void ShowGames(List<GameInfo> found)
        {
            scanning = false;
            games = found;
            var items = games.Select(g =>
            {
                var it = new TileItem { Title = g.Title, Sub = g.Folder, Tag = g };
                it.Badges.Add((g.VersionLabel, Theme.VersionColor(g.Version)));
                if (g.Japanese) it.Badges.Add(("日本語", Color.FromArgb(200, 70, 70)));
                if (g.NeedsRtp) it.Badges.Add(("RTP 필요", Color.FromArgb(90, 98, 112)));
                return it;
            }).ToList();

            grid.EmptyText = Settings.GameFolders.Count == 0
                ? "[폴더 설정]에서 쯔꾸르 게임 폴더를 골라 주세요"
                : "등록한 폴더에서 쯔꾸르 게임을 못 찾았어요.\n게임 폴더를 넣고 [새로고침]을 눌러 주세요.";
            grid.SelectedIndex = items.Count > 0 ? 0 : -1;
            grid.SetItems(items);

            var byVer = games.GroupBy(g => g.VersionLabel).Select(x => x.Key + " " + x.Count());
            countLabel.Text = games.Count == 0 ? "게임 0개" : $"게임 {games.Count}개   ·   " + string.Join("  ", byVer);
        }

        void Launch(GameInfo g)
        {
            try
            {
                var psi = new ProcessStartInfo(g.Exe) { WorkingDirectory = g.Folder, UseShellExecute = true };
                var p = Process.Start(psi);
                WindowState = FormWindowState.Minimized; // 게임하는 동안 런처는 쉬기
                if (p != null)
                {
                    p.EnableRaisingEvents = true;
                    p.Exited += (s, e) => BeginInvoke(new Action(() =>
                    {
                        WindowState = FormWindowState.Maximized;
                        Activate();
                    }));
                }
            }
            catch (Exception ex)
            {
                WindowState = FormWindowState.Maximized;
                MessageBox.Show(this, "게임을 실행하지 못했어요.\n\n" + g.Exe + "\n\n" + ex.Message, "쯔꾸르 런처");
            }
        }

        // 방향키·엔터로도 고를 수 있게 (게임패드 연결은 3단계)
        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (grid.Items.Count > 0)
            {
                int i = Math.Max(0, grid.SelectedIndex);
                int cols = Math.Max(1, (grid.Width - Theme.S(16)) / (Theme.S(300) + Theme.S(16)));
                int n = i;
                switch (key)
                {
                    case Keys.Left: n = i - 1; break;
                    case Keys.Right: n = i + 1; break;
                    case Keys.Up: n = i - cols; break;
                    case Keys.Down: n = i + cols; break;
                    case Keys.Enter:
                        Launch((GameInfo)grid.Items[i].Tag);
                        return true;
                    default: return base.ProcessCmdKey(ref msg, key);
                }
                n = Math.Max(0, Math.Min(grid.Items.Count - 1, n));
                grid.SelectedIndex = n;
                grid.EnsureVisible(n);
                return true;
            }
            return base.ProcessCmdKey(ref msg, key);
        }
    }
}
