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
        public static readonly Color Canvas = Color.FromArgb(245, 246, 249);
        public static readonly Color Ink = Color.FromArgb(30, 35, 43);
        public static readonly Color Muted = Color.FromArgb(103, 112, 126);
        public static readonly Color Blue = Color.FromArgb(29, 100, 195);
        public static readonly Color SoftBlue = Color.FromArgb(235, 244, 255);
        public static readonly Color Border = Color.FromArgb(228, 233, 241);
    }

    internal sealed class BrandHeader : Control
    {
        private readonly Image mascot;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PrivateExtractIconsW")]
        private static extern uint PrivateExtractIcons(string file, int index, int width, int height, IntPtr[] icons, uint[] ids, uint count, uint flags);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        public BrandHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "领充 LINCHR 固件联合打包工具";
            try { mascot = LoadMascot(); }
            catch (Exception) { mascot = null; }
        }

        private static Image LoadMascot()
        {
            IntPtr[] handles = new IntPtr[1];
            uint[] ids = new uint[1];
            uint count = PrivateExtractIcons(Application.ExecutablePath, 0, 72, 72, handles, ids, 1, 0);
            if (count > 0 && handles[0] != IntPtr.Zero)
            {
                try { using (Icon icon = Icon.FromHandle(handles[0])) return icon.ToBitmap(); }
                finally { DestroyIcon(handles[0]); }
            }
            using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                return icon == null ? null : icon.ToBitmap();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && mascot != null) mascot.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush background = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(18, 61, 122), Color.FromArgb(45, 130, 205), LinearGradientMode.Horizontal))
                g.FillRectangle(background, ClientRectangle);
            using (Pen trace = new Pen(Color.FromArgb(32, 255, 255, 255), 1.2F))
            {
                int right = ClientSize.Width;
                g.DrawArc(trace, right - 400, -170, 470, 300, 16, 164);
                g.DrawArc(trace, right - 335, -145, 380, 255, 18, 160);
                g.DrawLine(trace, right - 175, 32, right - 65, 32);
                g.DrawLine(trace, right - 150, 39, right - 40, 39);
                g.DrawEllipse(trace, right - 49, 66, 6, 6);
            }
            using (Brush orange = new SolidBrush(Color.FromArgb(255, 100, 47)))
            using (Brush green = new SolidBrush(Color.FromArgb(147, 231, 45)))
            {
                g.FillRectangle(orange, 25, 21, 76, 5);
                Point[] wedge = { new Point(101, 26), new Point(108, 21), new Point(127, 21), new Point(127, 26) };
                g.FillPolygon(green, wedge);
            }
            using (Font logo = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold))
            using (Font chinese = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold))
            using (Font micro = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular))
            using (Font title = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold))
            using (Font small = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, "LINCHR", logo, new Point(21, 31), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "领充", chinese, new Point(140, 38), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "以领芯科技  做可靠伙伴", micro, new Point(25, 69), Color.FromArgb(203, 224, 247), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                using (Pen divider = new Pen(Color.FromArgb(80, 255, 255, 255))) g.DrawLine(divider, 222, 25, 222, 87);
                TextRenderer.DrawText(g, "固件联合打包工具", title, new Point(246, 23), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "FIRMWARE BUNDLE PACKAGER  ·  打包、校验与 FTP 上传", small, new Point(249, 69), Color.FromArgb(216, 234, 252), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (ClientSize.Width > 940)
                {
                    TextRenderer.DrawText(g, "FORMAT V1  /  待评审", small, new Point(ClientSize.Width - 270, 74), Color.FromArgb(218, 236, 253), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    if (mascot != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(mascot, new Rectangle(ClientSize.Width - 100, 17, 72, 72));
                    }
                }
            }
        }
    }

    internal sealed class SectionCard : Panel
    {
        public SectionCard()
        {
            BackColor = UiColors.Canvas;
            Padding = new Padding(16, 10, 16, 10);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (Width < 4 || Height < 4) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5F, 0.5F, Width - 2F, Height - 2F);
            const float d = 16F;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
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
                using (Pen border = new Pen(input.Focused ? Color.FromArgb(85, 150, 225) : UiColors.Border))
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
        private readonly Label status = new Label();
        private readonly Button build = new Button();
        private readonly FtpAddressBox ftpAddress = new FtpAddressBox();
        private readonly TextBox ftpUser = new TextBox();
        private readonly TextBox ftpPassword = new TextBox();
        private readonly Button ftpUpload = new Button();
        private readonly Label ftpStatus = new Label();
        private readonly ToolTip tooltips = new ToolTip();
        private string ftpAuthority;
        private bool ftpBusy;
        private bool changing;

        public MainForm()
        {
            Text = "ESP-SECC 固件联合打包工具 V1（待评审）";
            Name = "MainForm";
            MinimumSize = new Size(1000, 690);
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Max(1000, Math.Min(1280, workArea.Width - 48)),
                Math.Max(690, Math.Min(1040, workArea.Height - 32)));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiColors.Canvas;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            BrandHeader header = new BrandHeader();
            header.Name = "BrandHeader";
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            root.Controls.Add(header, 0, 0);

            Panel scroll = new Panel();
            scroll.Name = "ContentScroll";
            scroll.Dock = DockStyle.Fill;
            scroll.Margin = Padding.Empty;
            scroll.AutoScroll = true;
            scroll.BackColor = UiColors.Canvas;
            root.Controls.Add(scroll, 0, 1);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Top;
            body.Margin = Padding.Empty;
            body.Padding = new Padding(18, 8, 18, 8);
            body.BackColor = UiColors.Canvas;
            body.ColumnCount = 1;
            body.RowCount = 6;
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 255));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 242));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 165));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            body.Height = 965;
            scroll.Controls.Add(body);
            scroll.Resize += delegate { body.Height = Math.Max(965, scroll.ClientSize.Height); };

            Panel noticePanel = new Panel();
            noticePanel.Dock = DockStyle.Fill;
            noticePanel.Margin = new Padding(0, 0, 0, 8);
            noticePanel.BackColor = Color.FromArgb(229, 240, 254);
            Panel noticeAccent = new Panel();
            noticeAccent.Dock = DockStyle.Left;
            noticeAccent.Width = 4;
            noticeAccent.BackColor = Color.FromArgb(50, 136, 224);
            noticePanel.Controls.Add(noticeAccent);
            Label notice = new Label();
            notice.Dock = DockStyle.Fill;
            notice.Padding = new Padding(12, 0, 8, 0);
            notice.TextAlign = ContentAlignment.MiddleLeft;
            notice.ForeColor = Color.FromArgb(47, 86, 131);
            notice.Text = "格式 V1 尚在评审；当前设备尚未接入联合包解析。6 MiB 暂存区及 2.5 MiB ESP32 槽均为候选方案。";
            noticePanel.Controls.Add(notice);
            notice.BringToFront();
            body.Controls.Add(noticePanel, 0, 0);

            SectionCard inputsCard = new SectionCard();
            inputsCard.Dock = DockStyle.Fill;
            inputsCard.Margin = new Padding(0, 0, 0, 10);
            body.Controls.Add(inputsCard, 0, 1);
            TableLayoutPanel inputsLayout = CardLayout(2);
            inputsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            inputsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            inputsCard.Controls.Add(inputsLayout);
            inputsLayout.Controls.Add(SectionHeading("01", "选择固件", "将 BIN 拖入对应输入框，或点击浏览 · 最多选择 5 类"), 0, 0);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Name = "InputsTable";
            table.Dock = DockStyle.Fill;
            table.Margin = Padding.Empty;
            table.BackColor = Color.White;
            table.ColumnCount = 7;
            table.RowCount = 6;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            for (int i = 0; i < 5; i++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 198));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            string[] headings = { "固件类型", "固件文件（可拖入）", "", "", "固件名称", "版本（手填）", "原始大小" };
            for (int c = 0; c < headings.Length; c++)
            {
                Label label = new Label();
                label.Text = headings[c];
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.ForeColor = UiColors.Muted;
                label.Font = new Font("Microsoft YaHei UI", 8.3F, FontStyle.Regular);
                label.Margin = new Padding(4, 0, 0, 0);
                table.Controls.Add(label, c, 0);
            }
            FirmwareType[] types = { FirmwareType.PFC, FirmwareType.LLC, FirmwareType.M4, FirmwareType.PLC, FirmwareType.ESP32 };
            for (int i = 0; i < types.Length; i++) AddRow(table, types[i], i + 1);
            inputsLayout.Controls.Add(table, 0, 1);

            SectionCard previewCard = new SectionCard();
            previewCard.Dock = DockStyle.Fill;
            previewCard.Margin = new Padding(0, 0, 0, 10);
            body.Controls.Add(previewCard, 0, 2);
            TableLayoutPanel previewLayout = CardLayout(3);
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            previewCard.Controls.Add(previewLayout);
            previewLayout.Controls.Add(SectionHeading("02", "包内容预览", "偏移相对于联合 BIN 起点 · 大小为原始固件大小"), 0, 0);

            grid.Name = "PreviewGrid";
            grid.Dock = DockStyle.Fill;
            grid.Margin = new Padding(0, 1, 0, 1);
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Color.White;
            grid.GridColor = Color.FromArgb(235, 241, 248);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeight = 26;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 247, 253);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UiColors.Muted;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = UiColors.Ink;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 240, 255);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(22, 72, 133);
            grid.RowTemplate.Height = 24;
            grid.Columns.Add("Type", "类型");
            grid.Columns.Add("Name", "名称");
            grid.Columns.Add("Version", "版本");
            grid.Columns.Add("Size", "原始大小（字节）");
            grid.Columns.Add("Offset", "包内偏移");
            grid.Columns[0].FillWeight = 55;
            grid.Columns[1].FillWeight = 160;
            grid.Columns[2].FillWeight = 110;
            grid.Columns[3].FillWeight = 110;
            grid.Columns[4].FillWeight = 100;
            previewLayout.Controls.Add(grid, 0, 1);

            status.Name = "PreviewStatus";
            status.AutoSize = true;
            status.Dock = DockStyle.Fill;
            status.MaximumSize = new Size(1160, 0);
            status.Margin = new Padding(2, 5, 0, 1);
            status.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            previewLayout.Controls.Add(status, 0, 2);
            previewLayout.SizeChanged += delegate
            {
                int width = Math.Max(300, previewLayout.ClientSize.Width - 4);
                if (status.MaximumSize.Width != width) status.MaximumSize = new Size(width, 0);
            };

            SectionCard resultCard = new SectionCard();
            resultCard.Dock = DockStyle.Fill;
            resultCard.Margin = new Padding(0, 0, 0, 4);
            body.Controls.Add(resultCard, 0, 3);
            TableLayoutPanel resultLayout = CardLayout(3);
            resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            resultCard.Controls.Add(resultLayout);
            resultLayout.Controls.Add(SectionHeading("03", "生成与校验", "选择保存位置，或打开已有联合包"), 0, 0);

            TableLayoutPanel outputRow = new TableLayoutPanel();
            outputRow.Dock = DockStyle.Fill;
            outputRow.BackColor = Color.White;
            outputRow.Margin = Padding.Empty;
            outputRow.ColumnCount = 5;
            outputRow.RowCount = 1;
            outputRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
            Label outputLabel = new Label();
            outputLabel.Name = "OutputLabel";
            outputLabel.Text = "输出 BIN";
            outputLabel.Dock = DockStyle.Fill;
            outputLabel.TextAlign = ContentAlignment.MiddleLeft;
            outputLabel.ForeColor = UiColors.Muted;
            outputRow.Controls.Add(outputLabel, 0, 0);
            output.Name = "OutputPath";
            output.TextChanged += delegate { tooltips.SetToolTip(output, output.Text); };
            StyleTextBox(output, false);
            SoftField outputField = new SoftField(output, false);
            outputField.Dock = DockStyle.Fill;
            outputField.Margin = new Padding(3, 4, 3, 4);
            outputRow.Controls.Add(outputField, 1, 0);
            Button browseOutput = new Button();
            browseOutput.Text = "选择位置";
            browseOutput.Name = "BrowseOutput";
            browseOutput.Dock = DockStyle.Fill;
            StyleButton(browseOutput, false, false);
            browseOutput.Click += BrowseOutput;
            outputRow.Controls.Add(browseOutput, 2, 0);
            build.Name = "BuildButton";
            build.Text = "生成联合包";
            build.Dock = DockStyle.Fill;
            StyleButton(build, true, false);
            build.Click += BuildClicked;
            outputRow.Controls.Add(build, 3, 0);
            Button open = new Button();
            open.Name = "OpenVerifyButton";
            open.Text = "打开并校验联合包";
            open.Dock = DockStyle.Fill;
            StyleButton(open, false, false);
            open.Click += OpenClicked;
            outputRow.Controls.Add(open, 4, 0);
            resultLayout.Controls.Add(outputRow, 0, 1);

            details.Name = "ResultDetails";
            details.Multiline = true;
            details.ReadOnly = true;
            details.ScrollBars = ScrollBars.Both;
            details.WordWrap = false;
            details.ForeColor = UiColors.Ink;
            details.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point);
            details.Text = "请选择 1～5 个固件并填写版本。也可以打开现有 V1 联合包进行查看与校验。";
            SoftField detailsField = new SoftField(details, true);
            detailsField.Dock = DockStyle.Fill;
            detailsField.Margin = new Padding(0, 5, 0, 0);
            resultLayout.Controls.Add(detailsField, 0, 2);

            SectionCard ftpCard = new SectionCard();
            ftpCard.Dock = DockStyle.Fill;
            ftpCard.Margin = new Padding(0, 0, 0, 4);
            body.Controls.Add(ftpCard, 0, 4);
            TableLayoutPanel ftpLayout = CardLayout(4);
            ftpLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            ftpLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            ftpLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            ftpLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ftpCard.Controls.Add(ftpLayout);
            ftpLayout.Controls.Add(SectionHeading("04", "FTP 上传", "粘贴含账号密码的 FTP 链接可自动解析"), 0, 0);

            TableLayoutPanel addressRow = new TableLayoutPanel();
            addressRow.Dock = DockStyle.Fill;
            addressRow.Margin = Padding.Empty;
            addressRow.ColumnCount = 3;
            addressRow.RowCount = 1;
            addressRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            addressRow.Controls.Add(FieldLabel("FTP 地址"), 0, 0);
            ftpAddress.Name = "FtpAddress";
            ftpAddress.PasteAddress = delegate(string value) { ApplyFtpAddress(value, true); };
            ftpAddress.Leave += delegate { if (!String.IsNullOrWhiteSpace(ftpAddress.Text)) ApplyFtpAddress(ftpAddress.Text, false); };
            ftpAddress.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyFtpAddress(ftpAddress.Text, true); }
            };
            SoftField ftpAddressField = new SoftField(ftpAddress, false);
            ftpAddressField.Dock = DockStyle.Fill;
            ftpAddressField.Margin = new Padding(3, 3, 3, 3);
            addressRow.Controls.Add(ftpAddressField, 1, 0);
            ftpUpload.Name = "FtpUploadButton";
            ftpUpload.Text = "上传到 FTP";
            ftpUpload.Dock = DockStyle.Fill;
            StyleButton(ftpUpload, true, false);
            ftpUpload.Click += FtpUploadClicked;
            addressRow.Controls.Add(ftpUpload, 2, 0);
            ftpLayout.Controls.Add(addressRow, 0, 1);

            TableLayoutPanel credentialRow = new TableLayoutPanel();
            credentialRow.Dock = DockStyle.Fill;
            credentialRow.Margin = Padding.Empty;
            credentialRow.ColumnCount = 5;
            credentialRow.RowCount = 1;
            credentialRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            credentialRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            credentialRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            credentialRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            credentialRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            credentialRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            credentialRow.Controls.Add(FieldLabel("用户名"), 0, 0);
            ftpUser.Name = "FtpUser";
            SoftField userField = new SoftField(ftpUser, false);
            userField.Dock = DockStyle.Fill;
            credentialRow.Controls.Add(userField, 1, 0);
            credentialRow.Controls.Add(FieldLabel("密码"), 2, 0);
            ftpPassword.Name = "FtpPassword";
            ftpPassword.UseSystemPasswordChar = true;
            SoftField passwordField = new SoftField(ftpPassword, false);
            passwordField.Dock = DockStyle.Fill;
            credentialRow.Controls.Add(passwordField, 3, 0);
            Label credentialHint = FieldLabel("密码仅保留在当前窗口，不保存到文件。");
            credentialHint.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            credentialHint.Padding = new Padding(8, 0, 0, 0);
            credentialRow.Controls.Add(credentialHint, 4, 0);
            ftpLayout.Controls.Add(credentialRow, 0, 2);
            ftpStatus.Name = "FtpStatus";
            ftpStatus.Dock = DockStyle.Fill;
            ftpStatus.TextAlign = ContentAlignment.MiddleLeft;
            ftpStatus.ForeColor = UiColors.Muted;
            ftpStatus.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            ftpStatus.Text = "上传当前输出 BIN；普通 FTP 明文传输，服务器同名文件可能被覆盖。";
            ftpLayout.Controls.Add(ftpStatus, 0, 3);
            FormClosing += FtpFormClosing;

            Label md5Note = new Label();
            md5Note.Dock = DockStyle.Fill;
            md5Note.Margin = new Padding(2, 3, 0, 0);
            md5Note.ForeColor = UiColors.Muted;
            md5Note.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular);
            md5Note.TextAlign = ContentAlignment.MiddleLeft;
            md5Note.Text = "整包 MD5：计算时包头中的整包 MD5 字段按 0x00 处理，与对最终 BIN 直接运行 md5sum 不同。MD5 仅检查一致性，不验证发布者身份。";
            body.Controls.Add(md5Note, 0, 5);
            RefreshPreview();
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

        private static TableLayoutPanel SectionHeading(string number, string title, string hint)
        {
            TableLayoutPanel heading = new TableLayoutPanel();
            heading.Dock = DockStyle.Fill;
            heading.Margin = Padding.Empty;
            heading.ColumnCount = 3;
            heading.RowCount = 1;
            heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Label step = new Label();
            step.Dock = DockStyle.Fill;
            step.Margin = new Padding(0, 2, 6, 4);
            step.TextAlign = ContentAlignment.MiddleCenter;
            step.Text = number;
            step.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            step.BackColor = UiColors.SoftBlue;
            step.ForeColor = UiColors.Blue;
            heading.Controls.Add(step, 0, 0);
            Label name = new Label();
            name.Dock = DockStyle.Fill;
            name.TextAlign = ContentAlignment.MiddleLeft;
            name.Text = title;
            name.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            name.ForeColor = UiColors.Ink;
            name.Margin = Padding.Empty;
            heading.Controls.Add(name, 1, 0);
            Label subtitle = new Label();
            subtitle.Dock = DockStyle.Fill;
            subtitle.TextAlign = ContentAlignment.MiddleRight;
            subtitle.Text = hint;
            subtitle.AutoEllipsis = true;
            subtitle.Font = new Font("Microsoft YaHei UI", 8.3F, FontStyle.Regular);
            subtitle.ForeColor = UiColors.Muted;
            subtitle.Margin = Padding.Empty;
            heading.Controls.Add(subtitle, 2, 0);
            return heading;
        }

        private static void StyleTextBox(TextBox box, bool readOnly)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = readOnly ? Color.FromArgb(245, 249, 254) : Color.White;
            box.ForeColor = UiColors.Ink;
        }

        private static void StyleButton(Button button, bool primary, bool subtle)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(3, 2, 3, 2);
            button.Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.FlatAppearance.BorderSize = subtle ? 0 : 1;
            button.FlatAppearance.BorderColor = primary ? UiColors.Blue : Color.FromArgb(206, 222, 242);
            button.BackColor = primary ? UiColors.Blue : (subtle ? Color.White : Color.FromArgb(244, 249, 255));
            button.ForeColor = primary ? Color.White : (subtle ? UiColors.Muted : UiColors.Blue);
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(22, 82, 166) : Color.FromArgb(228, 241, 255);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(17, 69, 143) : Color.FromArgb(213, 232, 252);
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
            Button select = new Button();
            select.Name = "Select_" + type;
            select.Text = "浏览";
            select.Dock = DockStyle.Fill;
            StyleButton(select, false, false);
            select.Click += delegate { SelectInput(row); };
            table.Controls.Add(select, 2, index);
            Button clear = new Button();
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
            grid.Rows.Clear();
            try
            {
                PackageReport plan = Packager.Plan(Inputs());
                foreach (FirmwareEntry entry in plan.Entries)
                    grid.Rows.Add(entry.Type, entry.Name, entry.Version, entry.Size.ToString("N0"), "0x" + entry.Offset.ToString("X6"));
                StringBuilder text = new StringBuilder();
                text.AppendFormat("预计联合包：{0:N0} 字节（0x{0:X}），含包头和对齐填充。", plan.TotalSize);
                foreach (string warning in plan.Warnings) text.Append("  警告：").Append(warning);
                status.Text = text.ToString();
                status.ForeColor = plan.Warnings.Count == 0 ? Color.FromArgb(29, 120, 88) : Color.FromArgb(166, 94, 14);
            }
            catch (Exception e)
            {
                status.Text = "未就绪：" + e.Message;
                status.ForeColor = Color.FromArgb(181, 50, 57);
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
                if (showErrors) MessageBox.Show(this, ex.Message, "FTP 地址无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void FtpUploadClicked(object sender, EventArgs e)
        {
            if (ftpBusy || !ApplyFtpAddress(ftpAddress.Text, true)) return;
            try
            {
                if (String.IsNullOrWhiteSpace(output.Text) || !File.Exists(output.Text))
                    throw new InvalidDataException("请先生成或选择要上传的联合包。");
                if (String.IsNullOrWhiteSpace(ftpUser.Text)) throw new InvalidDataException("请输入 FTP 用户名。");
                FtpLocation location = FtpUploader.ParseAddress(ftpAddress.Text);
                Uri destination = FtpUploader.FileUri(location, Path.GetFileName(output.Text));
                DialogResult answer = MessageBox.Show(this,
                    "即将上传至：\n" + destination.AbsoluteUri +
                    "\n\n普通 FTP 明文传输账号、密码和文件；服务器同名文件可能被覆盖。确定上传？",
                    "确认 FTP 上传", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
                StartFtpUpload(output.Text, ftpAddress.Text, ftpUser.Text, ftpPassword.Text);
            }
            catch (InvalidDataException ex)
            {
                ftpStatus.ForeColor = Color.FromArgb(181, 50, 57);
                ftpStatus.Text = "上传失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "上传失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartFtpUpload(string path, string address, string username, string password)
        {
            ftpBusy = true;
            ftpUpload.Enabled = false;
            ftpStatus.ForeColor = UiColors.Blue;
            ftpStatus.Text = "正在校验联合包并上传，请稍候…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                FtpUploadResult result = null;
                string error = null;
                try { result = FtpUploader.Upload(path, address, username, password); }
                catch (InvalidDataException ex) { error = ex.Message; }
                catch (Exception) { error = "读取文件或连接 FTP 时发生错误，请检查文件、网络和服务器状态。"; }
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (IsDisposed) return;
                        ftpBusy = false;
                        ftpUpload.Enabled = true;
                        if (result != null)
                        {
                            ftpStatus.ForeColor = Color.FromArgb(29, 120, 88);
                            ftpStatus.Text = "上传成功：FTP 服务器已确认接收（" + result.StatusCode + "），共 " + result.Bytes.ToString("N0") + " 字节。";
                            tooltips.SetToolTip(ftpStatus, result.RemoteUrl);
                        }
                        else
                        {
                            ftpStatus.ForeColor = Color.FromArgb(181, 50, 57);
                            ftpStatus.Text = "上传失败：" + error + " 服务器上可能留有不完整文件。";
                            tooltips.SetToolTip(ftpStatus, ftpStatus.Text);
                        }
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private void FtpFormClosing(object sender, FormClosingEventArgs e)
        {
            if (ftpBusy && MessageBox.Show(this, "上传仍在进行。关闭可能中断传输，并在服务器上留下不完整文件。确定关闭？",
                "上传进行中", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                e.Cancel = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tooltips.Dispose();
            base.Dispose(disposing);
        }

        private void BuildClicked(object sender, EventArgs e)
        {
            try
            {
                PackageReport plan = Packager.Plan(Inputs());
                RefreshPreview();
                if (String.IsNullOrWhiteSpace(output.Text)) throw new InvalidDataException("请选择联合包输出位置。");
                if (File.Exists(output.Text))
                {
                    DialogResult answer = MessageBox.Show(this, "输出文件已存在。确认用新联合包替换？\n" + output.Text,
                        "确认替换", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes) return;
                }
                PackageReport report = Packager.Build(Inputs(), output.Text);
                ShowReport("打包并校验成功", Path.GetFullPath(output.Text), report);
            }
            catch (Exception ex)
            {
                details.Text = "打包失败：" + ex.Message;
                MessageBox.Show(this, details.Text, "打包失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenClicked(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "打开并校验 V1 联合固件包";
                dialog.Filter = "联合固件包 (*.bin)|*.bin|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { DisplayVerifiedFile(dialog.FileName); }
                catch (Exception ex)
                {
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
            text.AppendLine("包格式：SECCPKG1 / V1（待评审）；包头：4096 字节");
            text.AppendFormat("联合包大小：{0:N0} 字节（0x{0:X}）；固件数量：{1}\r\n", report.TotalSize, report.Entries.Count);
            text.AppendLine("整包 MD5（字段置零规则）：" + Packager.Hex(report.PackageMd5));
            foreach (FirmwareEntry entry in report.Entries)
            {
                text.AppendFormat("\r\n{0}  名称：{1}  版本：{2}\r\n", entry.Type, entry.Name, entry.Version);
                text.AppendFormat("  包内偏移：0x{0:X6}  原始大小：{1:N0} 字节  原始固件 MD5：{2}\r\n", entry.Offset, entry.Size, Packager.Hex(entry.Md5));
            }
            foreach (string warning in report.Warnings) text.AppendLine("\r\n警告：" + warning);
            text.AppendLine("\r\n整包 MD5 不等于直接对最终 BIN 运行普通 md5sum；MD5 不验证发布者身份。");
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
