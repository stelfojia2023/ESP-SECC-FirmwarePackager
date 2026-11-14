using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SeccFirmwarePackager
{
    // UI and package selection only. The device and platform protocols are not connected.
    internal sealed class UpgradePage : TableLayoutPanel
    {
        private readonly TextBox packagePath = new TextBox();
        private readonly TextBox packageInfo = new TextBox();
        private readonly Label dragHint = new Label();
        private readonly Button localMode = new RaisedButton();
        private readonly Button remoteMode = new RaisedButton();
        private readonly Panel localSettings = new Panel();
        private readonly Panel remoteSettings = new Panel();
        private readonly Label status = new Label();
        private readonly ToolTip tooltips = new ToolTip();
        private readonly TableLayoutPanel packageBody;
        private readonly TableLayoutPanel settingsBody;

        public UpgradePage()
        {
            Name = "UpgradePage"; Dock = DockStyle.Top; AutoSize = true; BackColor = UiColors.Canvas;
            Padding = new Padding(18, 14, 18, 14); Margin = Padding.Empty;
            ColumnCount = 1; RowCount = 3;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

            SectionCard packageCard = Card("UpgradePackageCard");
            packageBody = CreateLayout(3);
            packageBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            packageBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            packageBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            packageBody.Controls.Add(Title("升级文件"), 0, 0);

            packagePath.Name = "UpgradePackagePath"; packagePath.AccessibleName = "升级 BIN 文件";
            packagePath.ReadOnly = true; packagePath.AllowDrop = true;
            packagePath.DragEnter += PackageDragEnter; packagePath.DragDrop += PackageDragDrop;
            packagePath.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (!e.Control || e.KeyCode != Keys.V) return;
                e.SuppressKeyPress = true;
                try { SelectPackage(Clipboard.GetText().Trim().Trim('"')); }
                catch (Exception) { SetError("无法读取剪贴板中的文件路径。"); }
            };
            packagePath.TextChanged += delegate
            {
                dragHint.Visible = String.IsNullOrWhiteSpace(packagePath.Text);
                tooltips.SetToolTip(packagePath, packagePath.Text);
            };
            SoftField pathField = new SoftField(packagePath, false) { Dock = DockStyle.Fill, AllowDrop = true };
            pathField.DragEnter += PackageDragEnter; pathField.DragDrop += PackageDragDrop;
            dragHint.Name = "UpgradePackageHint"; dragHint.Text = "拖入 BIN 文件或点击右侧选择";
            dragHint.Dock = DockStyle.Fill; dragHint.TextAlign = ContentAlignment.MiddleLeft;
            dragHint.BackColor = UiColors.Field; dragHint.ForeColor = UiColors.Muted;
            dragHint.AutoEllipsis = true; dragHint.AllowDrop = true;
            dragHint.Click += delegate { packagePath.Focus(); };
            dragHint.DragEnter += PackageDragEnter; dragHint.DragDrop += PackageDragDrop;
            pathField.Controls.Add(dragHint); dragHint.BringToFront();

            Button browse = new RaisedButton { Name = "UpgradeBrowsePackage", Text = "选择升级 BIN", AccessibleName = "选择升级 BIN", Dock = DockStyle.Fill };
            Style(browse, false);
            ((RaisedButton)browse).IconOnly = true; ((RaisedButton)browse).Glyph = ButtonGlyph.Folder;
            tooltips.SetToolTip(browse, "选择升级 BIN");
            browse.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog { Title = "选择升级 BIN", Filter = "BIN 文件 (*.bin)|*.bin", CheckFileExists = true })
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK) SelectPackage(dialog.FileName);
            };
            TableLayoutPanel pathLine = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = UiColors.Card };
            pathLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            pathLine.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pathLine.Controls.Add(pathField, 0, 0); pathLine.Controls.Add(browse, 1, 0);
            packageBody.Controls.Add(pathLine, 0, 1);
            packageInfo.Name = "UpgradePackageInfo"; packageInfo.Multiline = true; packageInfo.ReadOnly = true;
            packageInfo.Dock = DockStyle.Fill; packageInfo.BorderStyle = BorderStyle.None; packageInfo.WordWrap = false;
            packageInfo.ScrollBars = ScrollBars.None; packageInfo.BackColor = UiColors.Card; packageInfo.ForeColor = UiColors.Muted;
            packageInfo.AccessibleName = "升级文件内容";
            packageBody.Controls.Add(packageInfo, 0, 2);
            packageCard.Controls.Add(packageBody); Controls.Add(packageCard, 0, 0);

            SectionCard settingsCard = Card("UpgradeSettingsCard");
            settingsBody = CreateLayout(2);
            settingsBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            settingsBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            FlowLayoutPanel modes = new FlowLayoutPanel { Name = "UpgradeModes", Dock = DockStyle.Fill, Margin = Padding.Empty,
                Padding = Padding.Empty, WrapContents = false, BackColor = UiColors.Card };
            PrepareMode(localMode, "LocalUpgradeMode", "本地升级");
            PrepareMode(remoteMode, "RemoteUpgradeMode", "远程升级");
            localMode.Click += delegate { SetMode(false); };
            remoteMode.Click += delegate { SetMode(true); };
            modes.Controls.Add(localMode); modes.Controls.Add(remoteMode); settingsBody.Controls.Add(modes, 0, 0);
            localSettings.Name = "LocalUpgradeSettings"; localSettings.Dock = DockStyle.Fill; localSettings.BackColor = UiColors.Card;
            remoteSettings.Name = "RemoteUpgradeSettings"; remoteSettings.Dock = DockStyle.Fill; remoteSettings.BackColor = UiColors.Card;
            localSettings.Controls.Add(BuildLocalSettings());
            remoteSettings.Controls.Add(BuildRemoteSettings());
            Panel modeHost = new Panel { Dock = DockStyle.Fill, BackColor = UiColors.Card, Margin = Padding.Empty };
            modeHost.Controls.Add(remoteSettings); modeHost.Controls.Add(localSettings);
            settingsBody.Controls.Add(modeHost, 0, 1);
            settingsCard.Controls.Add(settingsBody); Controls.Add(settingsCard, 0, 1);

            SectionCard resultCard = Card("UpgradeResultCard");
            TableLayoutPanel result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = UiColors.Card };
            result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); result.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
            result.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status.Name = "UpgradeStatus"; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = UiColors.Muted; status.AutoEllipsis = true; status.AccessibleName = "设备升级状态";
            result.Controls.Add(status, 0, 0);
            Button start = new RaisedButton { Name = "StartUpgradeButton", Text = "开始升级", Enabled = false, Size = new Size(180, 40),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(3, 2, 3, 3) };
            Style(start, true); tooltips.SetToolTip(start, "设备通信协议尚未接入");
            result.Controls.Add(start, 1, 0);
            resultCard.Controls.Add(result); Controls.Add(resultCard, 0, 2);
            SetMode(false);
        }

        private static SectionCard Card(string name)
        {
            return new SectionCard { Name = name, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(12, 8, 12, 10) };
        }

        private static TableLayoutPanel CreateLayout(int rows)
        {
            return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Margin = Padding.Empty, BackColor = UiColors.Card };
        }

        private static Label Title(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiColors.Ink, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold), Margin = Padding.Empty };
        }

        private static Label FieldLabel(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiColors.Muted, Margin = Padding.Empty };
        }

        private static void Style(Button button, bool selected)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.UseVisualStyleBackColor = false;
            button.Font = new Font("Microsoft YaHei UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular);
            button.BackColor = selected ? Color.FromArgb(219, 230, 214) : Color.FromArgb(238, 234, 242);
            button.ForeColor = UiColors.Ink; button.Cursor = Cursors.Hand;
        }

        private static void PrepareMode(Button button, string name, string text)
        {
            button.Name = name; button.Text = text; button.AccessibleName = text;
            button.Size = new Size(140, 32); button.Margin = new Padding(0, 2, 8, 2); Style(button, false);
        }

        private static TextBox NewInput(string name, string hint, bool password)
        {
            TextBox input = new PlaceholderTextBox { Name = name, PlaceholderText = hint, AccessibleName = name };
            input.UseSystemPasswordChar = password;
            return input;
        }

        private static TableLayoutPanel BuildLocalSettings()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, BackColor = UiColors.Card };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.Controls.Add(FieldLabel("设备 IP"), 0, 0);
            layout.Controls.Add(new SoftField(NewInput("LocalDeviceIp", "填写设备 IP 地址", false), false) { Dock = DockStyle.Fill }, 1, 0);
            layout.Controls.Add(FieldLabel("端口"), 2, 0);
            layout.Controls.Add(new SoftField(NewInput("LocalDevicePort", "待确定", false), false) { Dock = DockStyle.Fill }, 3, 0);
            return layout;
        }

        private static TableLayoutPanel BuildRemoteSettings()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Margin = Padding.Empty, BackColor = UiColors.Card };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.Controls.Add(FieldLabel("平台地址"), 0, 0);
            Control endpoint = new SoftField(NewInput("RemotePlatformAddress", "运营平台接口地址", false), false) { Dock = DockStyle.Fill };
            layout.Controls.Add(endpoint, 1, 0); layout.SetColumnSpan(endpoint, 3);
            layout.Controls.Add(FieldLabel("CPID"), 0, 1);
            Control cpid = new SoftField(NewInput("RemoteCpid", "填写设备 CPID", false), false) { Dock = DockStyle.Fill };
            layout.Controls.Add(cpid, 1, 1); layout.SetColumnSpan(cpid, 3);
            layout.Controls.Add(FieldLabel("账号"), 0, 2);
            layout.Controls.Add(new SoftField(NewInput("RemoteAccount", "按平台要求填写", false), false) { Dock = DockStyle.Fill }, 1, 2);
            layout.Controls.Add(FieldLabel("密码"), 2, 2);
            layout.Controls.Add(new SoftField(NewInput("RemotePassword", "按平台要求填写", true), false) { Dock = DockStyle.Fill }, 3, 2);
            return layout;
        }

        private void SetMode(bool remote)
        {
            localSettings.Visible = !remote; remoteSettings.Visible = remote;
            localMode.BackColor = remote ? Color.FromArgb(238, 234, 242) : Color.FromArgb(219, 230, 214);
            remoteMode.BackColor = remote ? Color.FromArgb(219, 230, 214) : Color.FromArgb(238, 234, 242);
            settingsBody.RowStyles[1].Height = remote ? 96 : 32;
            RowStyles[1].Height = remote ? 160 : 96;
            status.Text = remote ? "运营平台升级接口尚未接入。" : "本地网口升级协议尚未接入。";
            PerformLayout();
        }

        private static string[] DroppedFiles(DragEventArgs e)
        {
            return e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
        }

        private static bool IsBin(string path)
        {
            return !String.IsNullOrWhiteSpace(path) && File.Exists(path) &&
                String.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase);
        }

        private static void PackageDragEnter(object sender, DragEventArgs e)
        {
            string[] files = DroppedFiles(e);
            e.Effect = files != null && files.Length == 1 && IsBin(files[0]) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void PackageDragDrop(object sender, DragEventArgs e)
        {
            string[] files = DroppedFiles(e);
            if (files == null || files.Length != 1) { SetError("请选择一个 BIN 文件。"); return; }
            SelectPackage(files[0]);
        }

        private void SetError(string text)
        {
            packageInfo.ForeColor = Color.FromArgb(181, 50, 57);
            SetInfo(text, 1);
        }

        private void SetInfo(string text, int lines)
        {
            packageInfo.Text = text;
            int height = Math.Max(24, Math.Min(5, lines) * 24 + 4);
            packageBody.RowStyles[2].Height = height;
            RowStyles[0].Height = 96 + height;
            tooltips.SetToolTip(packageInfo, text);
            PerformLayout();
        }

        private void DisplayReport(PackageReport report)
        {
            StringBuilder info = new StringBuilder();
            foreach (FirmwareEntry entry in report.Entries)
            {
                if (info.Length > 0) info.Append("\r\n");
                info.Append(entry.Type).Append("    ").Append(entry.Version);
            }
            packageInfo.ForeColor = UiColors.Ink;
            SetInfo(info.ToString(), report.Entries.Count);
        }

        internal void UseGeneratedPackage(string path, PackageReport report)
        {
            packagePath.Text = path;
            DisplayReport(report);
        }

        private void SelectPackage(string path)
        {
            try
            {
                if (!IsBin(path)) { SetError("请选择一个存在的 BIN 文件。"); return; }
                string fullPath = Path.GetFullPath(path);
                packagePath.Text = fullPath;
                try { DisplayReport(Packager.VerifyFile(fullPath)); }
                catch (InvalidDataException) { SetError("已选择 BIN，无法识别或校验为本工具的联合包。"); }
            }
            catch (Exception) { SetError("无法读取所选文件。"); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tooltips.Dispose();
            base.Dispose(disposing);
        }
    }
}
