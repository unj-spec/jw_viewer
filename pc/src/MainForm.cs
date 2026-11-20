// 소설 뷰어 PC 버전 창: 위 메뉴, 왼쪽 소설 목록(최근·PC·깃허브), 본문, 아래 페이지 이동 바.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NovelViewer
{
    class Theme
    {
        public string Id, Label;
        public Color Bg, Fg, Muted, Panel, Line, Accent;
        public Theme(string id, string label, string bg, string fg, string muted, string panel, string line, string accent)
        {
            Id = id; Label = label;
            Bg = ColorTranslator.FromHtml(bg); Fg = ColorTranslator.FromHtml(fg); Muted = ColorTranslator.FromHtml(muted);
            Panel = ColorTranslator.FromHtml(panel); Line = ColorTranslator.FromHtml(line); Accent = ColorTranslator.FromHtml(accent);
        }
    }

    // 목록 한 줄
    class NavItem
    {
        public string Icon, Title, Sub, Id;
        public bool Active, IsMessage;
        public Action Open;
        public Action Remove;      // 오른쪽 클릭 "목록에서 빼기"
        public string RemoveLabel;
    }

    public class MainForm : Form
    {
        static readonly Theme[] Themes = {
            new Theme("light", "밝게", "#fbfaf7", "#222222", "#888888", "#f0ede6", "#ddd8cc", "#3b6fd4"),
            new Theme("sepia", "종이", "#f4ecd8", "#4a3b28", "#8b7a60", "#e9dfc6", "#d6c9a8", "#a0673a"),
            new Theme("green", "연두", "#e4edd3", "#2d3a24", "#6f7f60", "#d6e2c1", "#bfcfa6", "#4f7d3a"),
            new Theme("sky", "하늘", "#e5edf5", "#1f2c3a", "#6a7b8c", "#d6e2ee", "#bccbdb", "#3a6ea8"),
            new Theme("pink", "분홍", "#f8eaed", "#48303a", "#93737d", "#eedbe0", "#dfc3cb", "#b4546e"),
            new Theme("gray", "회색", "#dedede", "#262626", "#707070", "#d0d0d0", "#bcbcbc", "#4d4d4d"),
            new Theme("dusk", "저녁", "#2b2520", "#dccdb8", "#8f8170", "#362f28", "#4a4036", "#d39b5c"),
            new Theme("dark", "어둡게", "#1b1c1f", "#d6d6d6", "#777777", "#26272b", "#3a3b40", "#7aa2ff"),
            new Theme("navy", "남색", "#142036", "#cbd5e6", "#7a89a3", "#1c2a44", "#2c3c5a", "#7fa8ff"),
            new Theme("black", "검정", "#000000", "#b9b9b9", "#6b6b6b", "#121212", "#2a2a2a", "#8fa8d8"),
        };
        static readonly int[] SideMargins = { 16, 32, 48, 80, 120, 200, 320 };
        static readonly int[] TopMargins = { 8, 16, 32, 48, 80 };
        static readonly double[] LineHeights = { 1.5, 1.7, 1.9, 2.2, 2.5 };
        static readonly CultureInfo Ko = new CultureInfo("ko-KR");

        readonly Data data = Data.Load();
        Settings St { get { return data.Settings; } }
        readonly float sc;

        // 화면 부품
        readonly Panel header = new Panel(), nav = new Panel(), bottom = new Panel(), tabs = new Panel(), ghForm = new Panel();
        readonly Button btnList = new Button(), btnRefresh = new Button(), btnMarks = new Button(), btnSettings = new Button();
        readonly Button tabRecent = new Button(), tabPc = new Button(), tabGh = new Button();
        readonly Button btnPrevCh = new Button(), btnNextCh = new Button();
        readonly List<Panel> covers = new List<Panel>();
        readonly Label title = new Label(), status = new Label(), progress = new Label(), where = new Label(), seekVal = new Label(), toast = new Label();
        readonly ListBox list = new ListBox();
        readonly ReaderView reader = new ReaderView();
        readonly SeekBar seek = new SeekBar();
        readonly TextBox ghRepoIn = new TextBox(), ghTokenIn = new TextBox();
        readonly Label ghInfo = new Label();
        readonly Button ghSave = new Button(), ghClear = new Button();
        readonly LinkLabel ghLink = new LinkLabel();
        readonly ContextMenuStrip settingsMenu = new ContextMenuStrip(), marksMenu = new ContextMenuStrip(), itemMenu = new ContextMenuStrip();
        readonly Timer poll = new Timer(), saveTimer = new Timer(), toastTimer = new Timer();

        string current, currentMtime;
        List<string> chapters = new List<string>();
        bool menuHidden, busy;
        DateTime ghChecked = DateTime.MinValue;
        FormWindowState beforeFull;

        public MainForm()
        {
            using (var g = CreateGraphics()) sc = g.DpiX / 96f;
            Text = "소설 뷰어";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Font = new Font("맑은 고딕", 10f);
            KeyPreview = true;
            MinimumSize = new Size(S(480), S(400));
            StartPosition = FormStartPosition.Manual;
            var area = Screen.PrimaryScreen.WorkingArea;
            Rectangle r = data.WinW > 0 ? new Rectangle(data.WinX, data.WinY, data.WinW, data.WinH)
                : new Rectangle(area.X + (area.Width - Math.Min(S(1200), area.Width)) / 2, area.Y + (area.Height - Math.Min(S(920), area.Height)) / 2,
                    Math.Min(S(1200), area.Width), Math.Min(S(920), area.Height));
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r))) r = new Rectangle(area.X + 40, area.Y + 40, S(1120), S(880));
            Bounds = r;
            if (data.WinMax) WindowState = FormWindowState.Maximized;

            BuildUi();
            current = data.Current;
            ApplySettings();
            reader.Placeholder = "☰ 를 눌러 소설을 여세요.\nPC 폴더의 txt 파일이나 GitHub 저장소의 소설을 열 수 있습니다.";

            poll.Interval = 2000;
            poll.Tick += async (s, e) => await Poll();
            poll.Start();
            saveTimer.Interval = 400;
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SavePos(); };
            toastTimer.Interval = 2200;
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); toast.Visible = false; };
        }

        int S(int px) { return (int)Math.Round(px * sc); }

        // ---- 화면 만들기 ----
        void BuildUi()
        {
            // 위 메뉴
            header.Dock = DockStyle.Top;
            header.Height = S(56);
            header.Padding = new Padding(S(8), S(7), S(8), S(7));
            SetupIconButton(btnList, "☰", "소설 목록 (L)");
            SetupIconButton(btnSettings, "가", "보기 설정");
            SetupIconButton(btnMarks, "🔖", "책갈피 (B)");
            SetupIconButton(btnRefresh, "⟳", "새로고침 (R)");
            btnList.Dock = DockStyle.Left;
            foreach (var b in new[] { btnSettings, btnMarks, btnRefresh }) b.Dock = DockStyle.Right;
            var gap1 = new Panel { Dock = DockStyle.Right, Width = S(6) };
            var gap2 = new Panel { Dock = DockStyle.Right, Width = S(6) };
            progress.Dock = DockStyle.Right;
            progress.AutoSize = false;
            progress.Width = S(100);
            progress.TextAlign = ContentAlignment.MiddleRight;
            progress.Padding = new Padding(0, 0, S(10), 0);
            var head = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10), 0, 0, 0) };
            title.Dock = DockStyle.Top; title.Height = S(24); title.AutoEllipsis = true;
            title.Font = new Font("맑은 고딕", 11f, FontStyle.Bold);
            status.Dock = DockStyle.Top; status.Height = S(18); status.AutoEllipsis = true;
            status.Font = new Font("맑은 고딕", 8.5f);
            head.Controls.Add(status); head.Controls.Add(title);
            header.Controls.Add(head);
            header.Controls.Add(progress);
            header.Controls.Add(btnRefresh); header.Controls.Add(gap1);
            header.Controls.Add(btnMarks); header.Controls.Add(gap2);
            header.Controls.Add(btnSettings);
            header.Controls.Add(btnList);
            btnList.Click += (s, e) => { St.Nav = menuHidden || !St.Nav; menuHidden = false; ApplySettings(); if (St.Nav) SetTab(data.NavTab); };
            btnSettings.Click += (s, e) => { BuildSettingsMenu(); settingsMenu.Show(btnSettings, new Point(btnSettings.Width - settingsMenu.Width, btnSettings.Height)); };
            btnMarks.Click += (s, e) => { BuildMarksMenu(); marksMenu.Show(btnMarks, new Point(btnMarks.Width - marksMenu.Width, btnMarks.Height)); };
            btnRefresh.Click += async (s, e) => await Refresh(true);

            // 왼쪽 소설 목록
            nav.Dock = DockStyle.Left;
            nav.Width = S(320);
            tabs.Dock = DockStyle.Top;
            tabs.Height = S(52);
            tabs.Padding = new Padding(S(8));
            SetupTab(tabRecent, "최근"); SetupTab(tabPc, "📁 PC"); SetupTab(tabGh, "☁ 깃허브");
            tabs.Resize += (s, e) =>
            {
                int w = (tabs.ClientSize.Width - S(16) - S(12)) / 3;
                tabRecent.SetBounds(S(8), S(8), w, S(36));
                tabPc.SetBounds(S(8) + w + S(6), S(8), w, S(36));
                tabGh.SetBounds(S(8) + (w + S(6)) * 2, S(8), w, S(36));
            };
            tabs.Controls.Add(tabRecent); tabs.Controls.Add(tabPc); tabs.Controls.Add(tabGh);
            tabRecent.Click += (s, e) => SetTab("recent");
            tabPc.Click += (s, e) => SetTab("pc");
            tabGh.Click += (s, e) => SetTab("gh");
            where.Dock = DockStyle.Top;
            where.AutoSize = false;
            where.Height = S(34);
            where.Padding = new Padding(S(12), S(8), S(12), 0);
            where.AutoEllipsis = true;
            where.Font = new Font("맑은 고딕", 8.5f);
            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = S(48);
            list.IntegralHeight = false;
            list.DrawItem += DrawNavItem;
            list.MouseClick += (s, e) =>
            {
                int i = list.IndexFromPoint(e.Location);
                if (i < 0 || i >= list.Items.Count) return;
                var it = (NavItem)list.Items[i];
                if (it.Open != null) it.Open();
            };
            list.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                int i = list.IndexFromPoint(e.Location);
                if (i < 0 || i >= list.Items.Count) return;
                var it = (NavItem)list.Items[i];
                if (it.Remove == null) return;
                itemMenu.Items.Clear();
                itemMenu.Items.Add(it.RemoveLabel, null, (s2, e2) => it.Remove());
                itemMenu.Show(list, e.Location);
            };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter || list.SelectedIndex < 0) return;
                var it = (NavItem)list.SelectedItem;
                if (it.Open != null) it.Open();
                e.Handled = true;
            };
            BuildGhForm();
            nav.Controls.Add(list);
            nav.Controls.Add(ghForm);
            nav.Controls.Add(where);
            nav.Controls.Add(tabs);
            var navLine = new Panel { Dock = DockStyle.Right, Width = 1 };
            nav.Controls.Add(navLine);
            nav.Tag = navLine;

            // 아래 페이지 이동 바
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = S(44);
            bottom.Padding = new Padding(S(16), S(6), S(16), S(6));
            seek.Dock = DockStyle.Fill;
            seekVal.Dock = DockStyle.Right;
            seekVal.AutoSize = false;
            seekVal.Width = S(110);
            seekVal.TextAlign = ContentAlignment.MiddleRight;
            // [◀ 이전 장] [이동 바] [쪽수] [다음 장 ▶]
            foreach (var b in new[] { btnPrevCh, btnNextCh })
            {
                b.FlatStyle = FlatStyle.Flat;
                b.TabStop = false;
                b.Width = S(96);
            }
            btnPrevCh.Text = "◀ 이전 장"; btnPrevCh.Dock = DockStyle.Left;
            btnNextCh.Text = "다음 장 ▶"; btnNextCh.Dock = DockStyle.Right;
            btnPrevCh.Click += (s, e) => GoChapter(-1, false);
            btnNextCh.Click += (s, e) => GoChapter(1, false);
            var gapL = new Panel { Dock = DockStyle.Left, Width = S(12) };
            var gapR = new Panel { Dock = DockStyle.Right, Width = S(12) };
            bottom.Controls.Add(seek);
            bottom.Controls.Add(gapL);
            bottom.Controls.Add(gapR);
            bottom.Controls.Add(seekVal);
            bottom.Controls.Add(btnPrevCh);
            bottom.Controls.Add(btnNextCh);
            seek.Scrub += v => { if (reader.PageMode) reader.GoPage(v); else reader.SetScrollPermille(v); };

            // 본문
            reader.Dock = DockStyle.Fill;
            reader.UiScale = sc;
            reader.Changed += () => { UpdateProgress(); saveTimer.Stop(); saveTimer.Start(); };
            reader.Edge += dir => AskChapter(dir);
            reader.Center += () => { if (current == null) { St.Nav = true; ApplySettings(); SetTab(data.NavTab); } else SetMenuHidden(!menuHidden); };
            toast.AutoSize = true;
            toast.Visible = false;
            toast.Padding = new Padding(S(14), S(7), S(14), S(7));
            toast.ForeColor = Color.White;
            reader.Controls.Add(toast);

            // 붙이는 순서: 나중에 붙인 것이 먼저 자리를 잡는다.
            // 위 메뉴가 맨 위 전체, 그 아래 왼쪽 목록, 아래 이동 바, 나머지가 본문.
            var headLine = new Panel { Dock = DockStyle.Top, Height = 1 };
            Controls.Add(reader);
            Controls.Add(nav);
            Controls.Add(bottom);
            Controls.Add(headLine);
            Controls.Add(header);
            header.Tag = headLine;

            // 메뉴를 숨길 때 위·왼쪽·아래 메뉴 자리를 덮는 판. 누르면 메뉴가 다시 보인다.
            foreach (var p in new[] { header, nav, bottom })
            {
                var c = new Panel { Bounds = new Rectangle(Point.Empty, p.ClientSize), Visible = false,
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
                c.Click += (s, e) => SetMenuHidden(false);
                p.Controls.Add(c);
                c.BringToFront();
                covers.Add(c);
            }
        }

        void SetupIconButton(Button b, string text, string tip)
        {
            b.Text = text;
            b.Width = S(42);
            b.FlatStyle = FlatStyle.Flat;
            b.Font = new Font(text == "🔖" ? "Segoe UI Emoji" : "맑은 고딕", 13f);
            b.TabStop = false;
            new ToolTip().SetToolTip(b, tip);
        }

        void SetupTab(Button b, string text)
        {
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.TabStop = false;
        }

        void BuildGhForm()
        {
            ghForm.Dock = DockStyle.Fill;
            ghForm.Visible = false;
            ghForm.Padding = new Padding(S(14));
            ghForm.AutoScroll = true;
            int y = S(12), w = S(286);
            ghInfo.Text = "비공개 저장소의 소설을 읽으려면 GitHub 토큰이 필요합니다.\n" +
                "GitHub → Settings → Developer settings → Fine-grained tokens 에서 만들고, 이 저장소만 고른 뒤 Contents: Read-only 권한만 주세요.";
            ghInfo.SetBounds(S(14), y, w, S(96));
            ghLink.Text = "토큰 만들기 페이지 열기";
            ghLink.SetBounds(S(14), y + S(98), w, S(22));
            ghLink.LinkClicked += (s, e) => { try { Process.Start("https://github.com/settings/personal-access-tokens/new"); } catch { } };
            var l1 = new Label { Text = "저장소 주소", Font = new Font("맑은 고딕", 10f, FontStyle.Bold) };
            l1.SetBounds(S(14), y + S(130), w, S(22));
            ghRepoIn.SetBounds(S(14), y + S(154), w, S(28));
            var l2 = new Label { Text = "토큰", Font = new Font("맑은 고딕", 10f, FontStyle.Bold) };
            l2.SetBounds(S(14), y + S(192), w, S(22));
            ghTokenIn.SetBounds(S(14), y + S(216), w, S(28));
            ghTokenIn.UseSystemPasswordChar = true;
            ghSave.Text = "저장하고 열기";
            ghSave.FlatStyle = FlatStyle.Flat;
            ghSave.SetBounds(S(14), y + S(258), w, S(38));
            ghClear.Text = "GitHub 연결 지우기";
            ghClear.FlatStyle = FlatStyle.Flat;
            ghClear.SetBounds(S(14), y + S(304), w, S(34));
            ghForm.Controls.AddRange(new Control[] { ghInfo, ghLink, l1, ghRepoIn, l2, ghTokenIn, ghSave, ghClear });
            ghSave.Click += (s, e) =>
            {
                string r = ghRepoIn.Text.Trim(), t = ghTokenIn.Text.Trim();
                if (r.Length == 0 || (data.Token.Length == 0 && t.Length == 0)) { Toast("저장소와 토큰을 넣어 주세요"); return; }
                data.SetGithub(r, t);
                data.GhDir = "";
                data.Save();
                ghTokenIn.Text = "";
                Browse("gh", "");
            };
            ghClear.Click += (s, e) =>
            {
                data.GhRepo = ""; data.GhRef = ""; data.GhRoot = ""; data.Token = "";
                data.Save();
                Toast("GitHub 연결을 지웠습니다");
                ShowGhForm();
            };
            ghRepoIn.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { ghSave.PerformClick(); e.SuppressKeyPress = true; } };
            ghTokenIn.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { ghSave.PerformClick(); e.SuppressKeyPress = true; } };
        }

        Theme Th { get { return Themes.FirstOrDefault(t => t.Id == St.Theme) ?? Themes[0]; } }

        // ---- 설정 적용 ----
        void ApplySettings()
        {
            var t = Th;
            BackColor = t.Bg;
            foreach (var p in new Control[] { header, nav, bottom, tabs, ghForm, list, where })
            {
                p.BackColor = t.Panel;
                p.ForeColor = t.Fg;
            }
            ((Control)header.Tag).BackColor = t.Line;
            ((Control)nav.Tag).BackColor = t.Line;
            status.ForeColor = t.Muted; where.ForeColor = t.Muted; progress.ForeColor = t.Muted; seekVal.ForeColor = t.Muted;
            ghInfo.ForeColor = t.Muted;
            ghLink.LinkColor = t.Accent; ghLink.ActiveLinkColor = t.Accent;
            foreach (var b in new[] { btnList, btnRefresh, btnMarks, btnSettings, tabRecent, tabPc, tabGh, ghClear })
            {
                b.BackColor = t.Bg; b.ForeColor = t.Fg;
                b.FlatAppearance.BorderColor = t.Line;
                b.FlatAppearance.MouseOverBackColor = t.Panel;
            }
            ghSave.BackColor = t.Accent; ghSave.ForeColor = Color.White; ghSave.FlatAppearance.BorderColor = t.Accent;
            foreach (var tb in new[] { ghRepoIn, ghTokenIn }) { tb.BackColor = t.Bg; tb.ForeColor = t.Fg; }
            foreach (var b in new[] { tabRecent, tabPc, tabGh })
            {
                bool on = (string)b.Tag == "on";
                if (on) { b.BackColor = t.Accent; b.ForeColor = Color.White; b.FlatAppearance.BorderColor = t.Accent; b.FlatAppearance.MouseOverBackColor = t.Accent; }
            }
            reader.BackColor = t.Bg; reader.Fg = t.Fg; reader.Muted = t.Muted;
            seek.BackColor = t.Panel; seek.Track = t.Line; seek.Accent = t.Accent;
            toast.BackColor = t.Accent;
            reader.PageMode = St.Mode == "page";
            reader.LineHeightMul = (float)St.LineHeight;
            reader.SideMargin = St.SideMargin;
            reader.TopMargin = St.TopMargin;
            if (reader.TextFont == null || Math.Abs(reader.TextFont.Size - St.FontSize * sc) > 0.1)
                reader.TextFont = new Font("맑은 고딕", St.FontSize * sc, GraphicsUnit.Pixel);
            // 메뉴를 숨겨도 자리는 그대로 두고 바탕색으로 덮기만 한다.
            // 본문 칸 크기가 바뀌지 않으니 글이 위아래로 움직이지 않는다.
            nav.Visible = St.Nav;
            bottom.Visible = current != null;
            ((Control)header.Tag).BackColor = menuHidden ? t.Bg : t.Line;
            foreach (var c in covers) { c.BackColor = t.Bg; c.Visible = menuHidden; if (menuHidden) c.BringToFront(); }
            foreach (var b in new[] { btnPrevCh, btnNextCh })
            {
                b.BackColor = t.Bg; b.ForeColor = t.Fg;
                b.FlatAppearance.BorderColor = t.Line;
                b.FlatAppearance.MouseOverBackColor = t.Panel;
            }
            list.Invalidate();
            reader.Relayout();
            UpdateProgress();
            data.Save();
        }

        void ChangeSetting(Action a) { a(); ApplySettings(); }

        // 가운데를 누르면 메뉴를 숨기고 글만 보이게(다시 누르면 보임)
        void SetMenuHidden(bool hide)
        {
            menuHidden = hide && current != null;
            ApplySettings();
            if (menuHidden) Toast("가운데를 누르면 메뉴가 다시 보입니다");
        }

        void BuildSettingsMenu()
        {
            var m = settingsMenu;
            m.Items.Clear();
            var theme = new ToolStripMenuItem("테마");
            foreach (var t in Themes)
            {
                var th = t;
                var it = new ToolStripMenuItem(t.Label, null, (s, e) => ChangeSetting(() => St.Theme = th.Id)) { Checked = St.Theme == t.Id };
                theme.DropDownItems.Add(it);
            }
            m.Items.Add(theme);
            var mode = new ToolStripMenuItem("넘기기 방식");
            mode.DropDownItems.Add(new ToolStripMenuItem("페이지 (클릭·휠로 한 장씩)", null, (s, e) => ChangeSetting(() => St.Mode = "page")) { Checked = St.Mode == "page" });
            mode.DropDownItems.Add(new ToolStripMenuItem("스크롤 (위아래로 이어서)", null, (s, e) => ChangeSetting(() => St.Mode = "scroll")) { Checked = St.Mode == "scroll" });
            m.Items.Add(mode);
            var size = new ToolStripMenuItem("글자 크기: " + St.FontSize);
            size.DropDownItems.Add("크게 (+)", null, (s, e) => ChangeSetting(() => St.FontSize = Math.Min(40, St.FontSize + 1)));
            size.DropDownItems.Add("작게 (−)", null, (s, e) => ChangeSetting(() => St.FontSize = Math.Max(12, St.FontSize - 1)));
            m.Items.Add(size);
            var lh = new ToolStripMenuItem("줄간격");
            foreach (var v in LineHeights)
            {
                var vv = v;
                lh.DropDownItems.Add(new ToolStripMenuItem(v.ToString("0.0"), null, (s, e) => ChangeSetting(() => St.LineHeight = vv)) { Checked = Math.Abs(St.LineHeight - v) < 0.01 });
            }
            m.Items.Add(lh);
            m.Items.Add(MarginMenu("양옆 여백", SideMargins, () => St.SideMargin, v => St.SideMargin = v, "[ ]"));
            m.Items.Add(MarginMenu("위아래 여백", TopMargins, () => St.TopMargin, v => St.TopMargin = v, null));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("자동 새로고침 (PC 2초 · GitHub 30초)", null, (s, e) =>
            {
                ChangeSetting(() => St.Auto = !St.Auto);
                Toast(St.Auto ? "자동 새로고침 켬" : "자동 새로고침 끔");
            }) { Checked = St.Auto });
            m.Items.Add(new ToolStripMenuItem("전체 화면 (F11)", null, (s, e) => ToggleFull()) { Checked = FormBorderStyle == FormBorderStyle.None });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("업데이트 확인 (지금 " + Updater.Current + ")", null, async (s, e) => { Toast("새 버전을 확인합니다"); await Updater.Check(this, true, Toast); }));
        }

        // 여백 고르기: 정해 둔 값 + 직접 입력
        ToolStripMenuItem MarginMenu(string label, int[] values, Func<int> get, Action<int> set, string keys)
        {
            var mi = new ToolStripMenuItem(label + ": " + get() + "px" + (keys != null ? "   (" + keys + " 키)" : ""));
            foreach (var v in values)
            {
                var vv = v;
                mi.DropDownItems.Add(new ToolStripMenuItem(v + "px", null, (s, e) => ChangeSetting(() => set(vv))) { Checked = get() == v });
            }
            mi.DropDownItems.Add(new ToolStripSeparator());
            mi.DropDownItems.Add("직접 입력…", null, (s, e) =>
            {
                string r = Microsoft.VisualBasic.Interaction.InputBox(label + "을(를) 픽셀로 입력하세요 (0 ~ 600)", "소설 뷰어", get().ToString());
                int n;
                if (int.TryParse(r.Trim(), out n)) ChangeSetting(() => set(Math.Max(0, Math.Min(600, n))));
            });
            return mi;
        }

        // ---- 책갈피 ----
        List<Mark> Marks()
        {
            List<Mark> l;
            if (current == null) return new List<Mark>();
            if (!data.Marks.TryGetValue(current, out l)) { l = new List<Mark>(); data.Marks[current] = l; }
            return l;
        }

        void AddMark()
        {
            if (current == null) return;
            int p = reader.TopParagraph();
            string text = reader.ParagraphText(p).Trim();
            Marks().Add(new Mark { P = p, Text = text.Length > 60 ? text.Substring(0, 60) : text, Pct = progress.Text, Time = Now() });
            data.Save();
            Toast("책갈피를 저장했습니다");
        }

        void BuildMarksMenu()
        {
            var m = marksMenu;
            m.Items.Clear();
            m.Items.Add(new ToolStripMenuItem("＋ 지금 위치에 책갈피 (B)", null, (s, e) => AddMark()) { Enabled = current != null });
            var marks = Marks().OrderBy(x => x.P).ToList();
            if (marks.Count == 0)
            {
                m.Items.Add(new ToolStripMenuItem(current == null ? "소설을 열면 책갈피를 쓸 수 있습니다" : "아직 책갈피가 없습니다") { Enabled = false });
                return;
            }
            m.Items.Add(new ToolStripSeparator());
            foreach (var mk in marks)
            {
                var k = mk;
                string label = (k.Text.Length > 0 ? k.Text : "(빈 줄)") + "   ·  " + k.Pct + " · " + FmtTime(k.Time);
                m.Items.Add(label, null, (s, e) => { reader.ShowParagraph(k.P); SetMenuHidden(true); });
            }
            m.Items.Add(new ToolStripSeparator());
            var del = new ToolStripMenuItem("책갈피 지우기");
            foreach (var mk in marks)
            {
                var k = mk;
                del.DropDownItems.Add(k.Text.Length > 0 ? k.Text : "(빈 줄)", null, (s, e) => { Marks().Remove(k); data.Save(); Toast("책갈피를 지웠습니다"); });
            }
            m.Items.Add(del);
        }

        // ---- 소설 목록 ----
        void SetTab(string tab)
        {
            data.NavTab = tab;
            tabRecent.Tag = tab == "recent" ? "on" : null;
            tabPc.Tag = tab == "pc" ? "on" : null;
            tabGh.Tag = tab == "gh" ? "on" : null;
            ApplySettings();
            if (tab == "pc") Browse("pc", data.PcDir);
            else if (tab == "gh") { if (data.GhName.Length > 0) Browse("gh", data.GhDir); else ShowGhForm(); }
            else RenderRecent();
        }

        void ShowList(string whereText, List<NavItem> items)
        {
            ghForm.Visible = false;
            list.Visible = true;
            where.Text = whereText;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var it in items) list.Items.Add(it);
            list.EndUpdate();
        }

        static NavItem Msg(string text) { return new NavItem { Title = text, IsMessage = true }; }

        void ShowGhForm()
        {
            list.Visible = false;
            ghForm.Visible = true;
            where.Text = "GitHub 연결";
            ghRepoIn.Text = data.GhName.Length > 0 ? "https://github.com/" + data.GhRepo + (data.GhRef.Length > 0 ? "/tree/" + data.GhRef + (data.GhRoot.Length > 0 ? "/" + data.GhRoot : "") : "") : "";
            ghTokenIn.Text = "";
            ghTokenIn.Enabled = true;
            ghClear.Visible = data.GhName.Length > 0;
        }

        void RenderRecent()
        {
            var items = new List<NavItem>();
            if (data.Recent.Count == 0) items.Add(Msg("아직 연 소설이 없습니다. 📁 PC 나 ☁ 깃허브 에서 고르세요."));
            foreach (var r in data.Recent)
            {
                var rr = r;
                string src = Sources.SrcOf(r.Id);
                items.Add(new NavItem
                {
                    Icon = src == "gh" ? "☁" : "💻", Title = StripExt(r.Name), Sub = (src == "gh" ? "GitHub" : "PC") + " · " + FmtTime(r.Time),
                    Id = r.Id, Active = r.Id == current, Open = () => Pick(rr.Id),
                    RemoveLabel = "최근 목록에서 지우기",
                    Remove = () => { data.Recent.RemoveAll(x => x.Id == rr.Id); data.Save(); RenderRecent(); },
                });
            }
            ShowList("최근 읽은 소설", items);
        }

        // PC 폴더나 GitHub 저장소를 둘러본다.
        async void Browse(string src, string dir)
        {
            ShowList(src == "gh" ? "GitHub" : "PC", new List<NavItem> { Msg("불러오는 중…") });
            Listing l;
            try { l = await Task.Run(() => src == "gh" ? Sources.BrowseGh(data, dir) : Sources.BrowsePc(data, dir)); }
            catch (Exception ex)
            {
                if (data.NavTab != src) return;
                if (!string.IsNullOrEmpty(dir)) { if (src == "gh") data.GhDir = ""; else data.PcDir = ""; Browse(src, ""); return; }
                var items = new List<NavItem> { Msg(ex is SourceError ? ex.Message : "열 수 없습니다.") };
                if (src == "gh") items.Add(new NavItem { Icon = "⚙", Title = "GitHub 설정 바꾸기", Open = ShowGhForm });
                ShowList(src == "gh" ? "GitHub" : "PC", items);
                return;
            }
            if (data.NavTab != src) return;
            if (src == "gh") data.GhDir = l.Dir; else data.PcDir = l.Dir;
            data.Save();
            var list2 = new List<NavItem>();
            string whereText;
            if (src == "gh")
            {
                string root = data.GhRoot;
                string sub = root.Length > 0 && l.Dir.StartsWith(root + "/") ? l.Dir.Substring(root.Length + 1) : l.Dir;
                whereText = "GitHub · " + data.GhName + (sub.Length > 0 ? " / " + sub : "");
                if (l.Dir.Length == 0) list2.Add(new NavItem { Icon = "⚙", Title = "GitHub 설정 바꾸기", Open = ShowGhForm });
            }
            else
            {
                whereText = l.Dir.Length > 0 ? l.Dir : "소설 폴더 (＋ 폴더 추가로 늘리기, 오른쪽 클릭으로 빼기)";
                if (l.Dir.Length == 0) list2.Add(new NavItem { Icon = "＋", Title = "폴더 추가", Sub = "이 PC의 소설 폴더를 고릅니다", Open = AddFolder });
            }
            if (l.Parent != null) { var up = l.Parent; list2.Add(new NavItem { Icon = "⬆", Title = "상위 폴더", Open = () => Browse(src, up) }); }
            foreach (var e in l.Entries)
            {
                var en = e;
                if (e.IsDir)
                {
                    var it = new NavItem { Icon = "📁", Title = e.Name, Open = () => Browse(src, en.Path) };
                    if (src == "pc" && l.Dir.Length == 0)
                    {
                        it.RemoveLabel = "목록에서 빼기 (폴더는 지우지 않음)";
                        it.Remove = () => { data.Folders.Remove(en.Path); data.Save(); Toast("목록에서 뺐습니다"); Browse("pc", ""); };
                    }
                    list2.Add(it);
                }
                else
                {
                    string id = src + ":" + e.Path;
                    string sub = (e.Mtime.HasValue ? e.Mtime.Value.ToString("M. d. tt h:mm", Ko) + " · " : "") + (e.Size / 1024.0).ToString("0.0") + "KB";
                    list2.Add(new NavItem { Icon = "📄", Title = StripExt(e.Name), Sub = sub, Id = id, Active = id == current, Open = () => Pick(id) });
                }
            }
            if (l.Entries.Count == 0) list2.Add(Msg(src == "pc" && l.Dir.Length == 0 ? "＋ 폴더 추가로 소설 폴더를 고르세요." : "이 폴더에는 txt 파일이 없습니다."));
            ShowList(whereText, list2);
        }

        void AddFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "소설 폴더를 고르세요", ShowNewFolderButton = false })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string dir = d.SelectedPath;
                if (data.Folders.Any(f => string.Equals(f, dir, StringComparison.OrdinalIgnoreCase))) Toast("이미 추가된 폴더입니다");
                else { data.Folders.Add(dir); data.Save(); Toast("폴더를 추가했습니다"); }
            }
            Browse("pc", "");
        }

        void DrawNavItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var it = (NavItem)list.Items[e.Index];
            var t = Th;
            var g = e.Graphics;
            var r = e.Bounds;
            using (var b = new SolidBrush(it.Active ? t.Bg : t.Panel)) g.FillRectangle(b, r);
            using (var p = new Pen(t.Line)) g.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            if (it.IsMessage)
            {
                TextRenderer.DrawText(g, it.Title, Font, new Rectangle(r.X + S(12), r.Y, r.Width - S(24), r.Height), t.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            using (var f = new Font("Segoe UI Emoji", 12f))
                TextRenderer.DrawText(g, it.Icon ?? "", f, new Rectangle(r.X + S(8), r.Y, S(30), r.Height), t.Fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            int x = r.X + S(44), w = r.Width - S(52);
            var fl = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var tf = it.Active ? new Font(Font, FontStyle.Bold) : Font;
            if (string.IsNullOrEmpty(it.Sub))
                TextRenderer.DrawText(g, it.Title, tf, new Rectangle(x, r.Y, w, r.Height), t.Fg, fl | TextFormatFlags.VerticalCenter);
            else
            {
                TextRenderer.DrawText(g, it.Title, tf, new Rectangle(x, r.Y + S(6), w, S(20)), t.Fg, fl);
                using (var sf = new Font("맑은 고딕", 8f))
                    TextRenderer.DrawText(g, it.Sub, sf, new Rectangle(x, r.Y + S(26), w, S(18)), t.Muted, fl);
            }
            if (it.Active) tf.Dispose();
        }

        // ---- 소설 열기 ----
        async void Pick(string id)
        {
            SavePos();
            if (await OpenFile(id, true, null))
            {
                if (ClientSize.Width < S(900)) { St.Nav = false; ApplySettings(); }
            }
        }

        // at: "start" = 첫 페이지부터, "end" = 마지막 페이지부터, null = 읽던 곳부터
        async Task<bool> OpenFile(string id, bool fresh, string at)
        {
            if (busy) return false;
            busy = true;
            FileData f;
            try { f = await Task.Run(() => Sources.Read(data, id)); }
            catch (Exception ex)
            {
                busy = false;
                Toast((Sources.SrcOf(id) == "gh" ? "GitHub" : "PC") + " 파일을 열 수 없습니다" + (ex is SourceError ? ": " + ex.Message : ""));
                return false;
            }
            busy = false;
            int keep;
            if (fresh) { if (!data.Pos.TryGetValue(id, out keep)) keep = 0; }
            else keep = reader.TopParagraph();
            bool same = id == current;
            current = id;
            currentMtime = f.Mtime;
            data.Current = id;
            if (fresh)
            {
                data.Recent.RemoveAll(x => x.Id == id);
                data.Recent.Insert(0, new RecentItem { Id = id, Name = f.Name, Time = Now() });
                if (data.Recent.Count > 30) data.Recent.RemoveRange(30, data.Recent.Count - 30);
            }
            reader.SetText(f.Text);
            ApplySettings();
            if (at == "start") reader.GoStart();
            else if (at == "end") reader.GoEnd();
            else reader.ShowParagraph(keep);
            title.Text = StripExt(f.Name);
            Text = StripExt(f.Name) + " - 소설 뷰어";
            status.Text = Sources.SrcOf(id) == "gh" ? "GitHub · " + data.GhName + " · 마지막 push 내용"
                : "PC · " + new DateTime(long.Parse(f.Mtime), DateTimeKind.Utc).ToLocalTime().ToString("M. d. tt h:mm", Ko) + " 저장본";
            if (fresh || !same) LoadChapters(id);
            if (data.NavTab == "recent" && list.Visible) RenderRecent();
            else
            {
                foreach (NavItem it in list.Items) it.Active = it.Id != null && it.Id == current;
                list.Invalidate();
            }
            UpdateProgress();
            data.Save();
            reader.Focus();
            return true;
        }

        async Task Refresh(bool manual)
        {
            if (current == null) return;
            if (await OpenFile(current, false, null) && manual) Toast("새로 불러왔습니다");
        }

        // 자동 새로고침: PC 파일은 2초, GitHub 은 30초마다 바뀌었는지 본다.
        async Task Poll()
        {
            if (!St.Auto || current == null || busy) return;
            string id = current;
            if (Sources.SrcOf(id) == "gh")
            {
                if ((DateTime.Now - ghChecked).TotalSeconds < 30) return;
                ghChecked = DateTime.Now;
            }
            string m;
            try { m = await Task.Run(() => Sources.Stat(data, id)); } catch { return; }
            if (id == current && m != currentMtime && await OpenFile(id, false, null)) Toast("저장된 내용으로 새로고침했습니다");
        }

        void SavePos()
        {
            if (current == null || !reader.HasText) return;
            data.Pos[current] = reader.TopParagraph();
            data.Save();
        }

        void UpdateProgress()
        {
            if (current == null || !reader.HasText) { progress.Text = ""; seekVal.Text = ""; return; }
            if (reader.PageMode)
            {
                progress.Text = (reader.PageIndex + 1) + " / " + reader.PageCount;
                seekVal.Text = progress.Text + " 쪽";
                seek.Max = reader.PageCount - 1;
                seek.Value = reader.PageIndex;
            }
            else
            {
                progress.Text = Math.Min(100, reader.ScrollPermille / 10) + "%";
                seekVal.Text = progress.Text;
                seek.Max = 1000;
                seek.Value = reader.ScrollPermille;
            }
            seek.Invalidate();
        }

        // ---- 다음 화 / 이전 화 ----
        async void LoadChapters(string id)
        {
            chapters = new List<string>();
            try
            {
                var l = await Task.Run(() => Sources.Siblings(data, id));
                if (current == id) chapters = l;
            }
            catch { }
        }

        void AskChapter(int dir) { GoChapter(dir, true); }

        // ask: 페이지 끝에서 넘길 때는 물어보고, 이전 장·다음 장 버튼은 바로 연다.
        void GoChapter(int dir, bool ask)
        {
            int i = chapters.IndexOf(current);
            string target = i < 0 || i + dir < 0 || i + dir >= chapters.Count ? null : chapters[i + dir];
            if (target == null)
            {
                if (!ask) Toast(i < 0 ? "앞뒤 장을 찾지 못했습니다" : dir > 0 ? "이 폴더의 마지막 장입니다" : "이 폴더의 첫 장입니다");
                else Toast(dir > 0 ? (i < 0 ? "마지막 페이지입니다" : "이 폴더의 마지막 화입니다") : "첫 페이지입니다");
                return;
            }
            string name = StripExt(target.Split('\\', '/').Last()).Replace('_', ' ');
            string q = dir > 0 ? "이 화를 다 읽었습니다. 다음 화로 넘어갈까요?" : "이전 화로 돌아갈까요?";
            if (ask && MessageBox.Show(this, q + "\n\n" + name, dir > 0 ? "다음 화" : "이전 화", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SavePos();
            // 다음 장은 첫 페이지부터. 끝에서 거꾸로 넘긴 이전 장은 마지막 페이지부터, 버튼으로 연 이전 장은 첫 페이지부터.
            var t = OpenFile(target, true, dir > 0 || !ask ? "start" : "end");
        }

        // ---- 시작 ----
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 켠 뒤 잠시 있다가 새 버전을 조용히 확인한다(있을 때만 물어본다).
            // 이어 읽기 질문 같은 창이 떠 있으면 그 창을 닫을 때까지 기다린다.
            var upd = new Timer { Interval = 3000 };
            upd.Tick += async (s, ev) =>
            {
                if (Form.ActiveForm != this) return;
                upd.Stop(); upd.Dispose();
                await Updater.Check(this, false, Toast);
            };
            upd.Start();
            string last = current;
            current = null;
            ApplySettings();
            if (last == null) { St.Nav = true; ApplySettings(); SetTab(data.NavTab); return; }
            St.Nav = false;
            ApplySettings();
            string src = Sources.SrcOf(last) == "gh" ? "GitHub" : "PC";
            string name = StripExt(RecentName(last)).Replace('_', ' ');
            var ans = MessageBox.Show(this, "마지막으로 읽던 소설을 이어 읽을까요?\n\n" + name + "\n(" + src + " · 읽던 페이지부터)", "소설 뷰어",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            while (ans == DialogResult.Yes)
            {
                if (await OpenFile(last, true, null)) return;
                ans = MessageBox.Show(this, "열 수 없습니다. " + (src == "GitHub" ? "인터넷 연결과 GitHub 설정(☰ → ☁ 깃허브)을 확인하세요." : "파일이 옮겨지거나 이름이 바뀌었는지 확인하세요.") +
                    "\n\n다시 시도할까요?", "소설 뷰어", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) == DialogResult.Retry ? DialogResult.Yes : DialogResult.No;
            }
            St.Nav = true; ApplySettings(); SetTab(data.NavTab);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SavePos();
            if (FormBorderStyle == FormBorderStyle.None) ToggleFull();
            data.WinMax = WindowState == FormWindowState.Maximized;
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            data.WinX = b.X; data.WinY = b.Y; data.WinW = b.Width; data.WinH = b.Height;
            data.Save();
            base.OnFormClosing(e);
        }

        void ToggleFull()
        {
            if (FormBorderStyle == FormBorderStyle.None)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = beforeFull;
            }
            else
            {
                beforeFull = WindowState;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
        }

        // ---- 키보드 ----
        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            // 입력 칸에 글자를 칠 때는 단축키가 끼어들지 않게 한다.
            if (ActiveControl is TextBox) return base.ProcessCmdKey(ref msg, key);
            switch (key)
            {
                case Keys.Right: case Keys.Down: case Keys.PageDown: case Keys.Space:
                    if (ActiveControl == list && (key == Keys.Down)) break;
                    reader.Turn(1); return true;
                case Keys.Left: case Keys.Up: case Keys.PageUp:
                    if (ActiveControl == list && (key == Keys.Up)) break;
                    reader.Turn(-1); return true;
                case Keys.Home: reader.GoStart(); return true;
                case Keys.End: reader.GoEnd(); return true;
                case Keys.R: case Keys.F5: var t = Refresh(true); return true;
                case Keys.B: AddMark(); return true;
                case Keys.L: btnList.PerformClick(); return true;
                case Keys.Oemplus: case Keys.Add: ChangeSetting(() => St.FontSize = Math.Min(40, St.FontSize + 1)); return true;
                case Keys.OemMinus: case Keys.Subtract: ChangeSetting(() => St.FontSize = Math.Max(12, St.FontSize - 1)); return true;
                case Keys.OemOpenBrackets: ChangeSetting(() => St.SideMargin = Math.Max(0, St.SideMargin - 8)); Toast("양옆 여백 " + St.SideMargin + "px"); return true;
                case Keys.OemCloseBrackets: ChangeSetting(() => St.SideMargin = Math.Min(600, St.SideMargin + 8)); Toast("양옆 여백 " + St.SideMargin + "px"); return true;
                case Keys.F11: ToggleFull(); return true;
                case Keys.Escape:
                    if (FormBorderStyle == FormBorderStyle.None) { ToggleFull(); return true; }
                    if (menuHidden) { SetMenuHidden(false); return true; }
                    break;
            }
            return base.ProcessCmdKey(ref msg, key);
        }

        // ---- 작은 도우미 ----
        void Toast(string text)
        {
            toast.Text = text;
            toast.Visible = true;
            toast.Location = new Point((reader.ClientSize.Width - toast.Width) / 2, reader.ClientSize.Height - toast.Height - S(24));
            toast.BringToFront();
            toastTimer.Stop();
            toastTimer.Start();
        }

        string RecentName(string id)
        {
            var r = data.Recent.FirstOrDefault(x => x.Id == id);
            return r != null ? r.Name : id.Split('\\', '/').Last();
        }

        static string StripExt(string n) { return System.Text.RegularExpressions.Regex.Replace(n ?? "", "\\.(txt|md|text)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
        static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
        static string FmtTime(long ms) { return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("M. d. tt h:mm", Ko); }
    }

    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr h, int cmd);

        [STAThread]
        static void Main()
        {
            // 한 번에 하나만 켠다(둘이 켜지면 설정을 서로 덮어쓴다). 이미 켜져 있으면 그 창을 앞으로 가져온다.
            bool first;
            var mutex = new System.Threading.Mutex(true, "NovelViewer.PC.SingleInstance", out first);
            if (!first)
            {
                var me = Process.GetCurrentProcess();
                foreach (var p in Process.GetProcessesByName(me.ProcessName))
                    if (p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero) { ShowWindow(p.MainWindowHandle, 9); SetForegroundWindow(p.MainWindowHandle); }
                return;
            }
            try { SetProcessDPIAware(); } catch { }
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; // TLS 1.2
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            GC.KeepAlive(mutex);
        }
    }
}
