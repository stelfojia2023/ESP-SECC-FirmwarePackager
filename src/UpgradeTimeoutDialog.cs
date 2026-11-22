using System;
using System.Drawing;
using System.Windows.Forms;

namespace SeccFirmwarePackager
{
    internal sealed class UpgradeTimeoutDialog : Form
    {
        private readonly TextBox connect = new TextBox { Name = "UpgradeConnectSeconds" };
        private readonly TextBox block = new TextBox { Name = "UpgradeBlockSeconds" };
        private readonly TextBox operation = new TextBox { Name = "UpgradeOperationSeconds" };
        private readonly TextBox wait = new TextBox { Name = "UpgradeWaitSeconds" };
        private readonly Label error = new Label();

        public int ConnectMs { get; private set; }
        public int BlockMs { get; private set; }
        public int OperationMs { get; private set; }
        public int WaitMs { get; private set; }

        public UpgradeTimeoutDialog(int connectMs, int blockMs, int operationMs, int waitMs)
        {
            Name = "UpgradeTimeoutDialog"; Text = "本地升级超时设置";
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowIcon = false; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 9.5F);
            ClientSize = new Size(470, 248); BackColor = UiColors.Canvas;
            connect.Text = (connectMs / 1000).ToString();
            block.Text = (blockMs / 1000).ToString();
            operation.Text = (operationMs / 1000).ToString();
            wait.Text = (waitMs / 1000).ToString();

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6,
                Padding = new Padding(16, 12, 16, 12), BackColor = Color.Transparent
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            AddField(layout, 0, "连接及普通查询（秒）", connect);
            AddField(layout, 1, "数据块（秒）", block);
            AddField(layout, 2, "开始及结束（秒）", operation);
            AddField(layout, 3, "等待版本（秒）", wait);
            error.Dock = DockStyle.Fill; error.ForeColor = UiColors.Error;
            error.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(error, 0, 4); layout.SetColumnSpan(error, 2);
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Margin = Padding.Empty, BackColor = Color.Transparent
            };
            Button save = new RaisedButton { Name = "UpgradeTimeoutSave", Text = "保存", Size = new Size(120, 40) };
            Button cancel = new RaisedButton { Text = "取消", Size = new Size(120, 40), DialogResult = DialogResult.Cancel };
            UpgradePage.Style(save, true);
            UpgradePage.Style(cancel, false);
            save.Click += SaveClicked;
            actions.Controls.Add(save); actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 5); layout.SetColumnSpan(actions, 2);
            Controls.Add(layout);
            AcceptButton = save; CancelButton = cancel;
        }

        private static void AddField(TableLayoutPanel layout, int row, string title, TextBox input)
        {
            Label label = new Label
            {
                Text = title, Dock = DockStyle.Fill, ForeColor = UiColors.Muted,
                TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
            };
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(new SoftField(input, false) { Dock = DockStyle.Fill }, 1, row);
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            int c, b, o, w;
            if (!Int32.TryParse(connect.Text, out c) || c < 1 || c > 3600 ||
                !Int32.TryParse(block.Text, out b) || b < 1 || b > 3600 ||
                !Int32.TryParse(operation.Text, out o) || o < 1 || o > 3600 ||
                !Int32.TryParse(wait.Text, out w) || w < 1 || w > 86400)
            {
                error.Text = "请输入有效秒数：等待时间 1～86400，其余 1～3600。";
                return;
            }
            ConnectMs = c * 1000; BlockMs = b * 1000; OperationMs = o * 1000; WaitMs = w * 1000;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
