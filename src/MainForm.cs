using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SeccFirmwarePackager
{
    internal static class UiColors
    {
        public static readonly Color Canvas = Color.FromArgb(242, 241, 237);
        public static readonly Color Card = Color.FromArgb(251, 250, 247);
        public static readonly Color Field = Color.FromArgb(246, 245, 241);
        public static readonly Color Ink = Color.FromArgb(48, 51, 48);
        public static readonly Color Muted = Color.FromArgb(112, 114, 107);
        public static readonly Color Blue = Color.FromArgb(77, 105, 87);
        public static readonly Color SoftBlue = Color.FromArgb(232, 237, 229);
        public static readonly Color Border = Color.FromArgb(221, 222, 214);
    }

    internal sealed class SectionCard : Panel
    {
        public SectionCard()
        {
            BackColor = UiColors.Canvas;
            Padding = new Padding(18, 14, 18, 18);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (Width < 4 || Height < 4) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(1.5F, 1.5F, Width - 6F, Height - 7F);
            const float d = 22F;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (System.Drawing.Drawing2D.Matrix shift = new System.Drawing.Drawing2D.Matrix())
                using (GraphicsPath shadow = (GraphicsPath)path.Clone())
                {
                    shift.Translate(1, 3); shadow.Transform(shift);
                    using (Brush shade = new SolidBrush(Color.FromArgb(18, 74, 72, 62))) e.Graphics.FillPath(shade, shadow);
                }
                using (Brush fill = new SolidBrush(UiColors.Card)) e.Graphics.FillPath(fill, path);
                using (Pen border = new Pen(UiColors.Border)) e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class SoftField : Panel
    {
        private readonly TextBox input;

        public SoftField(TextBox box, bool multiline)
        {
            input = box;
            BackColor = UiColors.Field;
            Padding = multiline ? new Padding(9, 7, 9, 7) : new Padding(8, 5, 8, 3);
            Margin = new Padding(3, 3, 3, 3);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            input.BorderStyle = BorderStyle.None;
            input.BackColor = UiColors.Field;
            input.Dock = DockStyle.Fill;
            input.Enter += delegate { Invalidate(); };
            input.Leave += delegate { Invalidate(); };
            Controls.Add(input);
            Click += delegate { if (input.CanFocus) input.Focus(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 4 || Height < 4) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5F, 0.5F, Width - 2F, Height - 2F);
            const float d = 9F;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (Pen border = new Pen(input.Focused ? UiColors.Blue : UiColors.Border))
                    e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class FtpAddressBox : TextBox
    {
        public Action<string> PasteAddress;

        protected override void WndProc(ref Message message)
        {
            const int WM_PASTE = 0x0302;
            if (message.Msg == WM_PASTE && PasteAddress != null)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string value = Clipboard.GetText().Trim();
                        if (value.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                        {
                            PasteAddress(value);
                            return;
                        }
                    }
                }
                catch (ExternalException) { }
            }
            base.WndProc(ref message);
        }
    }

    public sealed class MainForm : Form
    {
        private sealed class Row
        {
            public FirmwareType Type;
            public TextBox PathBox;
            public TextBox NameBox;
            public TextBox VersionBox;
            public Label SizeLabel;
            public string AutoName;
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox output = new TextBox();
        private readonly TextBox details = new TextBox();
        private readonly TextBox status = new TextBox();
        private readonly Button build = new RaisedButton();
        private readonly FtpAddressBox ftpAddress = new FtpAddressBox();
        private readonly TextBox ftpUser = new TextBox();
        private readonly TextBox ftpPassword = new TextBox();
        private readonly Button ftpUpload = new RaisedButton();
        private readonly Label ftpStatus = new Label();
        private readonly ToolTip tooltips = new ToolTip();
        private readonly UiSounds sounds = new UiSounds();
        private Button ftpBrowseFile;
        private TableLayoutPanel workspace;
        private SectionCard previewCard;
        private readonly TextBox ftpFile = new TextBox();
        private readonly Label ftpSourceKind = new Label();
        private string lastGeneratedPackage;
        private byte[] lastGeneratedHeader;
        private readonly Button headerView = new RaisedButton();
        private bool ftpPackageSource = true;
        private string ftpAuthority;
        private bool ftpBusy;
        private bool changing;
        private string lastPreviewMessage;

        public MainForm()
        {
            Text = "打包工具 · V0.0.1"; Name = "MainForm";
            MinimumSize = new Size(1100, 720);
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Max(1100, Math.Min(1320, work.Width - 24)), Math.Max(720, Math.Min(820, work.Height - 24)));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9.5F); AutoScaleMode = AutoScaleMode.Dpi; BackColor = UiColors.Canvas;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Panel helpStrip = new Panel { Name = "HelpStrip", Dock = DockStyle.Top, Height = 28, BackColor = UiColors.Canvas, Padding = new Padding(0, 1, 18, 2) };
            Button help = new RaisedButton { Name = "HelpButton", Text = "ⓘ", AccessibleName = "操作说明", Size = new Size(30, 25), Dock = DockStyle.Right };
            helpStrip.Controls.Add(help);
            help.Click += delegate { ShowHelp(); }; tooltips.SetToolTip(help, "操作说明");
            Controls.Add(helpStrip);
            Panel scroll = new Panel { Name = "ContentScroll", Dock = DockStyle.Fill, AutoScroll = true }; Controls.Add(scroll); scroll.BringToFront();
            workspace = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3,
                Padding = new Padding(18, 14, 18, 14), Margin = Padding.Empty };
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 440));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 270)); scroll.Controls.Add(workspace);
            TableLayoutPanel inputs = CardLayout(5); inputs.Name = "InputsCard"; inputs.BackColor = UiColors.Canvas;
            for (int i = 0; i < 5; i++) inputs.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            workspace.Controls.Add(inputs, 0, 0);
            FirmwareType[] types = { FirmwareType.PFC, FirmwareType.LLC, FirmwareType.M4, FirmwareType.PLC, FirmwareType.ESP32 };
            for (int i = 0; i < types.Length; i++)
            {
                TableLayoutPanel staging = new TableLayoutPanel { ColumnCount = 7, RowCount = 2 };
                AddRow(staging, types[i], 1);
                Control[] c = new Control[7]; for (int n = 0; n < 7; n++) c[n] = staging.GetControlFromPosition(n, 1);
                SectionCard card = new SectionCard { Name = "FirmwareCard_" + types[i], Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 6), Padding = new Padding(12, 8, 12, 10) };
                TableLayoutPanel item = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = Padding.Empty, BackColor = UiColors.Card };
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
                item.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); item.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                c[0].Font = new Font(Font.FontFamily, 12F, FontStyle.Bold); c[0].BackColor = UiColors.Card; c[0].ForeColor = UiColors.Blue;
                item.Controls.Add(c[0], 0, 0); item.SetRowSpan(c[0], 2);
                item.Controls.Add(c[1], 1, 0); item.SetColumnSpan(c[1], 2);
                item.Controls.Add(c[2], 3, 0); item.Controls.Add(c[3], 3, 1);
                item.Controls.Add(c[4], 1, 1); item.Controls.Add(c[5], 2, 1);
                ConfigureIcon((Button)c[2], "folder", "选择 " + types[i] + " 固件");
                ConfigureIcon((Button)c[3], "clear", "清除 " + types[i] + " 选择（不删除文件）");
                rows[i].PathBox.AccessibleName = types[i] + " 固件路径";
                rows[i].NameBox.AccessibleName = types[i] + " 固件名称"; rows[i].VersionBox.AccessibleName = types[i] + " 固件版本";
                // Size is shown in the dynamic list rather than repeated in each card.
                c[6].Parent = null; c[6].Visible = false;
                card.Controls.Add(item); inputs.Controls.Add(card, 0, i); staging.Dispose();
            }
            TableLayoutPanel right = CardLayout(3); right.BackColor = UiColors.Canvas;
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 108)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); workspace.Controls.Add(right, 1, 0);
            SectionCard loadCard = new SectionCard { Name = "AutoLoadCard", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 8), Padding = new Padding(12, 8, 12, 10) };
            TableLayoutPanel load = CardLayout(2); load.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); load.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TextBox folder = new PlaceholderTextBox { Name = "FolderPath", PlaceholderText = "选择固件文件夹", AccessibleName = "批量加载固件文件夹" };
            Button folderButton = new RaisedButton { Name = "BrowseFolder", Dock = DockStyle.Fill };
            ConfigureIcon(folderButton, "folder", "选择固件文件夹"); load.Controls.Add(PathLine(folder, folderButton), 0, 0);
            Button autoLoad = MainButton("AutoLoadButton", "一键加载", true);
            Button clearAll = MainButton("ClearAllButton", "一键清理", false);
            clearAll.Click += delegate { ClearAllInputs(); };
            folderButton.Click += delegate { ChooseFolder(); };
            autoLoad.Click += delegate { LoadFolder(folder.Text); };
            tooltips.SetToolTip(clearAll, "清空固件选择，不删除源文件");
            load.Controls.Add(RightActions(clearAll, autoLoad), 0, 1);
            loadCard.Controls.Add(load); right.Controls.Add(loadCard, 0, 0);
            SectionCard packageCard = new SectionCard { Name = "PackageCard", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 8), Padding = new Padding(12, 8, 12, 10) };
            TableLayoutPanel package = CardLayout(2); package.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); package.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            output.Name = "OutputPath"; output.AccessibleName = "输出 BIN 文件路径";
            output.TextChanged += delegate { tooltips.SetToolTip(output, output.Text); };
            Button browse = new RaisedButton { Name = "BrowseOutput", Dock = DockStyle.Fill }; ConfigureIcon(browse, "folder", "选择输出位置"); browse.Click += BrowseOutput;
            package.Controls.Add(PathLine(output, browse), 0, 0);
            build.Name = "BuildButton"; build.Text = "包生成"; SetupMainButton(build, true); build.Click += BuildClicked;
            Button verify = MainButton("OpenVerifyButton", "包校验", false); verify.Click += OpenClicked;
            package.Controls.Add(RightActions(verify, build), 0, 1); packageCard.Controls.Add(package); right.Controls.Add(packageCard, 0, 1);
            SectionCard ftpCard = new SectionCard { Name = "FtpInlinePanel", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 6), Padding = new Padding(12, 8, 12, 10) };
            TableLayoutPanel ftp = CardLayout(5);
            for (int i = 0; i < 4; i++) ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); ftp.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ftpFile.Name = "FtpFilePath"; ftpFile.ReadOnly = true; ftpFile.AllowDrop = true; ftpFile.AccessibleName = "FTP 上传文件";
            ftpFile.DragEnter += InputDragEnter;
            ftpFile.DragDrop += delegate(object sender, DragEventArgs e) { string[] files = DraggedFiles(e); if (!ftpBusy && files != null && files.Length == 1 && File.Exists(files[0])) SetFtpFile(files[0], false); };
            ftpFile.TextChanged += delegate { tooltips.SetToolTip(ftpFile, ftpFile.Text); };
            ftpBrowseFile = new RaisedButton { Name = "FtpBrowseFile", Dock = DockStyle.Fill }; ConfigureIcon(ftpBrowseFile, "folder", "选择 FTP 上传文件");
            ftpBrowseFile.Click += delegate { using (OpenFileDialog d = new OpenFileDialog { Title = "选择 FTP 上传文件", Filter = "所有文件 (*.*)|*.*", CheckFileExists = true }) if (d.ShowDialog(this) == DialogResult.OK) SetFtpFile(d.FileName, false); };
            ftp.Controls.Add(LabeledLine("文件", PathLine(ftpFile, ftpBrowseFile)), 0, 0);
            ftpAddress.Name = "FtpAddress"; ftpAddress.AccessibleName = "FTP URL";
            ftpAddress.PasteAddress = delegate(string value) { ApplyFtpAddress(value, true); };
            ftpAddress.Leave += delegate { if (!String.IsNullOrWhiteSpace(ftpAddress.Text)) ApplyFtpAddress(ftpAddress.Text, false); };
            ftpAddress.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyFtpAddress(ftpAddress.Text, true); } };
            ftpUser.Name = "FtpUser"; ftpUser.AccessibleName = "FTP 用户名";
            ftpPassword.Name = "FtpPassword"; ftpPassword.UseSystemPasswordChar = true; ftpPassword.AccessibleName = "FTP 密码";
            ftp.Controls.Add(LabeledLine("URL", new SoftField(ftpAddress, false) { Dock = DockStyle.Fill }), 0, 1);
            ftp.Controls.Add(LabeledLine("用户名", new SoftField(ftpUser, false) { Dock = DockStyle.Fill }), 0, 2);
            ftp.Controls.Add(LabeledLine("密码", new SoftField(ftpPassword, false) { Dock = DockStyle.Fill }), 0, 3);
            ftpUpload.Name = "FtpUploadButton"; ftpUpload.Text = "上传到 FTP"; SetupMainButton(ftpUpload, true); ftpUpload.Click += FtpUploadClicked;
            ftp.Controls.Add(RightActions(ftpUpload), 0, 4); ftpCard.Controls.Add(ftp); right.Controls.Add(ftpCard, 0, 2);
            ftpStatus.TextChanged += delegate { details.ForeColor = ftpStatus.ForeColor; details.Text = ftpStatus.Text; };
            FormClosing += FtpFormClosing;
            grid.Name = "PreviewGrid"; grid.Dock = DockStyle.Fill; grid.ReadOnly = true;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false; grid.ColumnHeadersVisible = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BorderStyle = BorderStyle.None; grid.BackgroundColor = UiColors.Card; grid.GridColor = UiColors.Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.RowTemplate.Height = 27; grid.ScrollBars = ScrollBars.Horizontal;
            grid.DefaultCellStyle.SelectionBackColor = UiColors.SoftBlue; grid.DefaultCellStyle.SelectionForeColor = UiColors.Ink;
            grid.DefaultCellStyle.BackColor = UiColors.Card;
            grid.Columns.Add("Type", "类型"); grid.Columns.Add("Name", "文件路径"); grid.Columns.Add("Size", "大小"); grid.Columns.Add("Offset", "偏移");
            grid.Columns[0].FillWeight = 40; grid.Columns[1].FillWeight = 240; grid.Columns[2].FillWeight = 100; grid.Columns[3].FillWeight = 70;
            grid.Columns[0].DefaultCellStyle.BackColor = UiColors.SoftBlue;
            previewCard = new SectionCard { Name = "PreviewCard", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4), Padding = new Padding(14, 10, 14, 12), Visible = false };
            previewCard.Controls.Add(grid); workspace.Controls.Add(previewCard, 0, 1); workspace.SetColumnSpan(previewCard, 2);
            SectionCard result = new SectionCard { Name = "OperationResultCard", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(14, 8, 14, 10) };
            TableLayoutPanel resultBody = CardLayout(2); resultBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); resultBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TableLayoutPanel resultHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = UiColors.Card };
            resultHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); resultHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            resultHeading.Controls.Add(TitleLabel("操作结果"), 0, 0);
            headerView.Name = "HeaderViewButton"; headerView.Text = "查看包头"; headerView.Enabled = false; headerView.Dock = DockStyle.Fill; headerView.Margin = Padding.Empty;
            StyleButton(headerView, false, true); headerView.Click += delegate { ShowGeneratedHeader(); };
            resultHeading.Controls.Add(headerView, 1, 0); resultBody.Controls.Add(resultHeading, 0, 0);
            details.Name = "ResultDetails"; details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Both;
            details.WordWrap = false; details.Text = "";
            resultBody.Controls.Add(new SoftField(details, true) { Dock = DockStyle.Fill, Padding = new Padding(9, 5, 9, 5) }, 0, 1); result.Controls.Add(resultBody);
            workspace.Controls.Add(result, 0, 2); workspace.SetColumnSpan(result, 2);
            RefreshPreview();
        }

        private Button MainButton(string name, string text, bool primary)
        { Button b = new RaisedButton { Name = name, Text = text }; SetupMainButton(b, primary); return b; }
        private void SetupMainButton(Button b, bool primary)
        { StyleButton(b, primary, false); b.Size = new Size(180, 40); b.Anchor = AnchorStyles.Right | AnchorStyles.Top; }
        private static Control RightActions(params Button[] buttons)
        {
            // Keep action buttons compact and aligned across the three cards.
            TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = UiColors.Card };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                buttons[i].Margin = new Padding(3, 5, 3, 3);
                p.Controls.Add(buttons[i], buttons.Length == 1 ? 2 : i + 1, 0);
            }
            return p;
        }
        private static Control PathLine(TextBox field, Button browse)
        {
            TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.Controls.Add(new SoftField(field, false) { Dock = DockStyle.Fill }, 0, 0); p.Controls.Add(browse, 1, 0); return p;
        }
        private static Control LabeledLine(string title, Control content)
        {
            TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.Controls.Add(FieldLabel(title), 0, 0); p.Controls.Add(content, 1, 0); return p;
        }
        private void ConfigureIcon(Button b, string icon, string accessible)
        {
            StyleButton(b, false, true); b.Text = accessible; b.AccessibleName = accessible;
            RaisedButton raised = b as RaisedButton;
            if (raised != null) { raised.IconOnly = true; raised.Glyph = icon == "clear" ? ButtonGlyph.Clear : b.Name == "BrowseOutput" ? ButtonGlyph.FolderDownload : ButtonGlyph.Folder; }
            tooltips.SetToolTip(b, accessible);
        }

        private static Label TitleLabel(string title)
        {
            Label label = FieldLabel(title); label.ForeColor = UiColors.Ink;
            label.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold); return label;
        }

        private void SetFtpFile(string path, bool package)
        {
            ftpFile.Text = Path.GetFullPath(path);
            ftpPackageSource = package;
            ftpSourceKind.Text = package ? "联合包" : "本地文件";
        }

        private static Label FieldLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = UiColors.Muted;
            label.Margin = Padding.Empty;
            return label;
        }

        private static TableLayoutPanel CardLayout(int rows)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Margin = Padding.Empty;
            layout.BackColor = UiColors.Card;
            layout.ColumnCount = 1;
            layout.RowCount = rows;
            return layout;
        }

        private static void StyleTextBox(TextBox box, bool readOnly)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = UiColors.Field;
            box.ForeColor = UiColors.Ink;
        }

        private void StyleButton(Button button, bool primary, bool subtle)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(3, 3, 3, 3);
            button.Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? Color.FromArgb(219, 230, 214) : (subtle ? Color.White : Color.FromArgb(238, 234, 242));
            button.ForeColor = subtle ? UiColors.Muted : UiColors.Ink;
            button.Click -= PlayClickSound;
            button.Click += PlayClickSound;
        }

        private void PlayClickSound(object sender, EventArgs e) { sounds.Play("click"); }

        private void AddRow(TableLayoutPanel table, FirmwareType type, int index)
        {
            Row row = new Row();
            row.Type = type;
            rows.Add(row);
            Label label = new Label();
            label.Text = type.ToString();
            label.Dock = DockStyle.Fill;
            label.Margin = new Padding(4, 4, 8, 4);
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            label.ForeColor = UiColors.Blue;
            label.BackColor = UiColors.SoftBlue;
            table.Controls.Add(label, 0, index);
            row.PathBox = new TextBox();
            row.PathBox.Name = "Path_" + type;
            row.PathBox.ReadOnly = true;
            row.PathBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (!e.Control || e.KeyCode != Keys.V) return;
                e.SuppressKeyPress = true;
                try { SetInputFile(row, Clipboard.GetText().Trim().Trim('"')); }
                catch (Exception ex) { ShowInputResult(ex.Message, true); }
            };
            row.PathBox.AllowDrop = true;
            StyleTextBox(row.PathBox, false);
            row.PathBox.TextChanged += delegate { InputPathChanged(row); };
            row.PathBox.TextChanged += delegate { tooltips.SetToolTip(row.PathBox, row.PathBox.Text); };
            row.PathBox.DragEnter += InputDragEnter;
            row.PathBox.DragDrop += delegate(object sender, DragEventArgs e) { InputDragDrop(row, e); };
            SoftField pathField = new SoftField(row.PathBox, false);
            pathField.Dock = DockStyle.Fill;
            pathField.AllowDrop = true;
            pathField.DragEnter += InputDragEnter;
            pathField.DragDrop += delegate(object sender, DragEventArgs e) { InputDragDrop(row, e); };
            Label dragHint = new Label { Name = "DragHint_" + type, Text = "拖入 BIN 或粘贴路径", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiColors.Muted, BackColor = UiColors.Field, AutoEllipsis = true, AllowDrop = true };
            dragHint.Click += delegate { row.PathBox.Focus(); };
            dragHint.DragEnter += InputDragEnter;
            dragHint.DragDrop += delegate(object sender, DragEventArgs e) { InputDragDrop(row, e); };
            pathField.Controls.Add(dragHint);
            dragHint.BringToFront();
            EventHandler updateDragHint = delegate { dragHint.Visible = String.IsNullOrWhiteSpace(row.PathBox.Text); };
            row.PathBox.TextChanged += updateDragHint;
            table.Controls.Add(pathField, 1, index);
            Button select = new RaisedButton();
            select.Name = "Select_" + type;
            select.Text = "浏览";
            select.Dock = DockStyle.Fill;
            StyleButton(select, false, false);
            select.Click += delegate { SelectInput(row); };
            table.Controls.Add(select, 2, index);
            Button clear = new RaisedButton();
            clear.Name = "Clear_" + type;
            clear.Text = "清除";
            clear.Dock = DockStyle.Fill;
            StyleButton(clear, false, true);
            clear.Click += delegate { ClearInput(row); };
            table.Controls.Add(clear, 3, index);
            row.NameBox = new PlaceholderTextBox { PlaceholderText = "固件名称" };
            row.NameBox.Name = "Name_" + type;
            StyleTextBox(row.NameBox, false);
            row.NameBox.TextChanged += InputChanged;
            row.NameBox.TextChanged += delegate { tooltips.SetToolTip(row.NameBox, row.NameBox.Text); };
            SoftField nameField = new SoftField(row.NameBox, false);
            nameField.Dock = DockStyle.Fill;
            table.Controls.Add(nameField, 4, index);
            row.VersionBox = new PlaceholderTextBox { PlaceholderText = "固件版本" };
            row.VersionBox.Name = "Version_" + type;
            StyleTextBox(row.VersionBox, false);
            row.VersionBox.TextChanged += InputChanged;
            row.VersionBox.TextChanged += delegate { tooltips.SetToolTip(row.VersionBox, row.VersionBox.Text); };
            SoftField versionField = new SoftField(row.VersionBox, false);
            versionField.Dock = DockStyle.Fill;
            table.Controls.Add(versionField, 5, index);
            row.SizeLabel = new Label();
            row.SizeLabel.Name = "Size_" + type;
            row.SizeLabel.Text = "未选择";
            row.SizeLabel.Dock = DockStyle.Fill;
            row.SizeLabel.TextAlign = ContentAlignment.MiddleLeft;
            row.SizeLabel.ForeColor = UiColors.Muted;
            row.SizeLabel.Font = new Font("Microsoft YaHei UI", 8.3F, FontStyle.Regular);
            table.Controls.Add(row.SizeLabel, 6, index);
        }

        private void ShowHelp()
        {
            using (Form help = new Form())
            {
                help.Text = "操作说明"; help.StartPosition = FormStartPosition.CenterParent;
                help.Size = new Size(650, 520); help.MinimumSize = new Size(480, 360); help.BackColor = UiColors.Canvas;
                TextBox text = new TextBox { Name = "HelpText", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = UiColors.Canvas, Font = Font };
                text.Text = "文件与版本\r\n名称为文件名去掉 .bin；版本取下划线后的 V/v 至下一个下划线或点，保留大小写。名称/版本可修改，最多 96/64 个 UTF-8 字节。手动选入缺版本文件后须补填才能生成。\r\n\r\n一键加载\r\n仅扫描当前文件夹；按开头 PFC、LLC、M4、PLC、ESP32 识别 .bin，大小写不敏感。同类唯一且有效才更新；多文件、缺版本或无效项保留原选择并提示。一键清理不删除磁盘文件，不清 FTP 配置和输出位置。\r\n\r\n大小限制\r\n文件不可为空。PFC/LLC：256,000 字节（250 KiB）；M4：524,288 字节（512 KiB）；PLC：2,097,152 字节（2 MiB）；ESP32：2,621,440 字节（2.5 MiB）。整包最多 6,291,456 字节（6 MiB），含包头和对齐。加载和生成都会检查。\r\n\r\nFTP\r\n生成后默认上传该联合包，也可独立选任意本地文件。联合包上传前校验；普通文件不做包格式校验。普通 FTP 明文传输，服务器同名文件可能覆盖，上传前请核对。\r\n\r\n查看包头\r\n生成成功后可查看本次包最前 4096 字节；只读，不改变文件，也不读取后续固件内容。地址范围 0x0000～0x0FFF。";
                help.Padding = new Padding(20); help.Controls.Add(text);
                help.Shown += delegate { text.Select(0, 0); };
                help.ShowDialog(this);
            }
        }

        private byte[] ReadGeneratedHeader()
        {
            if (lastGeneratedHeader == null || String.IsNullOrEmpty(lastGeneratedPackage))
                throw new InvalidDataException("请先成功生成联合包。");
            byte[] current = HeaderViewer.ReadHeader(lastGeneratedPackage);
            for (int i = 0; i < current.Length; i++)
                if (current[i] != lastGeneratedHeader[i]) throw new InvalidDataException("文件包头已发生变化，请重新生成后查看。");
            return current;
        }

        private void ShowGeneratedHeader()
        {
            try { using (HeaderViewer viewer = new HeaderViewer(ReadGeneratedHeader())) viewer.ShowDialog(this); }
            catch (Exception ex)
            {
                ShowInputResult("查看包头失败：" + ex.Message, true);
                MessageBox.Show(this, ex.Message, "无法查看包头", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowInputResult(string message, bool error)
        {
            details.ForeColor = error ? Color.FromArgb(181, 50, 57) : UiColors.Ink;
            details.Text = message;
            details.SelectionStart = 0;
        }

        private void ChooseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择固件所在文件夹（仅扫描当前层）";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    Controls.Find("FolderPath", true)[0].Text = dialog.SelectedPath;
            }
        }

        private void LoadFolder(string folder)
        {
            string[] paths;
            try
            {
                if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    throw new InvalidDataException("请选择可读取的固件文件夹。");
                paths = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly);
                Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)) throw;
                ShowInputResult("加载失败：" + ex.Message, true); return;
            }
            Dictionary<FirmwareType, List<string>> groups = new Dictionary<FirmwareType, List<string>>();
            List<string> messages = new List<string>();
            bool error = false;
            foreach (string path in paths)
            {
                FirmwareType type; string name, version;
                if (!FirmwareFileName.TryParse(path, out type, out name, out version)) continue;
                if (!groups.ContainsKey(type)) groups[type] = new List<string>();
                groups[type].Add(path);
            }
            int loaded = 0;
            foreach (Row row in rows)
            {
                List<string> candidates;
                if (!groups.TryGetValue(row.Type, out candidates))
                { messages.Add(row.Type + "：未找到匹配文件，保留原选择。"); continue; }
                if (candidates.Count != 1)
                { error = true; messages.Add(row.Type + "：发现 " + candidates.Count + " 个同类文件，未自动选择，保留原选择。"); continue; }
                FirmwareType type; string name, version;
                FirmwareFileName.TryParse(candidates[0], out type, out name, out version);
                if (String.IsNullOrEmpty(version))
                { error = true; messages.Add(row.Type + "：文件名缺少版本，未加载，保留原选择。"); continue; }
                try { SetInputFile(row, candidates[0]); loaded++; messages.Add(row.Type + "：已加载 " + name); }
                catch (Exception ex)
                {
                    if (!(ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)) throw;
                    error = true; messages.Add(row.Type + "：未加载，保留原选择。" + ex.Message);
                }
            }
            ShowInputResult("加载完成：更新 " + loaded + " 项；未匹配或无效项保持不变。" + Environment.NewLine + String.Join(Environment.NewLine, messages.ToArray()), error);
        }

        private void ClearAllInputs()
        {
            changing = true;
            try
            {
                foreach (Row row in rows)
                { row.PathBox.Clear(); row.NameBox.Clear(); row.VersionBox.Clear(); row.AutoName = null; }
            }
            finally { changing = false; }
            RefreshPreview();
            ShowInputResult("已清空五类固件选择；磁盘文件、FTP 配置和输出位置均未改变。", false);
        }

        private void SelectInput(Row row)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 " + row.Type + " 固件";
                dialog.Filter = "固件文件 (*.bin)|*.bin|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { SetInputFile(row, dialog.FileName); }
                catch (Exception ex) { ShowInputResult(ex.Message, true); MessageBox.Show(this, ex.Message, "无法添加固件", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void SetInputFile(Row row, string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException(row.Type + " 固件文件不存在或不可读取。");
            if (!String.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择 .bin 固件文件。");
            FirmwareType detected; string name, version;
            if (!FirmwareFileName.TryParse(path, out detected, out name, out version))
            {
                name = Path.GetFileNameWithoutExtension(path);
                version = FirmwareFileName.VersionFromName(name);
            }
            else if (detected != row.Type)
                throw new InvalidDataException("文件名类型为 " + detected + "，不能加载到 " + row.Type + " 栏。");
            // Reuse the package's UTF-8, empty-file and size protections before changing UI state.
            // Missing manual versions remain genuinely empty and must be filled before generating.
            Packager.Plan(new FirmwareInput[] { new FirmwareInput { Type = row.Type, Path = path, Name = name, Version = String.IsNullOrEmpty(version) ? "待填写" : version } });
            changing = true;
            try
            {
                row.PathBox.Text = path;
                row.NameBox.Text = name;
                row.VersionBox.Text = version;
                row.AutoName = row.NameBox.Text;
            }
            finally { changing = false; }
            RefreshPreview();
        }

        private static string[] DraggedFiles(DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            return e.Data.GetData(DataFormats.FileDrop) as string[];
        }

        private static void InputDragEnter(object sender, DragEventArgs e)
        {
            string[] files = DraggedFiles(e);
            e.Effect = files != null && files.Length == 1 && File.Exists(files[0])
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void InputDragDrop(Row row, DragEventArgs e)
        {
            string[] files = DraggedFiles(e);
            if (files == null || files.Length != 1 || !File.Exists(files[0]))
            {
                MessageBox.Show(this, "请向每个固件输入框拖入一个可读取的文件。", "无法添加固件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { SetInputFile(row, files[0]); }
            catch (Exception ex) { ShowInputResult(ex.Message, true); MessageBox.Show(this, ex.Message, "无法添加固件", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void InputPathChanged(Row row)
        {
            if (changing) return;
            if (File.Exists(row.PathBox.Text) && (String.IsNullOrEmpty(row.NameBox.Text) || row.NameBox.Text == row.AutoName))
            {
                changing = true;
                try
                {
                    row.NameBox.Text = Path.GetFileName(row.PathBox.Text);
                    row.AutoName = row.NameBox.Text;
                }
                finally { changing = false; }
            }
            RefreshPreview();
        }

        private void ClearInput(Row row)
        {
            changing = true;
            row.PathBox.Clear();
            row.NameBox.Clear();
            row.VersionBox.Clear();
            row.AutoName = null;
            changing = false;
            RefreshPreview();
        }

        private void InputChanged(object sender, EventArgs e)
        {
            if (!changing) RefreshPreview();
        }

        private List<FirmwareInput> Inputs()
        {
            List<FirmwareInput> inputs = new List<FirmwareInput>();
            foreach (Row row in rows)
            {
                if (String.IsNullOrWhiteSpace(row.PathBox.Text)) continue;
                inputs.Add(new FirmwareInput { Type = row.Type, Path = row.PathBox.Text, Name = row.NameBox.Text, Version = row.VersionBox.Text });
            }
            return inputs;
        }

        private void RefreshPreview()
        {
            if (workspace == null || previewCard == null) return;
            List<FirmwareInput> selected = Inputs();
            grid.Rows.Clear();
            PackageReport plan = null;
            string problem = null;
            try { if (selected.Count != 0) plan = Packager.Plan(selected); }
            catch (Exception ex) { problem = ex.Message; }
            foreach (Row row in rows)
            {
                if (String.IsNullOrWhiteSpace(row.PathBox.Text)) { row.SizeLabel.Text = "未选择"; continue; }
                string size;
                try { long bytes = new FileInfo(row.PathBox.Text).Length; size = bytes.ToString("N0") + " B"; }
                catch { size = "不可读取"; }
                row.SizeLabel.Text = size;
                string offset = "待校验";
                if (plan != null) foreach (FirmwareEntry entry in plan.Entries) if (entry.Type == row.Type) { offset = "0x" + entry.Offset.ToString("X8"); break; }
                grid.Rows.Add(row.Type, row.PathBox.Text, "大小：" + size, "偏移：" + offset);
            }
            previewCard.Visible = grid.Rows.Count > 0;
            int rowPixels = 0; foreach (DataGridViewRow visibleRow in grid.Rows) rowPixels += visibleRow.Height;
            workspace.RowStyles[1].Height = grid.Rows.Count == 0 ? 0 : previewCard.Margin.Vertical + previewCard.Padding.Vertical + rowPixels + SystemInformation.HorizontalScrollBarHeight + 6;
            status.Text = problem == null ? "" : "未就绪：" + problem;
            status.ForeColor = problem == null ? UiColors.Muted : Color.FromArgb(181, 50, 57);
            if (plan != null && plan.Warnings.Count > 0) status.Text = String.Join("\r\n", plan.Warnings.ToArray());
            // Validation remains visible without a permanent overview panel.
            if (!String.IsNullOrEmpty(status.Text)) { details.ForeColor = status.ForeColor; details.Text = status.Text; }
            else if (!String.IsNullOrEmpty(lastPreviewMessage) && details.Text == lastPreviewMessage) { details.Text = ""; details.ForeColor = UiColors.Ink; }
            lastPreviewMessage = status.Text;
            workspace.PerformLayout();
        }

        private void BrowseOutput(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "保存联合固件包";
                dialog.Filter = "联合固件包 (*.bin)|*.bin|所有文件 (*.*)|*.*";
                dialog.DefaultExt = "bin";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = false;
                if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.FileName;
            }
        }

        private bool ApplyFtpAddress(string raw, bool showErrors)
        {
            try
            {
                FtpLocation location = FtpUploader.ParseAddress(raw);
                string authority = location.DirectoryUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
                if (location.HasCredentials)
                {
                    ftpUser.Text = location.Username;
                    ftpPassword.Text = location.Password;
                }
                else if (ftpAuthority != null && !String.Equals(ftpAuthority, authority, StringComparison.OrdinalIgnoreCase))
                {
                    ftpUser.Clear();
                    ftpPassword.Clear();
                }
                ftpAuthority = authority;
                ftpAddress.Text = location.DirectoryUri.AbsoluteUri;
                tooltips.SetToolTip(ftpAddress, ftpAddress.Text);
                return true;
            }
            catch (InvalidDataException ex)
            {
                if (raw != null && raw.IndexOf('@') >= 0) ftpAddress.Clear();
                ftpStatus.ForeColor = Color.FromArgb(181, 50, 57);
                ftpStatus.Text = ex.Message;
                if (showErrors) MessageBox.Show(this, ex.Message, "FTP 地址无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void FtpUploadClicked(object sender, EventArgs e)
        {
            if (ftpBusy || !ApplyFtpAddress(ftpAddress.Text, true)) return;
            try
            {
                if (String.IsNullOrWhiteSpace(ftpFile.Text) || !File.Exists(ftpFile.Text))
                    throw new InvalidDataException("请先生成联合包，或选择要上传的本地文件。");
                if (String.IsNullOrWhiteSpace(ftpUser.Text)) throw new InvalidDataException("请输入 FTP 用户名。");
                FtpLocation location = FtpUploader.ParseAddress(ftpAddress.Text);
                Uri destination = FtpUploader.FileUri(location, Path.GetFileName(ftpFile.Text));
                DialogResult answer = MessageBox.Show(this,
                    "本地文件：\n" + ftpFile.Text + "\n\n" + (ftpPackageSource ? "联合包：上传前校验。" : "普通文件：直接上传，不进行联合包校验。") + "\n\n即将上传至：\n" + destination.AbsoluteUri +
                    "\n\n普通 FTP 明文传输账号、密码和文件；服务器同名文件可能被覆盖。确定上传？",
                    "确认 FTP 上传", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
                StartFtpTransfer(ftpFile.Text, ftpAddress.Text, ftpUser.Text, ftpPassword.Text, ftpPackageSource);
            }
            catch (InvalidDataException ex)
            {
                ftpStatus.ForeColor = Color.FromArgb(181, 50, 57);
                sounds.Play("failure");
                ftpStatus.Text = "上传失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "上传失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartFtpUpload(string path, string address, string username, string password)
        {
            StartFtpTransfer(path, address, username, password, true);
        }

        private void StartFtpTransfer(string path, string address, string username, string password, bool package)
        {
            SetFtpBusy(true);
            ftpStatus.ForeColor = UiColors.Blue;
            ftpStatus.Text = (package ? "正在校验联合包并上传：" : "正在上传本地文件：") + Path.GetFileName(path);
            ThreadPool.QueueUserWorkItem(delegate
            {
                FtpUploadResult result = null;
                string error = null;
                try { result = package ? FtpUploader.Upload(path, address, username, password) : FtpUploader.UploadFile(path, address, username, password); }
                catch (InvalidDataException ex) { error = ex.Message; }
                catch (Exception) { error = "读取文件或连接 FTP 时发生错误，请检查文件、网络和服务器状态。"; }
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (IsDisposed) return;
                        SetFtpBusy(false);
                        if (result != null)
                        {
                            sounds.Play("success");
                            ftpStatus.ForeColor = Color.FromArgb(29, 120, 88);
                            ftpStatus.Text = "上传成功：" + Path.GetFileName(path) + "，FTP 服务器已确认接收（" + result.StatusCode + "），共 " + result.Bytes.ToString("N0") + " 字节。";
                            tooltips.SetToolTip(ftpStatus, result.RemoteUrl);
                        }
                        else
                        {
                            ftpStatus.ForeColor = Color.FromArgb(181, 50, 57);
                            sounds.Play("failure");
                            ftpStatus.Text = "上传失败：" + Path.GetFileName(path) + "，" + error + " 服务器上可能留有不完整文件。";
                            tooltips.SetToolTip(ftpStatus, ftpStatus.Text);
                        }
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private void SetFtpBusy(bool busy)
        {
            ftpBusy = busy;
            ftpUpload.Enabled = !busy;
            ftpBrowseFile.Enabled = !busy;
            ftpFile.Enabled = !busy;

            ftpAddress.Enabled = !busy;
            ftpUser.Enabled = !busy;
            ftpPassword.Enabled = !busy;
            build.Enabled = !busy;
        }

        private void FtpFormClosing(object sender, FormClosingEventArgs e)
        {
            if (ftpBusy && MessageBox.Show(this, "上传仍在进行。关闭可能中断传输，并在服务器上留下不完整文件。确定关闭？",
                "上传进行中", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                e.Cancel = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tooltips.Dispose(); sounds.Dispose(); }
            base.Dispose(disposing);
        }

        private void BuildClicked(object sender, EventArgs e)
        {
            try
            {
                Packager.Plan(Inputs());
                RefreshPreview();
                if (String.IsNullOrWhiteSpace(output.Text)) throw new InvalidDataException("请选择联合包输出位置。");
                if (File.Exists(output.Text))
                {
                    DialogResult answer = MessageBox.Show(this, "输出文件已存在。确认用新联合包替换？\n" + output.Text,
                        "确认替换", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes) return;
                }
                PackageReport report = Packager.Build(Inputs(), output.Text);
                string generatedPath = Path.GetFullPath(output.Text);
                byte[] generatedHeader = HeaderViewer.ReadHeader(generatedPath);
                lastGeneratedPackage = generatedPath;
                lastGeneratedHeader = generatedHeader;
                headerView.Enabled = true;
                SetFtpFile(lastGeneratedPackage, true);
                ShowReport("打包并校验成功", lastGeneratedPackage, report);
            }
            catch (Exception ex)
            {
                sounds.Play("failure");
                details.ForeColor = Color.FromArgb(181, 50, 57);
                details.Text = "打包失败：" + ex.Message;
                MessageBox.Show(this, details.Text, "打包失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenClicked(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "打开并校验联合固件包";
                dialog.Filter = "联合固件包 (*.bin)|*.bin|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { DisplayVerifiedFile(dialog.FileName); }
                catch (Exception ex)
                {
                    sounds.Play("failure");
                    details.ForeColor = Color.FromArgb(181, 50, 57);
                    details.Text = "校验失败：" + ex.Message;
                    MessageBox.Show(this, details.Text, "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        internal void DisplayVerifiedFile(string path)
        {
            PackageReport report = Packager.VerifyFile(path);
            ShowReport("校验通过", Path.GetFullPath(path), report);
        }

        private void ShowReport(string result, string path, PackageReport report)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(result);
            text.AppendLine("文件：" + path);
            text.AppendFormat("联合包大小：{0:N0} 字节；固件数量：{1}\r\n", report.TotalSize, report.Entries.Count);
            foreach (FirmwareEntry entry in report.Entries)
            {
                text.AppendFormat("{0}  {1}  {2}  大小：{3:N0} 字节\r\n", entry.Type, entry.Name, entry.Version, entry.Size);
            }
            foreach (string warning in report.Warnings) text.AppendLine("\r\n警告：" + warning);
            sounds.Play("success");
            details.ForeColor = UiColors.Ink;
            details.Text = text.ToString();
            details.SelectionStart = 0;
            details.ScrollToCaret();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}

