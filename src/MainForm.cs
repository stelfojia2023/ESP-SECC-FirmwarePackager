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
                using (Brush fill = new SolidBrush(Color.White)) e.Graphics.FillPath(fill, path);
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
            BackColor = Color.White;
            Padding = multiline ? new Padding(9, 7, 9, 7) : new Padding(8, 5, 8, 3);
            Margin = new Padding(3, 3, 3, 3);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            input.BorderStyle = BorderStyle.None;
            input.BackColor = Color.White;
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
        private readonly Label overview = new Label();
        private Button ftpToggle;
        private Form ftpDialog;
        private Button ftpBrowseFile;
        private LinkLabel ftpUsePackage;
        private readonly TextBox ftpFile = new TextBox();
        private readonly Label ftpSourceKind = new Label();
        private string lastGeneratedPackage;
        private bool ftpPackageSource = true;
        private string ftpAuthority;
        private bool ftpBusy;
        private bool changing;

        public MainForm()
        {
            Text = "固件联合打包工具 · V0.0.1";
            Name = "MainForm";
            MinimumSize = new Size(1100, 720);
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Max(1100, Math.Min(1240, workArea.Width - 32)),
                Math.Max(720, Math.Min(850, workArea.Height - 24)));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiColors.Canvas;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            Panel scroll = new Panel { Name = "ContentScroll", Dock = DockStyle.Fill, AutoScroll = true };
            Controls.Add(scroll);
            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 5,
                Padding = new Padding(22, 12, 22, 16), Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 560));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            scroll.Controls.Add(body);

            SectionCard inputsCard = new SectionCard { Name = "InputsCard", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 12) };
            body.Controls.Add(inputsCard, 0, 1);
            TableLayoutPanel inputs = CardLayout(6);
            inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            for (int i = 0; i < 5; i++) inputs.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            inputsCard.Controls.Add(inputs);
            inputs.Controls.Add(TitleLabel("固件文件"), 0, 0);
            FirmwareType[] types = { FirmwareType.PFC, FirmwareType.LLC, FirmwareType.M4, FirmwareType.PLC, FirmwareType.ESP32 };
            for (int i = 0; i < types.Length; i++)
            {
                // Keep the original input controls and handlers; only change their layout.
                TableLayoutPanel staging = new TableLayoutPanel { ColumnCount = 7, RowCount = 2 };
                AddRow(staging, types[i], 1);
                TableLayoutPanel item = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2,
                    Margin = new Padding(0, 4, 0, 5), BackColor = Color.White };
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
                item.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); item.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                Control[] controls = new Control[7];
                for (int c = 0; c < 7; c++) controls[c] = staging.GetControlFromPosition(c, 1);
                item.Controls.Add(controls[0], 0, 0);
                item.Controls.Add(controls[1], 1, 0); item.SetColumnSpan(controls[1], 2);
                item.Controls.Add(controls[2], 3, 0); item.Controls.Add(controls[3], 4, 0);
                Label metadata = FieldLabel("名称 / 版本"); metadata.Font = new Font(Font.FontFamily, 7.8F);
                item.Controls.Add(metadata, 0, 1);
                item.Controls.Add(controls[4], 1, 1); item.Controls.Add(controls[5], 2, 1);
                item.Controls.Add(controls[6], 3, 1); item.SetColumnSpan(controls[6], 2);
                tooltips.SetToolTip(rows[i].PathBox, "粘贴路径或拖入一个 BIN 文件");
                rows[i].PathBox.AccessibleName = types[i] + " 固件文件路径，可拖入文件";
                rows[i].NameBox.AccessibleName = types[i] + " 固件名称";
                rows[i].VersionBox.AccessibleName = types[i] + " 固件版本，手动填写";
                staging.Dispose();
                inputs.Controls.Add(item, 0, i + 1);
            }

            SectionCard resultCard = new SectionCard { Name = "ResultCard", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
            body.Controls.Add(resultCard, 1, 1);
            TableLayoutPanel result = CardLayout(8);
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            result.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            resultCard.Controls.Add(result);
            result.Controls.Add(TitleLabel("打包概览"), 0, 0);
            overview.Name = "PackageOverview"; overview.Dock = DockStyle.Fill;
            overview.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold); overview.ForeColor = UiColors.Ink;
            result.Controls.Add(overview, 0, 1);
            status.Name = "PreviewStatus"; status.Dock = DockStyle.Fill;
            status.Multiline = true; status.ReadOnly = true; status.ScrollBars = ScrollBars.Vertical;
            status.BorderStyle = BorderStyle.None; status.BackColor = Color.White; status.Font = new Font(Font.FontFamily, 8.5F);
            result.Controls.Add(status, 0, 2);
            result.Controls.Add(FieldLabel("输出 BIN"), 0, 3);
            TableLayoutPanel outputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            outputRow.RowCount = 1; outputRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            output.Name = "OutputPath"; output.AccessibleName = "输出 BIN 文件路径";
            output.TextChanged += delegate { tooltips.SetToolTip(output, output.Text); };
            SoftField outputField = new SoftField(output, false) { Dock = DockStyle.Fill };
            outputRow.Controls.Add(outputField, 0, 0);
            Button browse = new RaisedButton { Name = "BrowseOutput", Text = "选择位置", Dock = DockStyle.Fill };
            StyleButton(browse, false, false); browse.Click += BrowseOutput; outputRow.Controls.Add(browse, 1, 0);
            result.Controls.Add(outputRow, 0, 4);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            actions.RowCount = 1; actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            build.Name = "BuildButton"; build.Text = "生成联合包"; build.Dock = DockStyle.Fill;
            StyleButton(build, true, false); build.Click += BuildClicked; actions.Controls.Add(build, 0, 0);
            Button open = new RaisedButton { Name = "OpenVerifyButton", Text = "打开并校验", Dock = DockStyle.Fill };
            StyleButton(open, false, false); open.Click += OpenClicked; actions.Controls.Add(open, 1, 0);
            result.Controls.Add(actions, 0, 5);
            result.Controls.Add(FieldLabel("操作结果"), 0, 6);
            details.Name = "ResultDetails"; details.Multiline = true; details.ReadOnly = true;
            details.ScrollBars = ScrollBars.Both; details.WordWrap = false; details.Text = "尚无操作记录";
            result.Controls.Add(new SoftField(details, true) { Dock = DockStyle.Fill }, 0, 7);

            grid.Name = "PreviewGrid"; grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true; grid.RowHeadersVisible = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Color.White; grid.GridColor = UiColors.Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = UiColors.Canvas; grid.ColumnHeadersDefaultCellStyle.ForeColor = UiColors.Muted;
            grid.DefaultCellStyle.SelectionBackColor = UiColors.SoftBlue; grid.DefaultCellStyle.SelectionForeColor = UiColors.Ink;
            grid.RowTemplate.Height = 28;
            grid.Columns.Add("Type", "类型"); grid.Columns.Add("Name", "名称"); grid.Columns.Add("Version", "版本");
            grid.Columns.Add("Size", "原始大小（字节）"); grid.Columns.Add("Offset", "包内偏移");
            grid.Columns[0].FillWeight = 55; grid.Columns[1].FillWeight = 160;
            TableLayoutPanel preview = CardLayout(1); preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            preview.Controls.Add(grid, 0, 0);
            AddDisclosure(body, 2, "包内容与偏移", preview, 290, "PreviewToggle");

            TableLayoutPanel ftp = CardLayout(6);
            ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); ftp.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TableLayoutPanel addressRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            addressRow.RowCount = 1; addressRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76)); addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); addressRow.Controls.Add(FieldLabel("FTP 地址"), 0, 0);
            ftpAddress.Name = "FtpAddress"; ftpAddress.AccessibleName = "FTP 地址";
            ftpAddress.PasteAddress = delegate(string value) { ApplyFtpAddress(value, true); };
            ftpAddress.Leave += delegate { if (!String.IsNullOrWhiteSpace(ftpAddress.Text)) ApplyFtpAddress(ftpAddress.Text, false); };
            ftpAddress.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyFtpAddress(ftpAddress.Text, true); } };
            addressRow.Controls.Add(new SoftField(ftpAddress, false) { Dock = DockStyle.Fill }, 1, 0);
            ftpUpload.Name = "FtpUploadButton"; ftpUpload.Text = "上传到 FTP"; ftpUpload.Dock = DockStyle.Fill;
            StyleButton(ftpUpload, true, false); ftpUpload.Click += FtpUploadClicked; addressRow.Controls.Add(ftpUpload, 2, 0);
            ftp.Controls.Add(addressRow, 0, 2);
            TableLayoutPanel credentials = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = Padding.Empty };
            credentials.RowCount = 1; credentials.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76)); credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64)); credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            credentials.Controls.Add(FieldLabel("用户名"), 0, 0); ftpUser.Name = "FtpUser"; ftpUser.AccessibleName = "FTP 用户名";
            credentials.Controls.Add(new SoftField(ftpUser, false) { Dock = DockStyle.Fill }, 1, 0);
            credentials.Controls.Add(FieldLabel("密码"), 2, 0); ftpPassword.Name = "FtpPassword"; ftpPassword.UseSystemPasswordChar = true;
            ftpPassword.AccessibleName = "FTP 密码"; credentials.Controls.Add(new SoftField(ftpPassword, false) { Dock = DockStyle.Fill }, 3, 0);
            ftp.Controls.Add(credentials, 0, 3);
            ftp.Controls.Add(FieldLabel("普通 FTP 明文传输；同名文件可能覆盖。密码只保留在当前窗口。"), 0, 4);
            ftpStatus.Name = "FtpStatus"; ftpStatus.Dock = DockStyle.Fill; ftpStatus.ForeColor = UiColors.Muted;
            ftpStatus.Text = "生成后默认上传联合包；也可独立选择本地文件。"; ftp.Controls.Add(ftpStatus, 0, 5);
            TableLayoutPanel fileRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            fileRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            fileRow.Controls.Add(FieldLabel("上传文件"), 0, 0);
            ftpFile.Name = "FtpFilePath"; ftpFile.ReadOnly = true; ftpFile.AllowDrop = true;
            ftpFile.DragEnter += InputDragEnter;
            ftpFile.DragDrop += delegate(object sender, DragEventArgs e)
            {
                string[] files = DraggedFiles(e);
                if (!ftpBusy && files != null && files.Length == 1 && File.Exists(files[0])) SetFtpFile(files[0], false);
            }; ftpFile.AccessibleName = "FTP 上传的本地文件";
            ftpFile.TextChanged += delegate { tooltips.SetToolTip(ftpFile, ftpFile.Text); };
            fileRow.Controls.Add(new SoftField(ftpFile, false) { Dock = DockStyle.Fill }, 1, 0);
            Button chooseFile = new RaisedButton { Name = "FtpBrowseFile", Text = "选择本地文件", Dock = DockStyle.Fill };
            ftpBrowseFile = chooseFile;
            StyleButton(chooseFile, false, false);
            chooseFile.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog { Title = "选择 FTP 上传文件", Filter = "所有文件 (*.*)|*.*", CheckFileExists = true })
                    if (dialog.ShowDialog(ftpDialog) == DialogResult.OK) SetFtpFile(dialog.FileName, false);
            };
            fileRow.Controls.Add(chooseFile, 2, 0); ftp.Controls.Add(fileRow, 0, 0);
            TableLayoutPanel sourceRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            sourceRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            ftpSourceKind.Name = "FtpSourceKind"; ftpSourceKind.Dock = DockStyle.Fill; ftpSourceKind.ForeColor = UiColors.Muted;
            ftpSourceKind.Text = "拖入文件，或浏览选择本地文件"; sourceRow.Controls.Add(ftpSourceKind, 0, 0);
            LinkLabel usePackage = new LinkLabel { Name = "FtpUsePackage", Text = "使用最近生成的联合包", Dock = DockStyle.Fill, LinkColor = UiColors.Blue };
            ftpUsePackage = usePackage;
            usePackage.LinkClicked += delegate
            {
                if (!String.IsNullOrEmpty(lastGeneratedPackage)) SetFtpFile(lastGeneratedPackage, true);
                else { ftpStatus.Text = "还没有生成联合包；可直接选择本地文件上传。"; ftpStatus.ForeColor = UiColors.Muted; }
            };
            sourceRow.Controls.Add(usePackage, 1, 0); ftp.Controls.Add(sourceRow, 0, 1);
            ftpDialog = new Form { Name = "FtpDialog", Text = "FTP 上传", Size = new Size(820, 400),
                MinimumSize = new Size(720, 370), StartPosition = FormStartPosition.CenterParent,
                Font = Font, AutoScaleMode = AutoScaleMode.Dpi, BackColor = UiColors.Canvas,
                ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false };
            SectionCard dialogCard = new SectionCard { Dock = DockStyle.Fill, Margin = Padding.Empty };
            ftpDialog.Padding = new Padding(16); ftpDialog.Controls.Add(dialogCard); dialogCard.Controls.Add(ftp);
            ftpDialog.FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (ftpBusy)
                {
                    e.Cancel = true;
                    MessageBox.Show(ftpDialog, "上传仍在进行，请等待结果后关闭。", "上传进行中", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            ftpToggle = new RaisedButton { Name = "FtpToggle", Text = "FTP 上传", Width = 160, Height = 40,
                Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(2, 6, 0, 4) };
            StyleButton(ftpToggle, false, false);
            ftpToggle.Click += delegate { ftpDialog.ShowDialog(this); };
            body.Controls.Add(ftpToggle, 0, 3); body.SetColumnSpan(ftpToggle, 2);
            ftpStatus.TextChanged += delegate { UpdateFtpCaption(); };
            FormClosing += FtpFormClosing;

            LinkLabel limits = new LinkLabel { Text = "设备使用注意", Dock = DockStyle.Fill,
                LinkColor = UiColors.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(4, 0, 0, 0) };
            limits.LinkClicked += delegate { MessageBox.Show(this,
                "当前设备尚未接入联合包解析。6 MiB 暂存区与 2.5 MiB ESP32 槽为候选方案，并非已部署的设备配置。\n\n" +
                "上传成功不代表设备已完成升级；使用前请确认目标设备支持该文件。",
                "设备使用注意", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            body.Controls.Add(limits, 0, 4); body.SetColumnSpan(limits, 2);
            RefreshPreview();
        }

        private static Label TitleLabel(string title)
        {
            Label label = FieldLabel(title); label.ForeColor = UiColors.Ink;
            label.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold); return label;
        }

        private void AddDisclosure(TableLayoutPanel body, int row, string title, Control content, int expandedHeight, string name)
        {
            SectionCard card = new SectionCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(16, 5, 18, 10) };
            TableLayoutPanel layout = CardLayout(2); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.Controls.Add(layout);
            Button toggle = new RaisedButton { Name = name, Text = "›  " + title, Dock = DockStyle.None, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left, Width = 210, Height = 32, TextAlign = ContentAlignment.MiddleLeft };
            StyleButton(toggle, false, true); layout.Controls.Add(toggle, 0, 0); layout.Controls.Add(content, 0, 1); content.Visible = false;
            toggle.Click += delegate
            {
                bool show = !content.Visible; content.Visible = show; toggle.Text = (show ? "⌄  " : "›  ") + title;
                float scale = layout.RowStyles[0].Height / 32F;
                body.RowStyles[row].Height = (show ? expandedHeight : 56) * scale;
                card.PerformLayout(); body.PerformLayout();
            };
            body.Controls.Add(card, 0, row); body.SetColumnSpan(card, 2);
        }

        private void SetFtpFile(string path, bool package)
        {
            ftpFile.Text = Path.GetFullPath(path);
            ftpPackageSource = package;
            ftpSourceKind.Text = package ? "联合包 · 上传前校验" : "本地文件 · 直接上传，不进行联合包校验";
        }

        private void UpdateFtpCaption()
        {
            if (ftpToggle == null) return;
            string state = ftpBusy ? " · 上传中" : ftpStatus.Text.StartsWith("上传成功") ? " · 成功" :
                ftpStatus.Text.StartsWith("上传失败") ? " · 失败" : "";
            ftpToggle.Text = "FTP 上传" + state;
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
            layout.BackColor = Color.White;
            layout.ColumnCount = 1;
            layout.RowCount = rows;
            return layout;
        }

        private static void StyleTextBox(TextBox box, bool readOnly)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = readOnly ? Color.FromArgb(245, 249, 254) : Color.White;
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
            button.Click += delegate { sounds.Play("click"); };
        }

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
            row.NameBox = new TextBox();
            row.NameBox.Name = "Name_" + type;
            StyleTextBox(row.NameBox, false);
            row.NameBox.TextChanged += InputChanged;
            row.NameBox.TextChanged += delegate { tooltips.SetToolTip(row.NameBox, row.NameBox.Text); };
            SoftField nameField = new SoftField(row.NameBox, false);
            nameField.Dock = DockStyle.Fill;
            table.Controls.Add(nameField, 4, index);
            row.VersionBox = new TextBox();
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
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法添加固件", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void SetInputFile(Row row, string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException(row.Type + " 固件文件不存在或不可读取。");
            changing = true;
            try
            {
                row.PathBox.Text = path;
                row.NameBox.Text = Path.GetFileName(path);
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
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法添加固件", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
            foreach (Row row in rows)
            {
                if (String.IsNullOrWhiteSpace(row.PathBox.Text)) row.SizeLabel.Text = "未选择";
                else
                {
                    try { row.SizeLabel.Text = new FileInfo(row.PathBox.Text).Length.ToString("N0") + " B"; }
                    catch { row.SizeLabel.Text = "不可读取"; }
                }
            }
            overview.Text = "已选择 " + Inputs().Count + " / 5 类固件";
            grid.Rows.Clear();
            try
            {
                PackageReport plan = Packager.Plan(Inputs());
                foreach (FirmwareEntry entry in plan.Entries)
                    grid.Rows.Add(entry.Type, entry.Name, entry.Version, entry.Size.ToString("N0"), "0x" + entry.Offset.ToString("X6"));
                StringBuilder text = new StringBuilder();
                text.AppendFormat("预计联合包：{0:N0} 字节。", plan.TotalSize);
                foreach (string warning in plan.Warnings) text.Append("  警告：").Append(warning);
                status.Text = text.ToString();
                overview.Text = plan.Entries.Count + " 类固件  ·  " + (plan.TotalSize / 1024.0).ToString("N1") + " KiB";
                status.ForeColor = plan.Warnings.Count == 0 ? Color.FromArgb(29, 120, 88) : Color.FromArgb(166, 94, 14);
            }
            catch (Exception e)
            {
                status.Text = Inputs().Count == 0 ? "选择固件后显示包大小与检查结果。" : "未就绪：" + e.Message;
                status.ForeColor = Inputs().Count == 0 ? UiColors.Muted : Color.FromArgb(181, 50, 57);
            }
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
                ftpStatus.ForeColor = UiColors.Muted;
                ftpStatus.Text = "已识别 FTP 服务器、端口和目录；上传前将确认目标文件。";
                return true;
            }
            catch (InvalidDataException ex)
            {
                if (raw != null && raw.IndexOf('@') >= 0) ftpAddress.Clear();
                ftpStatus.ForeColor = Color.FromArgb(181, 50, 57);
                ftpStatus.Text = ex.Message;
                if (showErrors) MessageBox.Show(ftpDialog, ex.Message, "FTP 地址无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                DialogResult answer = MessageBox.Show(ftpDialog,
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
                MessageBox.Show(ftpDialog, ex.Message, "上传失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            ftpUsePackage.Enabled = !busy;
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
            if (disposing) { if (ftpDialog != null) ftpDialog.Dispose(); tooltips.Dispose(); sounds.Dispose(); }
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
                lastGeneratedPackage = Path.GetFullPath(output.Text);
                SetFtpFile(lastGeneratedPackage, true);
                ShowReport("打包并校验成功", lastGeneratedPackage, report);
            }
            catch (Exception ex)
            {
                sounds.Play("failure");
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
                text.AppendFormat("\r\n{0}  名称：{1}  版本：{2}\r\n", entry.Type, entry.Name, entry.Version);
                text.AppendFormat("  大小：{0:N0} 字节\r\n", entry.Size);
            }
            foreach (string warning in report.Warnings) text.AppendLine("\r\n警告：" + warning);
            sounds.Play("success");
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

