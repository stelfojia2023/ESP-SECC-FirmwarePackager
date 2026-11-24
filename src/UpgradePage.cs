using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SeccFirmwarePackager
{
    // Local upgrade uses LUP1; the remote platform remains a separate placeholder.
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
        private readonly TableLayoutPanel transferStatus = CreateLayout(2);
        private readonly ProgressBar transferProgress = new ProgressBar();
        private readonly Label lastTaskLabel = new Label();
        private readonly SectionCard logCard = Card("UpgradeOperationLogCard");
        private readonly TextBox operationLog = new TextBox();
        private readonly Button start = new RaisedButton();
        private readonly Button continueButton = new RaisedButton();
        private readonly Button stopUpgrade = new RaisedButton();
        private readonly Button readVersions = new RaisedButton();
        private readonly FlowLayoutPanel localActions = new FlowLayoutPanel();
        private readonly Button browse = new RaisedButton();
        private readonly Button timeoutSettings = new RaisedButton();
        private readonly TextBox localIp = NewInput("LocalDeviceIp", "填写设备 IP 地址", false);
        private readonly TextBox localPort = NewInput("LocalDevicePort", "填写端口", false);
        private readonly ToolTip tooltips = new ToolTip();
        private readonly TableLayoutPanel packageBody;
        private readonly TableLayoutPanel settingsBody;
        private readonly TableLayoutPanel resultBody;
        private readonly TableLayoutPanel upgradeActions = new TableLayoutPanel();
        private readonly Label stopHint = FieldLabel("协议未接入");
        private bool showingTransferProgress;
        private bool remote;
        private bool candidateValid;
        private int selectionRevision;
        private bool busy;
        private bool transferActive;
        private string pendingGeneratedPath;
        private string localStatusText = "准备本地升级。";
        private bool localStatusError;
        private string lastObservedStatus;
        private int connectTimeoutMs = 5000;
        private int blockTimeoutMs = 10000;
        private int operationTimeoutMs = 120000;
        private int waitTimeoutMs = 600000;
        private ObservedTask lastTask;
        private bool canResume;
        private readonly object connectionGate = new object();
        private readonly CancellationTokenSource shutdownCancellation = new CancellationTokenSource();
        private Lup1Connection activeConnection;
        private volatile bool shuttingDown;
        public event Action<string, bool> ResultReported;

        internal bool TransferActive { get { return transferActive; } }

        private sealed class ObservedTask
        {
            public IPAddress Address;
            public int Port;
            public LocalUpgradeSnapshot Snapshot;
            public byte[] Task;
            public IList<LupVersion> Before;
            public IList<LupVersion> Latest;
            public int CandidateRevision;
            public bool ReceiptUnconfirmed;
            public bool Ended;
            public string VersionResult = "版本待核对";
            public string LastVersionLog;
            public string Endpoint { get { return new IPEndPoint(Address, Port).ToString(); } }
        }

        public UpgradePage()
        {
            Name = "UpgradePage"; Dock = DockStyle.Top; AutoSize = true; BackColor = UiColors.Canvas;
            Padding = new Padding(18, 4, 18, 14); Margin = Padding.Empty;
            ColumnCount = 1; RowCount = 4;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 142));

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
                if (busy) return;
                if (!e.Control || e.KeyCode != Keys.V) return;
                e.SuppressKeyPress = true;
                try { SelectPackage(Clipboard.GetText().Trim().Trim('"')); }
                catch (Exception) { SetError("无法读取剪贴板中的文件路径。"); }
            };
            packagePath.TextChanged += delegate
            {
                selectionRevision++;
                candidateValid = false;
                UpdateStart();
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

            browse.Name = "UpgradeBrowsePackage"; browse.Text = "选择升级 BIN";
            browse.AccessibleName = "选择升级 BIN"; browse.Dock = DockStyle.Fill;
            Style(browse, false); browse.BackColor = UiColors.ButtonIcon;
            ((RaisedButton)browse).IconOnly = true; ((RaisedButton)browse).Glyph = ButtonGlyph.Folder;
            tooltips.SetToolTip(browse, "选择升级 BIN");
            browse.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog { Title = "选择升级 BIN", Filter = "BIN 文件 (*.bin)|*.bin", CheckFileExists = true })
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK) SelectPackage(dialog.FileName);
            };
            TableLayoutPanel pathLine = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
            pathLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            pathLine.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pathLine.Controls.Add(pathField, 0, 0); pathLine.Controls.Add(browse, 1, 0);
            packageBody.Controls.Add(pathLine, 0, 1);
            packageInfo.Name = "UpgradePackageInfo"; packageInfo.Multiline = true; packageInfo.ReadOnly = true;
            packageInfo.Dock = DockStyle.Fill; packageInfo.BorderStyle = BorderStyle.None; packageInfo.WordWrap = true;
            packageInfo.ScrollBars = ScrollBars.Vertical; packageInfo.BackColor = UiColors.Card; packageInfo.ForeColor = UiColors.Muted;
            packageInfo.AccessibleName = "升级文件内容";
            packageBody.Controls.Add(packageInfo, 0, 2);
            packageCard.Controls.Add(packageBody); Controls.Add(packageCard, 0, 0);

            SectionCard settingsCard = Card("UpgradeSettingsCard");
            settingsBody = CreateLayout(2);
            settingsBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            settingsBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            FlowLayoutPanel modes = new FlowLayoutPanel { Name = "UpgradeModes", Dock = DockStyle.Fill, Margin = Padding.Empty,
                Padding = Padding.Empty, WrapContents = false, BackColor = Color.Transparent };
            PrepareMode(localMode, "LocalUpgradeMode", "本地升级");
            PrepareMode(remoteMode, "RemoteUpgradeMode", "远程升级");
            localMode.Click += delegate { SetMode(false); };
            remoteMode.Click += delegate { SetMode(true); };
            modes.Controls.Add(localMode); modes.Controls.Add(remoteMode); settingsBody.Controls.Add(modes, 0, 0);
            localSettings.Name = "LocalUpgradeSettings"; localSettings.Dock = DockStyle.Fill; localSettings.BackColor = Color.Transparent;
            remoteSettings.Name = "RemoteUpgradeSettings"; remoteSettings.Dock = DockStyle.Fill; remoteSettings.BackColor = Color.Transparent;
            localSettings.Controls.Add(BuildLocalSettings());
            remoteSettings.Controls.Add(BuildRemoteSettings());
            Panel modeHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
            modeHost.Controls.Add(remoteSettings); modeHost.Controls.Add(localSettings);
            settingsBody.Controls.Add(modeHost, 0, 1);
            settingsCard.Controls.Add(settingsBody); Controls.Add(settingsCard, 0, 1);

            SectionCard resultCard = Card("UpgradeResultCard");
            resultBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty, BackColor = Color.Transparent };
            resultBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); resultBody.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
            resultBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); resultBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            resultBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            status.Name = "UpgradeStatus"; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = UiColors.Muted; status.AutoEllipsis = true; status.AccessibleName = "设备升级状态";
            transferStatus.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            transferStatus.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            transferStatus.Controls.Add(status, 0, 0);
            transferProgress.Name = "UpgradeTransferProgress"; transferProgress.AccessibleName = "包传输进度";
            transferProgress.Dock = DockStyle.Fill; transferProgress.Margin = new Padding(3, 0, 12, 3);
            transferProgress.Style = ProgressBarStyle.Continuous; transferProgress.Visible = false;
            transferStatus.Controls.Add(transferProgress, 0, 1);
            resultBody.Controls.Add(transferStatus, 0, 0);
            start.Name = "StartUpgradeButton"; start.Text = "开始升级"; start.Enabled = false; start.Size = new Size(180, 40);
            start.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; start.Margin = new Padding(3, 2, 3, 3);
            Style(start, true); start.Click += StartClicked;
            upgradeActions.Dock = DockStyle.Fill; upgradeActions.Margin = Padding.Empty;
            upgradeActions.ColumnCount = 2; upgradeActions.RowCount = 2;
            upgradeActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
            upgradeActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
            upgradeActions.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            upgradeActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            upgradeActions.Controls.Add(start, 0, 0);
            PrepareMode(stopUpgrade, "StopUpgradeButton", "停止升级");
            stopUpgrade.Size = start.Size; stopUpgrade.Margin = start.Margin;
            stopUpgrade.Anchor = start.Anchor; stopUpgrade.Enabled = false;
            upgradeActions.Controls.Add(stopUpgrade, 1, 0);
            stopHint.Name = "StopUpgradeHint"; stopHint.TextAlign = ContentAlignment.MiddleCenter;
            upgradeActions.Controls.Add(stopHint, 1, 1);
            resultBody.Controls.Add(upgradeActions, 1, 0);
            lastTaskLabel.Name = "UpgradeLastTask"; lastTaskLabel.Dock = DockStyle.Fill;
            lastTaskLabel.ForeColor = UiColors.Muted; lastTaskLabel.AutoEllipsis = true;
            lastTaskLabel.TextAlign = ContentAlignment.MiddleLeft;
            resultBody.Controls.Add(lastTaskLabel, 0, 1);
            continueButton.Name = "ContinueUpgradeButton"; continueButton.Text = "继续等待";
            continueButton.Size = new Size(180, 40); continueButton.Margin = new Padding(3, 2, 3, 3);
            continueButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            Style(continueButton, false); continueButton.Click += ContinueClicked;
            resultBody.Controls.Add(continueButton, 1, 1);
            localActions.Dock = DockStyle.Fill; localActions.Margin = Padding.Empty; localActions.WrapContents = false;
            PrepareMode(readVersions, "ReadFirmwareVersionsButton", "读取固件版本");
            readVersions.Size = new Size(180, 40); readVersions.Click += ReadFirmwareVersionsClicked;
            localActions.Controls.Add(readVersions);
            resultBody.Controls.Add(localActions, 0, 2); resultBody.SetColumnSpan(localActions, 2);
            resultCard.Controls.Add(resultBody); Controls.Add(resultCard, 0, 2);

            TableLayoutPanel logBody = CreateLayout(2);
            logBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            logBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            logBody.Controls.Add(Title("升级日志"), 0, 0);
            operationLog.Name = "UpgradeOperationLog"; operationLog.AccessibleName = "升级日志";
            operationLog.Dock = DockStyle.Fill; operationLog.Multiline = true; operationLog.ReadOnly = true;
            operationLog.ScrollBars = ScrollBars.Vertical; operationLog.BackColor = UiColors.Field;
            operationLog.ForeColor = UiColors.Ink;
            logBody.Controls.Add(operationLog, 0, 1);
            logCard.Controls.Add(logBody); Controls.Add(logCard, 0, 3);
            SetMode(false);
        }

        private static SectionCard Card(string name)
        {
            return new SectionCard { Name = name, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(12, 8, 12, 10) };
        }

        private static TableLayoutPanel CreateLayout(int rows)
        {
            return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Margin = Padding.Empty, BackColor = Color.Transparent };
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

        internal static void Style(Button button, bool selected)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.UseVisualStyleBackColor = false;
            button.Font = new Font("Microsoft YaHei UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular);
            button.BackColor = selected ? UiColors.ButtonPrimary : UiColors.ButtonSecondary;
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

        private TableLayoutPanel BuildLocalSettings()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            localIp.Text = "192.168.198.50";
            localPort.Text = "8090";
            layout.Controls.Add(FieldLabel("设备 IP"), 0, 0);
            layout.Controls.Add(new SoftField(localIp, false) { Dock = DockStyle.Fill }, 1, 0);
            layout.Controls.Add(FieldLabel("端口"), 2, 0);
            layout.Controls.Add(new SoftField(localPort, false) { Dock = DockStyle.Fill }, 3, 0);
            timeoutSettings.Name = "UpgradeTimeoutSettings"; timeoutSettings.Text = "超时设置";
            timeoutSettings.Dock = DockStyle.Fill; Style(timeoutSettings, false);
            timeoutSettings.Click += delegate
            {
                if (busy) return;
                using (UpgradeTimeoutDialog dialog = new UpgradeTimeoutDialog(connectTimeoutMs, blockTimeoutMs, operationTimeoutMs, waitTimeoutMs))
                {
                    if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                    connectTimeoutMs = dialog.ConnectMs; blockTimeoutMs = dialog.BlockMs;
                    operationTimeoutMs = dialog.OperationMs; waitTimeoutMs = dialog.WaitMs;
                }
            };
            layout.Controls.Add(timeoutSettings, 4, 0);
            return layout;
        }

        private static TableLayoutPanel BuildRemoteSettings()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Margin = Padding.Empty, BackColor = Color.Transparent };
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
            this.remote = remote;
            localSettings.Visible = !remote; remoteSettings.Visible = remote;
            localActions.Visible = !remote;
            stopUpgrade.Visible = !remote;
            stopHint.Visible = !remote;
            resultBody.ColumnStyles[1].Width = remote ? 186 : 372;
            resultBody.RowStyles[0].Height = remote ? 48 : 64;
            upgradeActions.ColumnStyles[1].Width = remote ? 0 : 186;
            UpdateTransferProgressVisibility();
            logCard.Visible = !remote;
            RowStyles[3].Height = remote ? 0 : 142;
            localMode.BackColor = remote ? UiColors.ButtonSecondary : UiColors.ButtonPrimary;
            remoteMode.BackColor = remote ? UiColors.ButtonPrimary : UiColors.ButtonSecondary;
            settingsBody.RowStyles[1].Height = remote ? 96 : 32;
            RowStyles[1].Height = remote ? 160 : 96;
            status.Text = remote ? "运营平台升级接口尚未接入。" : localStatusText;
            status.ForeColor = !remote && localStatusError ? UiColors.Error : UiColors.Muted;
            tooltips.SetToolTip(status, status.Text);
            UpdateStart();
            PerformLayout();
        }

        private void UpdateStart()
        {
            start.Enabled = !remote && candidateValid && !busy;
            readVersions.Enabled = !remote && !busy;
            UpdateResumeLayout();
        }

        private void UpdateResumeLayout()
        {
            if (resultBody == null) return;
            bool show = !remote && lastTask != null &&
                (canResume || selectionRevision != lastTask.CandidateRevision);
            lastTaskLabel.Visible = show;
            continueButton.Visible = show && canResume;
            continueButton.Enabled = show && canResume && !busy;
            resultBody.RowStyles[1].Height = show ? 48 : 0;
            resultBody.RowStyles[2].Height = remote ? 0 : 48;
            RowStyles[2].Height = (show ? 120 : 72) + (remote ? 0 : 64);
            if (show)
            {
                lastTaskLabel.Text = (canResume ? "可继续等待：" : "最近任务：") + lastTask.Endpoint +
                    " " + Path.GetFileName(lastTask.Snapshot.Path);
                if (selectionRevision != lastTask.CandidateRevision &&
                    String.Equals(packagePath.Text, lastTask.Snapshot.Path, StringComparison.OrdinalIgnoreCase))
                    lastTaskLabel.Text += "（路径已用于新包）";
                StringBuilder details = new StringBuilder("设备：" + lastTask.Endpoint + "\r\n任务包：" + lastTask.Snapshot.Path);
                for (int i = 0; i < lastTask.Snapshot.Report.Entries.Count; i++)
                {
                    FirmwareEntry entry = lastTask.Snapshot.Report.Entries[i];
                    details.Append("\r\n").Append(entry.Type).Append("  目标：").Append(entry.Version);
                    if (lastTask.Before != null) details.Append("  升级前：").Append(VersionText(lastTask.Before[i]));
                    if (lastTask.Latest != null) details.Append("  当前：").Append(VersionText(lastTask.Latest[i]));
                }
                tooltips.SetToolTip(lastTaskLabel, details.ToString());
            }
        }

        private void PostToUi(Action action)
        {
            if (shuttingDown || IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        private Lup1Connection Connect(IPAddress address, int port, int timeoutMs)
        {
            if (shuttingDown) throw new OperationCanceledException("工具已关闭。");
            Lup1Connection connection = new Lup1Connection(address, port, timeoutMs, shutdownCancellation.Token);
            lock (connectionGate)
            {
                if (shuttingDown)
                {
                    connection.Dispose();
                    throw new OperationCanceledException("工具已关闭。");
                }
                activeConnection = connection;
            }
            return connection;
        }

        private void CloseConnection(Lup1Connection connection)
        {
            if (connection == null) return;
            lock (connectionGate)
            {
                if (Object.ReferenceEquals(activeConnection, connection)) activeConnection = null;
            }
            connection.Dispose();
        }

        internal void ShutdownClient()
        {
            Lup1Connection connection;
            lock (connectionGate)
            {
                shuttingDown = true;
                connection = activeConnection;
                activeConnection = null;
            }
            shutdownCancellation.Cancel();
            if (connection != null) connection.Dispose();
        }

        private void Finish(string message, bool error)
        {
            busy = false;
            transferActive = false;
            showingTransferProgress = false;
            UpdateTransferProgressVisibility();
            packagePath.Enabled = true;
            browse.Enabled = true;
            localIp.Enabled = true;
            localPort.Enabled = true;
            timeoutSettings.Enabled = true;
            if (message != null)
            {
                SetLocalStatus(message, error);
                ReportResult(message, error);
            }
            if (!String.IsNullOrEmpty(pendingGeneratedPath))
            {
                string next = pendingGeneratedPath;
                pendingGeneratedPath = null;
                SelectPackage(next);
            }
            UpdateStart();
        }

        private void SetLocalStatus(string message, bool error)
        {
            localStatusText = message;
            localStatusError = error;
            if (!remote)
            {
                status.ForeColor = error ? UiColors.Error : UiColors.Muted;
                status.Text = message;
                tooltips.SetToolTip(status, message);
            }
        }

        private void UpdateTransferProgressVisibility()
        {
            bool show = showingTransferProgress && !remote;
            transferStatus.RowStyles[1].Height = show ? 16 : 0;
            transferProgress.Visible = show;
        }

        private void SetTransferProgress(int percent)
        {
            showingTransferProgress = true;
            transferProgress.Value = percent;
            UpdateTransferProgressVisibility();
            SetLocalStatus("包传输进度：" + percent + "%", false);
        }

        private void ReportResult(string message, bool error)
        {
            operationLog.AppendText("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "] " +
                message.Replace("\r\n", "；").Replace("\n", "；").TrimEnd('\r') + Environment.NewLine);
            operationLog.SelectionStart = operationLog.TextLength;
            operationLog.ScrollToCaret();
            Action<string, bool> handler = ResultReported;
            if (handler != null) handler(message, error);
        }

        private static string VersionText(LupVersion version)
        {
            return version.Status == 0 ? version.Value :
                version.Status == 1 ? "未就绪" : version.Status == 2 ? "不支持查询" : "不可用";
        }

        private void LogVersionComparison(ObservedTask observed, IList<LupVersion> versions)
        {
            StringBuilder text = new StringBuilder("运行版本对比：");
            for (int i = 0; i < versions.Count; i++)
            {
                if (i != 0) text.Append("；");
                FirmwareEntry entry = observed.Snapshot.Report.Entries[i];
                LupVersion current = versions[i];
                text.Append(entry.Type).Append(" 升级前：").Append(VersionText(observed.Before[i]))
                    .Append(" / 目标：").Append(entry.Version).Append(" / 当前：").Append(VersionText(current));
                if (current.Status == 0)
                    text.Append(String.Equals(current.Value, entry.Version, StringComparison.Ordinal) ? "（一致）" : "（不一致）");
            }
            string message = text.ToString();
            if (observed.LastVersionLog == message) return;
            observed.LastVersionLog = message;
            ReportResult(message, false);
        }

        private void ReadFirmwareVersionsClicked(object sender, EventArgs e)
        {
            if (busy || remote) return;
            IPAddress address;
            int port;
            if (!IPAddress.TryParse(localIp.Text.Trim(), out address) ||
                !Int32.TryParse(localPort.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                MessageBox.Show(FindForm(), "请输入有效的设备 IP 和端口。", "读取固件版本",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string endpoint = new IPEndPoint(address, port).ToString();
            int timeout = connectTimeoutMs;
            busy = true;
            packagePath.Enabled = false; browse.Enabled = false;
            localIp.Enabled = false; localPort.Enabled = false; timeoutSettings.Enabled = false;
            readVersions.Text = "正在读取…";
            UpdateStart();
            ThreadPool.QueueUserWorkItem(delegate
            {
                Lup1Connection connection = null;
                StringBuilder result = new StringBuilder("设备：" + endpoint);
                bool error = false;
                try
                {
                    FirmwareEntry[] targets = {
                        new FirmwareEntry { Type = FirmwareType.PFC }, new FirmwareEntry { Type = FirmwareType.LLC },
                        new FirmwareEntry { Type = FirmwareType.M4 }, new FirmwareEntry { Type = FirmwareType.PLC },
                        new FirmwareEntry { Type = FirmwareType.ESP32 }
                    };
                    connection = Connect(address, port, timeout);
                    foreach (LupVersion version in connection.QueryVersions(targets, timeout))
                        result.Append("\r\n").Append(version.Type).Append("：").Append(VersionText(version));
                }
                catch (Exception ex)
                {
                    error = true;
                    result.Append("\r\n读取固件版本失败：").Append(ex.Message);
                }
                finally { CloseConnection(connection); }
                PostToUi(delegate
                {
                    readVersions.Text = "读取固件版本";
                    Finish(null, false);
                    ReportResult("读取固件版本：" + result, error);
                    MessageBox.Show(FindForm(), result.ToString(), "读取固件版本", MessageBoxButtons.OK,
                        error ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                });
            });
        }

        private void DisplayVersions(PackageReport report, IList<LupVersion> versions, IList<LupVersion> before)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < report.Entries.Count; i++)
            {
                if (i > 0) text.Append("\r\n");
                FirmwareEntry entry = report.Entries[i];
                LupVersion version = versions[i];
                text.Append(entry.Type).Append("  目标：").Append(entry.Version);
                if (before != null) text.Append("  升级前：").Append(VersionText(before[i]));
                text.Append("  当前：").Append(VersionText(version));
            }
            packageInfo.ForeColor = UiColors.Ink;
            SetInfo(text.ToString(), report.Entries.Count * 2);
        }

        private static List<LupVersion> ReadVersions(Lup1Connection connection, PackageReport report, int timeoutMs)
        {
            try { return connection.QueryVersions(report.Entries, timeoutMs); }
            catch (LupDeviceException ex)
            {
                if (ex.Code != 2) throw;
                List<LupVersion> versions = new List<LupVersion>();
                foreach (FirmwareEntry entry in report.Entries)
                    versions.Add(new LupVersion { Type = entry.Type, Status = 2 });
                return versions;
            }
        }

        private static int RemainingQueryTimeout(Stopwatch wait, int waitMs, int queryTimeoutMs)
        {
            long remaining = waitMs - wait.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException("等待超时。");
            return (int)Math.Min(queryTimeoutMs, remaining);
        }

        private void ObserveTask(ObservedTask observed, Lup1Connection initialConnection,
            int initialState, int queryTimeoutMs, int waitMs)
        {
            Stopwatch wait = Stopwatch.StartNew();
            Lup1Connection connection = initialConnection;
            LocalUpgradeSnapshot snapshot = observed.Snapshot;
            try
            {
                while (wait.ElapsedMilliseconds < waitMs)
                {
                    if (shuttingDown) return;
                    try
                    {
                        if (connection == null)
                            connection = Connect(observed.Address, observed.Port, RemainingQueryTimeout(wait, waitMs, queryTimeoutMs));
                        byte state;
                        if (initialState >= 0) { state = (byte)initialState; initialState = -1; }
                        else
                        {
                            try { state = connection.QueryTask(observed.Task, RemainingQueryTimeout(wait, waitMs, queryTimeoutMs)); }
                            catch (LupDeviceException ex)
                            {
                                if (ex.Code != 11) throw;
                                state = 255;
                            }
                        }
                        if (state == 4)
                        {
                            CloseConnection(connection);
                            connection = null;
                            PostToUi(delegate
                            {
                                lastTask = observed; canResume = false;
                                Finish("设备报告本次任务失败。", true);
                            });
                            return;
                        }
                        if (state == 3) observed.Ended = true;
                        if (observed.ReceiptUnconfirmed && state >= 1 && state <= 3)
                        {
                            observed.ReceiptUnconfirmed = false;
                            ReportAccepted();
                        }
                        List<LupVersion> versions = ReadVersions(connection, snapshot.Report,
                            RemainingQueryTimeout(wait, waitMs, queryTimeoutMs));
                        bool matched = true, unsupported = false, mismatch = false;
                        for (int i = 0; i < versions.Count; i++)
                        {
                            if (versions[i].Status == 2) unsupported = true;
                            if (versions[i].Status == 0 &&
                                !String.Equals(versions[i].Value, snapshot.Report.Entries[i].Version, StringComparison.Ordinal))
                                mismatch = true;
                            if (versions[i].Status != 0 ||
                                !String.Equals(versions[i].Value, snapshot.Report.Entries[i].Version, StringComparison.Ordinal))
                                matched = false;
                        }
                        string phase = observed.Ended ? "设备任务已结束" : state == 0 ? "设备接收中" :
                            state == 1 ? "设备等待安装" : state == 2 ? "设备安装中" : "设备任务状态未知";
                        string result = matched ? "版本核对通过" : unsupported ? "部分目标不支持版本查询" :
                            observed.Ended && mismatch ? "版本不一致" : "版本待核对";
                        if (observed.Ended) observed.VersionResult = result;
                        string description = phase + "，" + result + "。" +
                            (matched ? "版本一致不能证明本次重新写入或由本任务导致。" : "") +
                            (observed.ReceiptUnconfirmed ? "本次任务结果未确认。" : "");
                        bool unknownMatched = !observed.Ended && state == 255 && matched;
                        bool complete = matched && (state == 3 || state == 255);
                        if (complete)
                        {
                            CloseConnection(connection);
                            connection = null;
                        }
                        PostToUi(delegate
                        {
                            observed.Latest = versions;
                            LogVersionComparison(observed, versions);
                            if (selectionRevision == observed.CandidateRevision)
                                DisplayVersions(snapshot.Report, versions, observed.Before);
                            UpdateResumeLayout();
                            SetLocalStatus(description, false);
                            if (lastObservedStatus != description)
                            {
                                lastObservedStatus = description;
                                ReportResult(description, false);
                            }
                            if (complete)
                            {
                                lastTask = observed; canResume = unknownMatched;
                                Finish(unknownMatched ?
                                    "设备任务状态未知，版本核对通过；本次任务结果未确认。" :
                                    "设备任务已结束，版本核对通过；版本一致不能证明本次重新写入。", false);
                            }
                        });
                        if (complete) return;
                    }
                    catch (LupDeviceException) { throw; }
                    catch (Exception ex)
                    {
                        if (shuttingDown) return;
                        if (!(ex is IOException || ex is TimeoutException || ex is System.Net.Sockets.SocketException)) throw;
                        CloseConnection(connection);
                        connection = null;
                        if (wait.ElapsedMilliseconds >= waitMs) break;
                        string message = "设备暂不可达或应答无效，等待重新查询：" + ex.Message;
                        if (observed.Ended)
                            message = "设备任务已结束，" + observed.VersionResult + "；" + message;
                        PostToUi(delegate
                        {
                            SetLocalStatus(message, true);
                            if (lastObservedStatus != message) { lastObservedStatus = message; ReportResult(message, true); }
                        });
                    }
                    int remaining = (int)Math.Max(0, waitMs - wait.ElapsedMilliseconds);
                    if (remaining == 0) break;
                    Thread.Sleep(Math.Min(5000, remaining));
                }
                CloseConnection(connection);
                connection = null;
                PostToUi(delegate
                {
                    lastTask = observed; canResume = true;
                    Finish(observed.Ended ? "等待超时；设备任务已结束，" + observed.VersionResult + "。" :
                        observed.ReceiptUnconfirmed ? "等待超时；结束应答未确认，本次任务结果未确认。" :
                        "等待超时；设备可能仍在等待或执行安装。", false);
                });
            }
            catch (Exception ex)
            {
                if (!observed.Ended) throw;
                PostToUi(delegate
                {
                    lastTask = observed; canResume = true;
                    Finish("设备任务已结束，" + observed.VersionResult + "；查询失败：" + ex.Message, true);
                });
            }
            finally { CloseConnection(connection); }
        }

        private void ReportAccepted()
        {
            PostToUi(delegate
            {
                transferActive = false;
                showingTransferProgress = false;
                UpdateTransferProgressVisibility();
                SetLocalStatus("设备已接收并校验，安装状态尚未确认。", false);
                ReportResult("设备已接收并校验，安装状态尚未确认。", false);
            });
        }

        private void KeepUnconfirmedEnd(ObservedTask observed, string reason)
        {
            observed.ReceiptUnconfirmed = true;
            PostToUi(delegate
            {
                lastTask = observed; canResume = true;
                Finish("结束应答未确认：" + reason + "；本次任务结果未确认，可继续等待。", true);
            });
        }

        private void ContinueClicked(object sender, EventArgs e)
        {
            if (busy || remote || !canResume || lastTask == null) return;
            ObservedTask observed = lastTask;
            int queryTimeout = connectTimeoutMs, waitTimeout = waitTimeoutMs;
            busy = true;
            packagePath.Enabled = false; browse.Enabled = false;
            localIp.Enabled = false; localPort.Enabled = false; timeoutSettings.Enabled = false;
            lastObservedStatus = null;
            SetLocalStatus("继续等待：" + observed.Endpoint + " " + Path.GetFileName(observed.Snapshot.Path), false);
            ReportResult("继续等待：" + observed.Endpoint + " " + observed.Snapshot.Path, false);
            UpdateStart();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ObserveTask(observed, null, -1, queryTimeout, waitTimeout);
                }
                catch (Exception ex) { PostToUi(delegate { Finish(ex.Message, true); }); }
            });
        }

        private void StartClicked(object sender, EventArgs e)
        {
            if (busy || remote || !candidateValid) return;
            IPAddress address;
            int port;
            if (!IPAddress.TryParse(localIp.Text.Trim(), out address) ||
                !Int32.TryParse(localPort.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                Finish("请输入有效的设备 IP 和端口。", true);
                return;
            }
            string path = packagePath.Text;
            string endpoint = new IPEndPoint(address, port).ToString();
            int taskCandidateRevision = selectionRevision;
            int queryTimeout = connectTimeoutMs, blockTimeout = blockTimeoutMs;
            int operationTimeout = operationTimeoutMs, waitTimeout = waitTimeoutMs;
            busy = true;
            packagePath.Enabled = false;
            browse.Enabled = false;
            localIp.Enabled = false;
            localPort.Enabled = false;
            timeoutSettings.Enabled = false;
            UpdateStart();
            SetLocalStatus("正在校验升级包。", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                LocalUpgradeSnapshot snapshot;
                try { snapshot = LocalUpgradeSnapshot.Load(path); }
                catch (Exception ex)
                {
                    PostToUi(delegate { SetError("联合包校验失败：" + ex.Message, false); Finish("联合包校验失败：" + ex.Message, true); });
                    return;
                }
                try
                {
                    bool packageApproved = false;
                    if (IsDisposed || !IsHandleCreated || shuttingDown) return;
                    Invoke(new Action(delegate
                    {
                        StringBuilder details = new StringBuilder();
                        foreach (FirmwareEntry entry in snapshot.Report.Entries)
                        {
                            details.AppendLine(entry.Type + "  目标：" + entry.Version);
                            if (entry.Type == FirmwareType.PLC &&
                                (!entry.Version.StartsWith("maya-", StringComparison.Ordinal) ||
                                 Encoding.UTF8.GetByteCount(entry.Version) >= 15))
                                details.AppendLine("当前设备对 PLC 版本格式或查询长度有限制，请确认是否继续。");
                        }
                        string message = "目标设备：" + endpoint + "\r\n将发送以下联合包：\r\n" + snapshot.Path + "\r\n" + details +
                            "\r\n设备已有等待安装的旧包可能被新任务替换，即使新传输失败也可能不再安装。" +
                            "\r\n云端下载可能使设备暂时无响应；超时后请稍后人工重试。确定继续？";
                        packageApproved = MessageBox.Show(FindForm(), message, "确认本地升级", MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                        if (!packageApproved) { Finish("已取消本地升级。", false); return; }
                        transferActive = true;
                    }));
                    if (!packageApproved || shuttingDown) return;
                    PostToUi(delegate { SetLocalStatus("正在查询设备状态和升级前版本。", false); });
                    using (Lup1Connection connection = Connect(address, port, queryTimeout))
                    {
                        PostToUi(delegate { ReportResult("TCP 已连接：" + endpoint, false); });
                        byte deviceStatus = connection.QueryStatus(queryTimeout);
                        if (deviceStatus == 1) throw new InvalidDataException("设备已插枪，请先拔枪。");
                        if (deviceStatus == 2) throw new InvalidDataException("设备状态未知，不能发送升级包。");
                        List<LupVersion> before = ReadVersions(connection, snapshot.Report, queryTimeout);
                        bool approved = false;
                        if (IsDisposed || !IsHandleCreated) return;
                        Invoke(new Action(delegate
                        {
                            DisplayVersions(snapshot.Report, before, null);
                            StringBuilder details = new StringBuilder();
                            for (int i = 0; i < snapshot.Report.Entries.Count; i++)
                            {
                                FirmwareEntry entry = snapshot.Report.Entries[i];
                                details.AppendLine(entry.Type + "  目标：" + entry.Version + "  升级前：" + VersionText(before[i]));
                            }
                            string message = "目标设备：" + endpoint + "\r\n设备升级前版本：\r\n" + details +
                                "\r\n升级前版本相同或未知时，版本一致不能证明本次重新写入。确定发送？";
                            approved = MessageBox.Show(FindForm(), message, "确认设备版本", MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                        }));
                        if (shuttingDown) return;
                        if (!approved)
                        {
                            CloseConnection(connection);
                            PostToUi(delegate { Finish("已取消本地升级。", false); });
                            return;
                        }
                        PostToUi(delegate { ReportResult("开始本地升级：" + snapshot.Path, false); });
                        byte[] task;
                        try { task = connection.BeginPackage(snapshot.Length, operationTimeout); }
                        catch (LupDeviceException) { throw; }
                        catch (Exception ex) { throw new IOException("开始应答未确认，无法查询本次任务：" + ex.Message, ex); }
                        PostToUi(delegate
                        {
                            lastTask = null;
                            canResume = false;
                            lastObservedStatus = null;
                            UpdateResumeLayout();
                            ReportResult("设备已受理升级：" + endpoint, false);
                            SetTransferProgress(0);
                            ReportResult("包传输进度：0%", false);
                        });
                        ObservedTask observed = new ObservedTask
                        {
                            Address = address, Port = port, Snapshot = snapshot, Task = task,
                            Before = before, CandidateRevision = taskCandidateRevision
                        };
                        int lastProgressBucket = 0;
                        for (int offset = 0; offset < snapshot.Length;)
                        {
                            int length = Math.Min(4096, snapshot.Length - offset);
                            connection.SendBlock(task, offset, snapshot.ReadBlock(offset, length), blockTimeout);
                            offset += length;
                            int percent = offset * 100 / snapshot.Length;
                            bool milestone = percent / 10 > lastProgressBucket;
                            lastProgressBucket = percent / 10;
                            PostToUi(delegate
                            {
                                SetTransferProgress(percent);
                                if (milestone)
                                    ReportResult("包传输进度：" + percent + "%", false);
                            });
                        }
                        try { connection.EndPackage(task, operationTimeout); }
                        catch (LupDeviceException) { throw; }
                        catch (InvalidDataException) { throw; }
                        catch (Exception)
                        {
                            CloseConnection(connection);
                            if (shuttingDown) return;
                            Lup1Connection recovery = null;
                            byte recovered;
                            try
                            {
                                recovery = Connect(address, port, queryTimeout);
                                recovered = recovery.QueryTask(task, queryTimeout);
                            }
                            catch (Exception recoveryError)
                            {
                                CloseConnection(recovery);
                                KeepUnconfirmedEnd(observed, recoveryError.Message);
                                return;
                            }
                            using (recovery)
                            {
                                if (recovered == 4)
                                {
                                    CloseConnection(recovery);
                                    PostToUi(delegate { Finish("设备报告本次任务失败。", true); });
                                    return;
                                }
                                if (recovered == 0)
                                {
                                    CloseConnection(recovery);
                                    KeepUnconfirmedEnd(observed, "设备仍在接收，无法确认校验结果");
                                    return;
                                }
                                ReportAccepted();
                                ObserveTask(observed, recovery, recovered, queryTimeout, waitTimeout);
                            }
                            return;
                        }
                        ReportAccepted();
                        ObserveTask(observed, connection, -1, queryTimeout, waitTimeout);
                    }
                }
                catch (Exception ex) { PostToUi(delegate { Finish(ex.Message, true); }); }
            });
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
            if (busy) return;
            string[] files = DroppedFiles(e);
            if (files == null || files.Length != 1) { SetError("请选择一个 BIN 文件。"); return; }
            SelectPackage(files[0]);
        }

        private void SetError(string text)
        {
            SetError(text, true);
        }

        private void SetError(string text, bool report)
        {
            selectionRevision++;
            candidateValid = false;
            UpdateStart();
            packageInfo.ForeColor = UiColors.Error;
            SetInfo(text, 1);
            if (report) ReportResult("升级文件：" + text, true);
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
            candidateValid = true;
            UpdateStart();
        }

        internal void UseGeneratedPackage(string path, PackageReport report)
        {
            if (busy)
            {
                pendingGeneratedPath = path;
                return;
            }
            selectionRevision++;
            packagePath.Text = path;
            DisplayReport(report);
        }

        private void SelectPackage(string path)
        {
            selectionRevision++;
            candidateValid = false;
            UpdateStart();
            try
            {
                if (!IsBin(path)) { SetError("请选择一个存在的 BIN 文件。"); return; }
                string fullPath = Path.GetFullPath(path);
                packagePath.Text = fullPath;
                int revision = ++selectionRevision;
                packageInfo.ForeColor = UiColors.Muted;
                SetInfo("正在校验联合包。", 1);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        PackageReport report = Packager.VerifyFile(fullPath);
                        PostToUi(delegate
                        {
                            if (revision == selectionRevision)
                            {
                                DisplayReport(report);
                                ReportResult("已选择升级文件：" + fullPath, false);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        string message = "已选择 BIN，无法识别或校验为本工具的联合包：" + ex.Message;
                        PostToUi(delegate
                        {
                            if (revision == selectionRevision) SetError(message);
                        });
                    }
                });
            }
            catch (Exception ex) { SetError("无法读取所选文件：" + ex.Message); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ShutdownClient(); tooltips.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

