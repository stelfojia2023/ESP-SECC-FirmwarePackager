using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SeccFirmwarePackager
{
    public sealed class MainForm : Form
    {
        private sealed class Row
        {
            public FirmwareType Type;
            public TextBox PathBox;
            public TextBox NameBox;
            public TextBox VersionBox;
            public Label SizeLabel;
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox output = new TextBox();
        private readonly TextBox details = new TextBox();
        private readonly Label status = new Label();
        private readonly Button build = new Button();
        private bool changing;

        public MainForm()
        {
            Text = "ESP-SECC 固件联合打包工具 V1（待评审）";
            Name = "MainForm";
            MinimumSize = new Size(1000, 690);
            Size = new Size(1280, 880);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.RowCount = 8;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            Label notice = new Label();
            notice.AutoSize = true;
            notice.MaximumSize = new Size(1200, 0);
            notice.ForeColor = Color.FromArgb(145, 69, 0);
            notice.Text = "待评审 V1 格式：当前设备尚未接入联合包解析。6 MiB 暂存区及 2.5 MiB ESP32 槽为候选方案，非当前设备配置。";
            notice.Margin = new Padding(0, 0, 0, 10);
            root.Controls.Add(notice, 0, 0);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Name = "InputsTable";
            table.AutoSize = true;
            table.Dock = DockStyle.Fill;
            table.ColumnCount = 7;
            table.RowCount = 6;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            string[] headings = { "类型", "固件文件", "选择", "清除", "固件名称（可修改）", "版本（手填）", "原始大小" };
            for (int c = 0; c < headings.Length; c++)
            {
                Label label = new Label();
                label.Text = headings[c];
                label.AutoSize = true;
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.MiddleLeft;
                table.Controls.Add(label, c, 0);
            }
            FirmwareType[] types = { FirmwareType.PFC, FirmwareType.LLC, FirmwareType.M4, FirmwareType.PLC, FirmwareType.ESP32 };
            for (int i = 0; i < types.Length; i++) AddRow(table, types[i], i + 1);
            root.Controls.Add(table, 0, 1);

            Label preview = new Label();
            preview.AutoSize = true;
            preview.Margin = new Padding(0, 10, 0, 4);
            preview.Text = "打包前预览：偏移相对于联合 BIN 起点，大小为原始固件大小。";
            root.Controls.Add(preview, 0, 2);

            grid.Name = "PreviewGrid";
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
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
            root.Controls.Add(grid, 0, 3);

            status.Name = "PreviewStatus";
            status.AutoSize = true;
            status.MaximumSize = new Size(1200, 0);
            status.Margin = new Padding(0, 5, 0, 5);
            root.Controls.Add(status, 0, 4);

            TableLayoutPanel outputRow = new TableLayoutPanel();
            outputRow.AutoSize = true;
            outputRow.Dock = DockStyle.Fill;
            outputRow.ColumnCount = 5;
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            Label outputLabel = new Label();
            outputLabel.Text = "输出 BIN：";
            outputLabel.AutoSize = true;
            outputLabel.Dock = DockStyle.Fill;
            outputLabel.TextAlign = ContentAlignment.MiddleLeft;
            outputRow.Controls.Add(outputLabel, 0, 0);
            output.Name = "OutputPath";
            output.Dock = DockStyle.Fill;
            outputRow.Controls.Add(output, 1, 0);
            Button browseOutput = new Button();
            browseOutput.Text = "选择位置";
            browseOutput.Name = "BrowseOutput";
            browseOutput.AutoSize = true;
            browseOutput.Click += BrowseOutput;
            outputRow.Controls.Add(browseOutput, 2, 0);
            build.Name = "BuildButton";
            build.Text = "生成联合包";
            build.AutoSize = true;
            build.Click += BuildClicked;
            outputRow.Controls.Add(build, 3, 0);
            Button open = new Button();
            open.Name = "OpenVerifyButton";
            open.Text = "打开并校验联合包";
            open.AutoSize = true;
            open.Click += OpenClicked;
            outputRow.Controls.Add(open, 4, 0);
            root.Controls.Add(outputRow, 0, 5);

            details.Name = "ResultDetails";
            details.Multiline = true;
            details.ReadOnly = true;
            details.ScrollBars = ScrollBars.Both;
            details.WordWrap = false;
            details.Dock = DockStyle.Fill;
            details.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            details.Text = "请选择 1～5 个固件并填写版本。也可以打开现有 V1 联合包进行查看与校验。";
            root.Controls.Add(details, 0, 6);

            Label md5Note = new Label();
            md5Note.AutoSize = true;
            md5Note.Margin = new Padding(0, 8, 0, 0);
            md5Note.Text = "整包 MD5：计算时包头中的整包 MD5 字段按 0x00 处理，与对最终 BIN 直接运行 md5sum 不同。MD5 仅检查一致性，不验证发布者身份。";
            root.Controls.Add(md5Note, 0, 7);
            RefreshPreview();
        }

        private void AddRow(TableLayoutPanel table, FirmwareType type, int index)
        {
            Row row = new Row();
            row.Type = type;
            rows.Add(row);
            Label label = new Label();
            label.Text = type.ToString();
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            table.Controls.Add(label, 0, index);
            row.PathBox = new TextBox();
            row.PathBox.Name = "Path_" + type;
            row.PathBox.ReadOnly = true;
            row.PathBox.Dock = DockStyle.Fill;
            row.PathBox.TextChanged += InputChanged;
            table.Controls.Add(row.PathBox, 1, index);
            Button select = new Button();
            select.Name = "Select_" + type;
            select.Text = "浏览";
            select.Dock = DockStyle.Fill;
            select.Click += delegate { SelectInput(row); };
            table.Controls.Add(select, 2, index);
            Button clear = new Button();
            clear.Name = "Clear_" + type;
            clear.Text = "清除";
            clear.Dock = DockStyle.Fill;
            clear.Click += delegate { ClearInput(row); };
            table.Controls.Add(clear, 3, index);
            row.NameBox = new TextBox();
            row.NameBox.Name = "Name_" + type;
            row.NameBox.Dock = DockStyle.Fill;
            row.NameBox.TextChanged += InputChanged;
            table.Controls.Add(row.NameBox, 4, index);
            row.VersionBox = new TextBox();
            row.VersionBox.Name = "Version_" + type;
            row.VersionBox.Dock = DockStyle.Fill;
            row.VersionBox.TextChanged += InputChanged;
            table.Controls.Add(row.VersionBox, 5, index);
            row.SizeLabel = new Label();
            row.SizeLabel.Name = "Size_" + type;
            row.SizeLabel.Text = "未选择";
            row.SizeLabel.Dock = DockStyle.Fill;
            row.SizeLabel.TextAlign = ContentAlignment.MiddleLeft;
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
                changing = true;
                row.PathBox.Text = dialog.FileName;
                row.NameBox.Text = Path.GetFileName(dialog.FileName);
                changing = false;
                RefreshPreview();
            }
        }

        private void ClearInput(Row row)
        {
            changing = true;
            row.PathBox.Clear();
            row.NameBox.Clear();
            row.VersionBox.Clear();
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
                status.ForeColor = plan.Warnings.Count == 0 ? Color.DarkGreen : Color.DarkOrange;
            }
            catch (Exception e)
            {
                status.Text = "未就绪：" + e.Message;
                status.ForeColor = Color.Firebrick;
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
