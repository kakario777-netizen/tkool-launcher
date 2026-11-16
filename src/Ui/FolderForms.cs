using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TkoolLauncher
{
    /// <summary>전체 화면 어두운 창의 공통 틀: 위 제목줄 + 가운데 목록 + 아래 버튼줄</summary>
    class TouchDialog : Form, IPadTarget
    {
        protected readonly Label Header;
        protected readonly Label SubHeader;
        protected readonly TouchGrid Grid;
        protected readonly FlowLayoutPanel Buttons;
        readonly Label hint;
        Button primary;

        /// <summary>Ⓧ 버튼으로 바로 누를 주요 버튼</summary>
        protected void SetPrimary(Button b)
        {
            primary = b;
            hint.Text = "Ⓐ 선택   Ⓑ 뒤로   Ⓧ " + b.Text;
        }
        bool onButtons;      // 패드 포커스가 아래 버튼줄에 있는지
        int buttonIndex;     // Buttons.Controls 순서 (0 = 맨 오른쪽)

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

            // 버튼줄 왼쪽 끝에 패드 안내
            hint = new Label
            {
                Text = "Ⓐ 선택   Ⓑ 뒤로   ▼ 버튼으로",
                ForeColor = Theme.SubText,
                Font = Theme.F(10f),
                AutoSize = true,
                Margin = new Padding(Theme.S(8), Theme.S(14), Theme.S(16), 0),
            };

            Buttons.Controls.Add(hint);
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
            Buttons.Controls.SetChildIndex(hint, Buttons.Controls.Count - 1);
            return b;
        }

        Button[] ButtonList()
        {
            var list = new System.Collections.Generic.List<Button>();
            foreach (Control c in Buttons.Controls)
                if (c is Button b && b.Enabled) list.Add(b);
            return list.ToArray();
        }

        void PaintFocus()
        {
            var bs = ButtonList();
            if (buttonIndex >= bs.Length) buttonIndex = bs.Length - 1;
            foreach (Control c in Buttons.Controls)
                if (c is Button b) b.FlatAppearance.BorderSize = 0;
            if (onButtons && buttonIndex >= 0 && buttonIndex < bs.Length)
            {
                bs[buttonIndex].FlatAppearance.BorderColor = Theme.Accent;
                bs[buttonIndex].FlatAppearance.BorderSize = Theme.S(3);
            }
            Grid.Invalidate();
        }

        /// <summary>Ⓑ 버튼 동작. 기본은 닫기</summary>
        protected virtual void OnBack()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected void FocusGrid()
        {
            onButtons = false;
            PaintFocus();
        }

        public void OnPad(Pad b)
        {
            var bs = ButtonList();
            if (b == Pad.B) { OnBack(); return; }
            if ((b == Pad.X || b == Pad.Start) && primary != null && primary.Enabled) { primary.PerformClick(); return; }

            if (!onButtons)
            {
                switch (b)
                {
                    case Pad.Up: Grid.MoveSelection(-1); break;
                    case Pad.Down:
                        if (Grid.Items.Count == 0 || Grid.SelectedIndex >= Grid.Items.Count - 1)
                        {
                            onButtons = bs.Length > 0;
                            buttonIndex = bs.Length - 1; // 왼쪽 끝 버튼부터
                        }
                        else Grid.MoveSelection(1);
                        break;
                    case Pad.A: Grid.ActivateSelected(); break;
                }
            }
            else
            {
                switch (b)
                {
                    case Pad.Left: buttonIndex = Math.Min(bs.Length - 1, buttonIndex + 1); break;
                    case Pad.Right: buttonIndex = Math.Max(0, buttonIndex - 1); break;
                    case Pad.Up: if (Grid.Items.Count > 0) onButtons = false; break;
                    case Pad.A:
                        if (buttonIndex >= 0 && buttonIndex < bs.Length) bs[buttonIndex].PerformClick();
                        break;
                }
            }
            PaintFocus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) OnBack();
        }
    }

    /// <summary>손가락으로 고르는 폴더 선택 창 (윈도우 기본 창은 터치로 쓰기 불편해서 직접 만듦)</summary>
    class FolderPickerForm : TouchDialog
    {
        public string SelectedPath { get; private set; }
        string current; // null = 드라이브 목록
        readonly Button upBtn, pickBtn;
        readonly bool exeMode;

        public FolderPickerForm(string start, bool pickExe = false) : base(pickExe ? "게임 실행 파일 고르기" : "게임 폴더 고르기")
        {
            exeMode = pickExe;
            AddButton("취소", () => { DialogResult = DialogResult.Cancel; Close(); });
            pickBtn = AddButton("이 폴더 선택", () =>
            {
                if (current == null) return;
                SelectedPath = current;
                DialogResult = DialogResult.OK;
                Close();
            }, Theme.Accent);
            upBtn = AddButton("▲ 위로", GoUp);
            SetPrimary(pickBtn);
            if (exeMode) pickBtn.Visible = false;

            Grid.ItemClicked += it =>
            {
                string t = (string)it.Tag;
                if (t.StartsWith("file:"))
                {
                    SelectedPath = t.Substring(5);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else Navigate(t);
            };
            Navigate(start != null && Directory.Exists(start) ? start : null);
        }

        protected override void OnBack()
        {
            if (current == null) base.OnBack();
            else GoUp();
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
                            if (g != null) item.Badges.Add((g.VersionLabel, Theme.GameColor(g)));
                        }
                        catch { }
                        items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    SubHeader.Text = path + "   ·   열 수 없음: " + ex.Message;
                }
                if (exeMode)
                {
                    SubHeader.Text = path + "   ·   게임 실행 파일(exe)을 누르세요";
                    try
                    {
                        foreach (var f in Directory.GetFiles(path, "*.exe").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                        {
                            var item = new TileItem { Title = Path.GetFileName(f), Tag = "file:" + f };
                            item.Badges.Add(("실행 파일", Theme.Accent));
                            items.Insert(0, item);
                        }
                    }
                    catch { }
                }
            }
            Grid.TileHeight = 76;
            Grid.EmptyText = "하위 폴더가 없어요";
            Grid.SelectedIndex = items.Count > 0 ? 0 : -1;
            Grid.SetItems(items);
            FocusGrid();
            if (upBtn != null) upBtn.Enabled = path != null;
            if (pickBtn != null) pickBtn.Enabled = path != null;
        }
    }

    /// <summary>등록된 게임 폴더 관리 (여러 개 가능)</summary>
    class FolderManagerForm : TouchDialog
    {
        public bool Changed { get; private set; }

        public FolderManagerForm(bool firstRun) : base(firstRun ? "게임 폴더를 골라 주세요" : "게임 폴더 설정")
        {
            Grid.ShowSelection = true;
            Grid.EmptyText = "아직 등록된 폴더가 없어요.\n[폴더 추가]를 눌러 쯔꾸르 게임이 들어있는 폴더를 골라 주세요.";
            SubHeader.Text = "여기 등록한 폴더 안의 쯔꾸르·PC 게임을 자동으로 찾아요 (폴더 하나 = 게임 하나)";

            var done = AddButton("완료", () => { DialogResult = DialogResult.OK; Close(); }, Theme.Accent);
            AddButton("선택 삭제", RemoveSelected, Theme.Danger);
            AddButton("+ 폴더 추가", AddFolder);
            SetPrimary(done);
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
