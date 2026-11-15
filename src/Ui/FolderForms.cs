using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>전체 화면 어두운 창의 공통 틀: 위 제목줄 + 가운데 목록 + 아래 버튼줄</summary>
    class TouchDialog : Form
    {
        protected readonly Label Header;
        protected readonly Label SubHeader;
        protected readonly TouchGrid Grid;
        protected readonly FlowLayoutPanel Buttons;

        public TouchDialog(string title)
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            KeyPreview = true;
            Text = title;

            var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(96), BackColor = Theme.Bar, Padding = new Padding(Theme.S(24), Theme.S(12), Theme.S(24), Theme.S(8)) };
            Header = new Label { Text = title, Dock = DockStyle.Top, Height = Theme.S(44), ForeColor = Theme.Text, Font = Theme.F(17f, true), AutoEllipsis = true };
            SubHeader = new Label { Dock = DockStyle.Fill, ForeColor = Theme.SubText, Font = Theme.F(10.5f), AutoEllipsis = true };
            top.Controls.Add(SubHeader);
            top.Controls.Add(Header);

            Buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = Theme.S(84),
                BackColor = Theme.Bar,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(Theme.S(16)),
                WrapContents = false,
            };

            Grid = new TouchGrid { Dock = DockStyle.Fill, SingleColumn = true, TileHeight = 76, Gap = 10 };

            Controls.Add(Grid);
            Controls.Add(Buttons);
            Controls.Add(top);
        }

        protected Button AddButton(string text, Action onClick, Color? back = null)
        {
            var b = Theme.MakeButton(text, back);
            b.Width = Theme.S(170);
            b.Margin = new Padding(Theme.S(8), 0, 0, 0);
            b.Click += (s, e) => onClick();
            Buttons.Controls.Add(b);
            return b;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        }
    }

    /// <summary>손가락으로 고르는 폴더 선택 창 (윈도우 기본 창은 터치로 쓰기 불편해서 직접 만듦)</summary>
    class FolderPickerForm : TouchDialog
    {
        public string SelectedPath { get; private set; }
        string current; // null = 드라이브 목록
        readonly Button upBtn, pickBtn;

        public FolderPickerForm(string start) : base("게임 폴더 고르기")
        {
            AddButton("취소", () => { DialogResult = DialogResult.Cancel; Close(); });
            pickBtn = AddButton("이 폴더 선택", () =>
            {
                if (current == null) return;
                SelectedPath = current;
                DialogResult = DialogResult.OK;
                Close();
            }, Theme.Accent);
            upBtn = AddButton("▲ 위로", GoUp);

            Grid.ItemClicked += it => Navigate((string)it.Tag);
            Navigate(start != null && Directory.Exists(start) ? start : null);
        }

        void GoUp()
        {
            if (current == null) return;
            var parent = Directory.GetParent(current);
            Navigate(parent?.FullName);
        }

        void Navigate(string path)
        {
            current = path;
            var items = new List<TileItem>();
            if (path == null)
            {
                SubHeader.Text = "드라이브를 고르세요";
                foreach (var d in DriveInfo.GetDrives())
                {
                    string label = d.Name;
                    try { if (d.IsReady && !string.IsNullOrEmpty(d.VolumeLabel)) label += "  " + d.VolumeLabel; } catch { }
                    items.Add(new TileItem { Title = label, Tag = d.RootDirectory.FullName });
                }
            }
            else
            {
                SubHeader.Text = path + "   ·   들어갈 폴더를 누르고, 다 왔으면 [이 폴더 선택]";
                try
                {
                    foreach (var dir in Directory.GetDirectories(path).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                    {
                        try
                        {
                            var attr = File.GetAttributes(dir);
                            if ((attr & FileAttributes.Hidden) != 0 || (attr & FileAttributes.System) != 0) continue;
                        }
                        catch { continue; }
                        var item = new TileItem { Title = Path.GetFileName(dir), Tag = dir };
                        // 이 폴더 자체가 쯔꾸르 게임이면 표시
                        try
                        {
                            var g = GameScanner.Detect(dir);
                            if (g != null) item.Badges.Add((g.VersionLabel, Theme.VersionColor(g.Version)));
                        }
                        catch { }
                        items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    SubHeader.Text = path + "   ·   열 수 없음: " + ex.Message;
                }
            }
            Grid.TileHeight = 76;
            Grid.EmptyText = "하위 폴더가 없어요";
            Grid.SetItems(items);
            if (upBtn != null) upBtn.Enabled = path != null;
            if (pickBtn != null) pickBtn.Enabled = path != null;
        }
    }

    /// <summary>등록된 게임 폴더 관리 (여러 개 가능)</summary>
    class FolderManagerForm : TouchDialog
    {
        public bool Changed { get; private set; }

        public FolderManagerForm(bool firstRun) : base(firstRun ? "쯔꾸르 게임 폴더를 골라 주세요" : "게임 폴더 설정")
        {
            Grid.ShowSelection = true;
            Grid.EmptyText = "아직 등록된 폴더가 없어요.\n[폴더 추가]를 눌러 쯔꾸르 게임이 들어있는 폴더를 골라 주세요.";
            SubHeader.Text = "여기 등록한 폴더 안의 게임을 자동으로 찾아요 (폴더 속 폴더까지)";

            AddButton("완료", () => { DialogResult = DialogResult.OK; Close(); }, Theme.Accent);
            AddButton("선택 삭제", RemoveSelected, Theme.Danger);
            AddButton("+ 폴더 추가", AddFolder);
            Refill();
            if (firstRun) Shown += (s, e) => AddFolder();
        }

        void Refill()
        {
            var items = Settings.GameFolders.Select(f => new TileItem
            {
                Title = Path.GetFileName(f.TrimEnd('\\')) is string n && n.Length > 0 ? n : f,
                Sub = f,
                Tag = f
            }).ToList();
            Grid.TileHeight = 96;
            Grid.SetItems(items);
        }

        void AddFolder()
        {
            string start = Settings.GameFolders.LastOrDefault();
            if (start != null) start = Directory.GetParent(start.TrimEnd('\\'))?.FullName;
            using (var p = new FolderPickerForm(start))
            {
                if (p.ShowDialog(this) != DialogResult.OK) return;
                if (!Settings.GameFolders.Contains(p.SelectedPath, StringComparer.OrdinalIgnoreCase))
                {
                    Settings.GameFolders.Add(p.SelectedPath);
                    Settings.Save();
                    Changed = true;
                    Refill();
                }
            }
        }

        void RemoveSelected()
        {
            int i = Grid.SelectedIndex;
            if (i < 0 || i >= Grid.Items.Count) return;
            Settings.GameFolders.Remove((string)Grid.Items[i].Tag);
            Settings.Save();
            Changed = true;
            Grid.SelectedIndex = -1;
            Refill();
        }
    }
}
