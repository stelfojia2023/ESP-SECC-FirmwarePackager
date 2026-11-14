using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

// In-process form contract and render checks. NOT a substitute for interactive Windows release tests.
internal static class UiContractTests
{
    private static int assertions;
    private static void Assert(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static string Digest(string value)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
    private static Control FindTree(Control root, string name)
    {
        if (root.Name == name) return root;
        foreach (Control child in root.Controls) { Control found = FindTree(child, name); if (found != null) return found; }
        return null;
    }
    private static Control Find(Control root, string name) { return FindTree(root, name); }
    private static void CheckHelpLayout(Form form)
    {
        Control help = Find(form, "HelpButton");
        Control folder = Find(form, "BrowseFolder");
        Control card = Find(form, "AutoLoadCard");
        Control nav = Find(form, "PageNavigation");
        Control pack = Find(form, "PackPageTab");
        Control upgrade = Find(form, "UpgradePageTab");
        Assert(help != null && folder != null && card != null && nav != null && nav.Contains(help) && !card.Contains(help), "help is in page navigation");
        Assert(Find(form, "HelpStrip") == null, "help does not occupy a separate row");
        Assert(help.Text == "! 操作说明" && help.Size == new Size(130, 40), "help uses the requested exclamation and label");
        Assert(pack.Size == new Size(180, 40) && upgrade.Size == pack.Size && Math.Abs(pack.Font.Size - 10.5F) < 0.01F &&
            Math.Abs(upgrade.Font.Size - 10.5F) < 0.01F, "main navigation uses uniform large buttons and text");
        Assert(pack.Font.Bold && !upgrade.Font.Bold, "selected page has stronger text");
        Assert(help.Parent == upgrade.Parent && upgrade.Bounds.Right <= help.Bounds.Left && upgrade.Bounds.Top == help.Bounds.Top,
            "help is immediately after upgrade on the same row");
        TableLayoutPanel folderLine = (TableLayoutPanel)Find(form, "FolderPath").Parent.Parent;
        Assert(folderLine.ColumnCount == 2 && folderLine.GetControlFromPosition(1, 0) == folder, "folder row releases the old help column");
        Assert(help.Parent.ClientRectangle.Contains(help.Bounds), "help fits within its row");
        Rectangle a = help.RectangleToScreen(help.ClientRectangle);
        Rectangle b = folder.RectangleToScreen(folder.ClientRectangle);
        Assert(!a.IntersectsWith(b), "help and folder selection do not overlap");
        Assert(form.RectangleToScreen(form.ClientRectangle).Contains(a), "help is within the window viewport");
        Control firstCard = Find(form, "FirmwareCard_PFC");
        Rectangle navBounds = pack.RectangleToScreen(pack.ClientRectangle);
        Rectangle cardBounds = firstCard.RectangleToScreen(firstCard.ClientRectangle);
        Assert(Math.Abs(navBounds.Left - cardBounds.Left) <= 2 && cardBounds.Top >= navBounds.Bottom &&
            cardBounds.Top - navBounds.Bottom <= 12, "navigation aligns with and sits close to content");
        foreach (Control button in new[] { pack, upgrade, help })
        {
            Size normal = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            using (Font enlarged = new Font(button.Font.FontFamily, button.Font.Size * 1.25F, button.Font.Style))
            {
                Size larger = TextRenderer.MeasureText(button.Text, enlarged, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                Assert(button.ClientSize.Width >= Math.Max(normal.Width, larger.Width) + 12 &&
                    button.ClientSize.Height >= Math.Max(normal.Height, larger.Height) + 4, "navigation caption fits normal and 125 percent font");
            }
        }
    }
    private static void CheckCompactLayout(Form form)
    {
        CheckHelpLayout(form);
        Control typeCard = Find(form, "FirmwareCard_ESP32");
        Label type = null;
        foreach (Control c in typeCard.Controls[0].Controls)
            if (c is Label && c.Text == "ESP32") type = (Label)c;
        Assert(type != null, "ESP32 label exists");
        Size textSize = TextRenderer.MeasureText(type.Text, type.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        Assert(type.ClientSize.Width >= textSize.Width + 4 && type.ClientSize.Height >= textSize.Height, "ESP32 fits on one line");
        using (Font enlarged = new Font(type.Font.FontFamily, type.Font.Size * 1.25F, type.Font.Style))
        {
            Size larger = TextRenderer.MeasureText(type.Text, enlarged, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Assert(type.ClientSize.Width >= larger.Width + 4, "ESP32 has room for enlarged text");
        }
        int buttonWidth = Find(form, "AutoLoadButton").Width;
        foreach (string name in new[] { "AutoLoadButton", "ClearAllButton", "HeaderViewButton", "BuildButton", "FtpDirectoryButton", "FtpUploadButton" })
        {
            Control button = Find(form, name);
            Size caption = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Assert(Math.Abs(button.Width - buttonWidth) <= 1 && button.Width <= button.Height * 5, "compact uniform action " + name);
            Assert(button.ClientSize.Width >= caption.Width + 12 && button.Parent.ClientRectangle.Contains(button.Bounds), "action caption and hit area fit " + name);
        }
        Control header = Find(form, "HeaderViewButton");
        Control build = Find(form, "BuildButton");
        Assert(header.Parent == build.Parent && header.Bounds.Top == build.Bounds.Top &&
            !header.Bounds.IntersectsWith(build.Bounds) && header.Size == build.Size, "header and build share a row and size");
        Control directory = Find(form, "FtpDirectoryButton");
        Control upload = Find(form, "FtpUploadButton");
        Assert(directory.Text == "打开 FTP 目录" && directory.Parent == upload.Parent && directory.Bounds.Top == upload.Bounds.Top &&
            directory.Bounds.Right <= upload.Bounds.Left && directory.Size == upload.Size, "directory button is left of upload and shares its size");
        Assert(Find(form, "PackageCard").Contains(header) && !Find(form, "OperationResultCard").Contains(header),
            "header action is inside the package card");
        Assert(Find(form, "OpenVerifyButton") == null, "standalone package verification button is removed");
        RichTextBox result = (RichTextBox)Find(form, "ResultDetails");
        Assert(result.ClientSize.Height >= result.Font.Height * 10, "compact results still show ten lines: actual=" + result.ClientSize.Height + ", required=" + result.Font.Height * 10);
    }
    private static void TestUiRefinement(Form form)
    {
        Color canvas = form.BackColor;
        Control card = Find(form, "FirmwareCard_PFC");
        Color cardColor = card.Controls[0].BackColor;
        Color fieldColor = Find(form, "Path_PFC").BackColor;
        Assert(cardColor.R > fieldColor.R && fieldColor.R > canvas.R && cardColor.R < 255, "warm, distinct card and field layers");
        Assert(cardColor.R >= cardColor.G && cardColor.G >= cardColor.B && fieldColor.R >= fieldColor.G && fieldColor.G >= fieldColor.B, "card and field are warm neutrals");
        foreach (string type in new[] { "PFC", "LLC", "M4", "PLC", "ESP32" })
        {
            TextBox path = (TextBox)Find(form, "Path_" + type);
            Control hint = Find(form, "DragHint_" + type);
            Assert(path.Text == "" && path.ReadOnly && path.AllowDrop, "empty path retains selection behavior " + type);
            Assert(hint != null && hint.Visible && hint.AllowDrop && hint.Parent == path.Parent &&
                hint.Text == "拖入 BIN 或粘贴路径", "visible drag prompt accepts drops " + type);
            Assert(hint.Width >= 80 && hint.Height >= hint.Font.Height &&
                hint.Parent.GetChildAtPoint(new Point(hint.Left + hint.Width / 2, hint.Top + hint.Height / 2)) == hint,
                "drag prompt is visible above the input " + type + " hint=" + hint.Bounds + " input=" + path.Bounds);
        }
        CheckCompactLayout(form);
        Size original = form.Size;
        try { form.Size = form.MinimumSize; form.PerformLayout(); Application.DoEvents(); CheckCompactLayout(form); }
        finally { form.Size = original; form.PerformLayout(); Application.DoEvents(); }
    }
    private static void TestFilenameWorkflow(Form form, string work)
    {
        Type t = form.GetType();
        MethodInfo load = t.GetMethod("LoadFolder", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo clear = t.GetMethod("ClearAllInputs", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo setFile = t.GetMethod("SetInputFile", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(load != null && clear != null && setFile != null, "filename workflow methods exist");
        clear.Invoke(form, null);
        string folder = Path.Combine(work, "batch-fixtures"); Directory.CreateDirectory(folder);
        string pfc = Path.Combine(folder, "pfc_board_v23_release.BIN");
        File.WriteAllBytes(pfc, new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(folder, "M4_V4.hex"), new byte[] { 1 });
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllBytes(Path.Combine(folder, "nested", "PLC_V9.bin"), new byte[] { 1 });
        load.Invoke(form, new object[] { folder });
        Assert(Find(form, "Path_PFC").Text == pfc && Find(form, "Name_PFC").Text == "pfc_board_v23_release" && Find(form, "Version_PFC").Text == "v23", "batch recognizes stem and preserves version case");
        Assert(!Find(form, "DragHint_PFC").Visible, "selected file replaces drag prompt");
        Assert(Find(form, "Path_M4").Text == "" && Find(form, "Path_PLC").Text == "", "batch ignores wrong extension and nested files");
        File.WriteAllBytes(Path.Combine(folder, "PFC_other_V99.bin"), new byte[] { 1 });
        Find(form, "Path_LLC").Text = pfc; Find(form, "Name_LLC").Text = "old LLC"; Find(form, "Version_LLC").Text = "Vold";
        File.WriteAllBytes(Path.Combine(folder, "LLC_withoutversion.bin"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(folder, "PLC_" + new string('a', 98) + "_V1.bin"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(folder, "ESP32_" + "V" + new string('a', 64) + ".bin"), new byte[] { 1 });
        load.Invoke(form, new object[] { folder });
        Assert(Find(form, "Path_PFC").Text == pfc && Find(form, "Version_PFC").Text == "v23", "duplicate type preserves prior selection");
        Assert(Find(form, "Path_LLC").Text == pfc && Find(form, "Name_LLC").Text == "old LLC" && Find(form, "Version_LLC").Text == "Vold", "missing version preserves prior selection");
        Assert(Find(form, "Path_PLC").Text == "" && Find(form, "Path_ESP32").Text == "", "oversize metadata skipped without truncation");
        Assert(!String.IsNullOrWhiteSpace(Find(form, "ResultDetails").Text), "batch decisions have a visible log");
        object m4Row = null;
        foreach (object row in (System.Collections.IEnumerable)t.GetField("rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form))
            if (row.GetType().GetField("Type").GetValue(row).ToString() == "M4") m4Row = row;
        string manual = Path.Combine(work, "M4_board_V7.8_debug.bin"); File.WriteAllBytes(manual, new byte[] { 2 });
        setFile.Invoke(form, new object[] { m4Row, manual });
        Assert(Find(form, "Name_M4").Text == "M4_board_V7.8_debug" && Find(form, "Version_M4").Text == "V7", "manual selection extracts metadata");
        string noVersion = Path.Combine(work, "M4_manual.bin"); File.WriteAllBytes(noVersion, new byte[] { 3 });
        setFile.Invoke(form, new object[] { m4Row, noVersion });
        Assert(Find(form, "Name_M4").Text == "M4_manual" && Find(form, "Version_M4").Text == "", "manual missing version remains empty, no stale version");
        string limitsFolder = Path.Combine(work, "limits-fixtures"); Directory.CreateDirectory(limitsFolder);
        string[] kinds = { "PFC", "LLC", "M4", "PLC", "ESP32" };
        int[] limits = { 256000, 256000, 524288, 2097152, 2621440 };
        System.Collections.IEnumerable allRows = (System.Collections.IEnumerable)t.GetField("rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        int k = 0;
        foreach (object row in allRows)
        {
            string bound = Path.Combine(limitsFolder, kinds[k] + "_V1.bin");
            File.WriteAllBytes(bound, new byte[limits[k]]);
            setFile.Invoke(form, new object[] { row, bound });
            Assert(Find(form, "Path_" + kinds[k]).Text == bound, "manual exact limit accepted " + kinds[k]);
            string oldName = Find(form, "Name_" + kinds[k]).Text;
            foreach (int length in new[] { 0, limits[k] + 1 })
            {
                File.WriteAllBytes(bound, new byte[length]);
                bool rejected = false;
                try { setFile.Invoke(form, new object[] { row, bound }); }
                catch (TargetInvocationException ex) { if (!(ex.InnerException is InvalidDataException)) throw; rejected = true; }
                Assert(rejected && Find(form, "Name_" + kinds[k]).Text == oldName, "manual invalid size rejected transactionally " + kinds[k]);
            }
            k++;
        }
        load.Invoke(form, new object[] { limitsFolder });
        for (k = 0; k < kinds.Length; k++)
            Assert(Find(form, "Path_" + kinds[k]).Text == Path.Combine(limitsFolder, kinds[k] + "_V1.bin"), "oversized batch preserves old selection " + kinds[k]);
        Assert(Find(form, "ResultDetails").Text.Contains("上限"), "batch oversized reason reported");
        ((Button)Find(form, "ClearAllButton")).PerformClick();
        foreach (string type in new[] { "PFC", "LLC", "M4", "PLC", "ESP32" })
            foreach (string prefix in new[] { "Path_", "Name_", "Version_" })
                Assert(Find(form, prefix + type).Text == "", "clear all resets " + prefix + type);
        foreach (string type in new[] { "PFC", "LLC", "M4", "PLC", "ESP32" })
            Assert(Find(form, "DragHint_" + type).Visible, "drag prompt returns after clear " + type);
        Assert(((DataGridView)Find(form, "PreviewGrid")).Rows.Count == 0, "clear all removes preview rows");
        Assert(File.Exists(pfc) && File.Exists(manual) && File.Exists(noVersion), "clear all does not delete files");
    }
    private static void TestHelpDialog(Form owner, string work)
    {
        Rectangle oldBounds = owner.Bounds;
        string oldOutput = Find(owner, "OutputPath").Text;
        string oldResult = Find(owner, "ResultDetails").Text;
        Exception failure = null; bool inspected = false;
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        using (Timer timer = new Timer())
        {
            timer.Interval = 150;
            timer.Tick += delegate
            {
                Form help = null;
                foreach (Form open in Application.OpenForms) if (open.Text == "操作说明") { help = open; break; }
                if (help == null)
                {
                    if (DateTime.UtcNow < deadline) return;
                    timer.Stop(); failure = new Exception("Help dialog not found within five seconds");
                    Form unexpectedModal = null;
                    foreach (Form open in Application.OpenForms) if (open != owner && open.Modal) { unexpectedModal = open; break; }
                    if (unexpectedModal != null) unexpectedModal.Close();
                    return;
                }
                timer.Stop();
                try
                {
                    TextBox text = (TextBox)Find(help, "HelpText");
                    Assert(help.Modal && text != null && text.ReadOnly, "help is modal and read-only");
                    foreach (string term in new[] { "96/64", "256,000", "524,288", "2,097,152", "2,621,440", "6,291,456", "4096", "0x0FFF", "FTP", "明文", "一键清理" })
                        Assert(text.Text.Contains(term), "help contains " + term);
                    File.WriteAllText(Path.Combine(work, "help-text.txt"), text.Text);
                    using (Bitmap image = new Bitmap(help.Width, help.Height))
                    { help.DrawToBitmap(image, new Rectangle(Point.Empty, help.Size)); image.Save(Path.Combine(work, "help-dialog.png")); }
                    inspected = true;
                }
                catch (Exception ex) { failure = ex; }
                finally { help.Close(); }
            };
            timer.Start();
            ((Button)Find(owner, "HelpButton")).PerformClick();
            timer.Stop();
        }
        if (failure != null) throw failure;
        Assert(inspected, "help dialog inspected before close");
        Assert(owner.Bounds == oldBounds && Find(owner, "OutputPath").Text == oldOutput && Find(owner, "ResultDetails").Text == oldResult,
            "closing help preserves window bounds and state");
    }
    private static void TestHeaderDialog(Form owner)
    {
        Exception failure = null; bool inspected = false;
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        using (Timer timer = new Timer())
        {
            timer.Interval = 100;
            timer.Tick += delegate
            {
                Form viewer = null;
                foreach (Form open in Application.OpenForms) if (open.Name == "HeaderViewer") { viewer = open; break; }
                if (viewer == null)
                {
                    if (DateTime.UtcNow < deadline) return;
                    timer.Stop(); failure = new Exception("Header dialog not found within five seconds");
                    foreach (Form open in Application.OpenForms)
                        if (open != owner && open.Modal) { open.Close(); break; }
                    return;
                }
                timer.Stop();
                try
                {
                    RichTextBox hex = (RichTextBox)Find(viewer, "HeaderHex");
                    Assert(viewer.Text == "", "header viewer has no title text");
                    Assert(viewer.Modal && hex != null && hex.ReadOnly && hex.Lines.Length == 256, "header button opens read-only full header");
                    int glyph = TextRenderer.MeasureText("|", hex.Font, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
                    int right = hex.GetPositionFromCharIndex(hex.Lines[0].Length - 1).X + glyph;
                    Assert(right <= hex.ClientSize.Width && hex.ClientSize.Width - right <= Math.Max(32, hex.Font.Height * 2),
                        "opened header dialog shows full row without excess right margin");
                    inspected = true;
                }
                catch (Exception ex) { failure = ex; }
                finally { viewer.Close(); }
            };
            timer.Start();
            ((Button)Find(owner, "HeaderViewButton")).PerformClick();
            timer.Stop();
        }
        if (failure != null) throw failure;
        Assert(inspected, "header dialog inspected before close");
    }
    private static void TestUpgradePage(Form form, string work, string package)
    {
        Size packageWindow = form.Size;
        string oldOutput = Find(form, "OutputPath").Text;
        string oldFtpFile = Find(form, "FtpFilePath").Text;
        Control page = Find(form, "UpgradePage");
        TextBox file = (TextBox)Find(form, "UpgradePackagePath");
        TextBox info = (TextBox)Find(form, "UpgradePackageInfo");
        Assert(!page.Visible && Find(form, "ContentScroll").Visible, "packaging page is initially selected");
        Assert(file.Text == package, "generated package is automatically associated without reselection");
        Assert(info.Text.Contains("M4") && info.Text.Contains("V000B01D15"), "generated firmware version is shown");
        ((Button)Find(form, "UpgradePageTab")).PerformClick(); Application.DoEvents();
        Assert(page.Visible && !Find(form, "ContentScroll").Visible, "upgrade tab displays its page");
        Assert(Find(form, "HelpButton").Visible && Find(form, "UpgradePageTab").Font.Bold && !Find(form, "PackPageTab").Font.Bold,
            "help remains visible and selected-page emphasis switches");
        Rectangle nav = Find(form, "UpgradePageTab").RectangleToScreen(Find(form, "UpgradePageTab").ClientRectangle);
        Rectangle card = Find(form, "UpgradePackageCard").RectangleToScreen(Find(form, "UpgradePackageCard").ClientRectangle);
        Assert(card.Top >= nav.Bottom && card.Top - nav.Bottom <= 12, "upgrade content sits close to the navigation");
        Assert(form.Width <= 1000 && form.Height <= 560, "upgrade page uses a compact default window");
        Assert(Find(form, "LocalUpgradeSettings").Visible && !Find(form, "RemoteUpgradeSettings").Visible, "local settings selected initially");
        Assert(!Find(form, "StartUpgradeButton").Enabled && Find(form, "UpgradeStatus").Text.Contains("尚未接入"), "unfinished protocol is explicit and disabled");
        Assert(file.ReadOnly && file.AllowDrop && Find(form, "UpgradePackageHint").AllowDrop, "upgrade BIN supports safe drag and selection");
        TextBox localIp = (TextBox)Find(form, "LocalDeviceIp");
        TextBox localPort = (TextBox)Find(form, "LocalDevicePort");
        localIp.Text = "192.0.2.10"; localPort.Text = "12345";
        ((Button)Find(form, "RemoteUpgradeMode")).PerformClick(); Application.DoEvents();
        Assert(Find(form, "RemoteUpgradeSettings").Visible && !Find(form, "LocalUpgradeSettings").Visible, "remote settings replace local settings");
        Assert(Find(form, "UpgradeStatus").Text.Contains("平台") && !Find(form, "StartUpgradeButton").Enabled, "remote API is not faked");
        TextBox endpoint = (TextBox)Find(form, "RemotePlatformAddress");
        TextBox cpid = (TextBox)Find(form, "RemoteCpid");
        TextBox password = (TextBox)Find(form, "RemotePassword");
        endpoint.Text = "https://example.invalid/api"; cpid.Text = "TEST-CPID-001"; password.Text = "synthetic-only";
        Assert(password.UseSystemPasswordChar, "remote platform password is masked");
        ((Button)Find(form, "LocalUpgradeMode")).PerformClick(); Application.DoEvents();
        Assert(localIp.Text == "192.0.2.10" && localPort.Text == "12345", "local settings survive mode change");
        ((Button)Find(form, "RemoteUpgradeMode")).PerformClick(); Application.DoEvents();
        Assert(endpoint.Text == "https://example.invalid/api" && cpid.Text == "TEST-CPID-001" && password.Text == "synthetic-only",
            "remote settings survive mode change");

        MethodInfo select = page.GetType().GetMethod("SelectPackage", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(select != null, "upgrade package selection exists");
        string alternate = Path.Combine(work, "alternate-upgrade.bin");
        File.Copy(package, alternate, true);
        byte[] before = File.ReadAllBytes(alternate);
        select.Invoke(page, new object[] { alternate }); Application.DoEvents();
        Assert(file.Text == alternate && info.Text.Contains("M4") && info.Text.Contains("V000B01D15"), "manual selection replaces generated package");
        Assert(Convert.ToBase64String(File.ReadAllBytes(alternate)) == Convert.ToBase64String(before), "selection does not modify or upload the package");
        string other = Path.Combine(work, "ordinary.txt");
        select.Invoke(page, new object[] { other }); Application.DoEvents();
        Assert(file.Text == alternate && info.Text.Contains("BIN"), "non-BIN selection keeps previous file");
        string unknown = Path.Combine(work, "unknown.bin"); File.WriteAllBytes(unknown, new byte[] { 1, 2, 3 });
        select.Invoke(page, new object[] { unknown }); Application.DoEvents();
        Assert(file.Text == unknown && info.Text.Contains("无法识别"), "unknown BIN can be selected without claiming validity");
        select.Invoke(page, new object[] { package }); Application.DoEvents();
        Assert(file.Text == package && info.Text.Contains("M4"), "valid package can be selected again");

        foreach (string name in new[] { "PackPageTab", "UpgradePageTab", "LocalUpgradeMode", "RemoteUpgradeMode", "StartUpgradeButton" })
        {
            Control c = Find(form, name);
            Size caption = TextRenderer.MeasureText(c.Text, c.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Assert(c.ClientSize.Width >= caption.Width + 12 && c.ClientSize.Height >= caption.Height + 4, "upgrade caption fits " + name);
        }
        Assert(Find(form, "StartUpgradeButton").Size == Find(form, "BuildButton").Size, "upgrade action shares main button size");
        form.Size = form.MinimumSize; form.PerformLayout(); Application.DoEvents();
        Panel scroll = (Panel)Find(form, "UpgradeScroll");
        Control start = Find(form, "StartUpgradeButton");
        scroll.ScrollControlIntoView(start); Application.DoEvents();
        Assert(scroll.RectangleToScreen(scroll.ClientRectangle).Contains(start.RectangleToScreen(start.ClientRectangle)), "upgrade action is reachable at minimum size");
        Assert(info.ClientSize.Height >= info.Font.Height, "upgrade package information is not clipped");
        using (Bitmap image = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(work, "upgrade-remote-render.png")); }
        ((Button)Find(form, "PackPageTab")).PerformClick(); Application.DoEvents();
        Assert(form.Size == packageWindow && Find(form, "ContentScroll").Visible && !page.Visible, "packaging window size and page restored");
        Assert(Find(form, "OutputPath").Text == oldOutput && Find(form, "FtpFilePath").Text == oldFtpFile, "page switching retains packaging and FTP choices");
        ((Button)Find(form, "UpgradePageTab")).PerformClick(); Application.DoEvents();
        Assert(Find(form, "RemoteUpgradeSettings").Visible && cpid.Text == "TEST-CPID-001" && file.Text == package,
            "upgrade page retains mode, package, and configuration");
        ((Button)Find(form, "PackPageTab")).PerformClick(); Application.DoEvents();
    }

    private static void TestResultHistory(Assembly app)
    {
        Type formType = app.GetType("SeccFirmwarePackager.MainForm");
        using (Form form = (Form)Activator.CreateInstance(formType, true))
        {
            form.Show(); Application.DoEvents();
            RichTextBox log = (RichTextBox)Find(form, "ResultDetails");
            Assert(log.ReadOnly && log.MaxLength == Int32.MaxValue && log.TextLength == 0, "history starts empty and has no small text limit");
            MethodInfo append = formType.GetMethod("AppendResult", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo ftp = formType.GetMethod("ReportFtp", BindingFlags.Instance | BindingFlags.NonPublic);
            DateTime before = DateTime.Now.AddSeconds(-1);
            Color normal = Color.FromArgb(48, 51, 48), red = Color.FromArgb(181, 50, 57);
            append.Invoke(form, new object[] { "正常结果", normal });
            ftp.Invoke(form, new object[] { "相同失败", red });
            ftp.Invoke(form, new object[] { "相同失败", red });
            string initial = log.Text;
            Assert(System.Text.RegularExpressions.Regex.Matches(initial,
                @"(?m)^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] ").Count == 3, "every result has a timestamp");
            DateTime stamp;
            Assert(DateTime.TryParseExact(initial.Substring(1, 19), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out stamp) && stamp >= before.AddSeconds(-1) && stamp <= DateTime.Now.AddSeconds(1),
                "timestamps use the local current time");
            int first = initial.IndexOf("正常结果", StringComparison.Ordinal);
            int error = initial.IndexOf("相同失败", StringComparison.Ordinal);
            Assert(first >= 0 && error > first && initial.IndexOf("相同失败", error + 1, StringComparison.Ordinal) > error,
                "repeated identical results are retained in order");
            log.Select(first, "正常结果".Length); Assert(log.SelectionColor.ToArgb() == normal.ToArgb(), "normal history retains its color");
            log.Select(error, "相同失败".Length); Assert(log.SelectionColor.ToArgb() == red.ToArgb(), "error history remains red");
            log.Select(log.TextLength, 0);
            ((Button)Find(form, "UpgradePageTab")).PerformClick(); Application.DoEvents();
            ((Button)Find(form, "PackPageTab")).PerformClick(); Application.DoEvents();
            Assert(log.Text == initial, "switching pages does not clear history");
            ((Button)Find(form, "ClearAllButton")).PerformClick(); Application.DoEvents();
            Assert(log.Text.StartsWith(initial, StringComparison.Ordinal) && log.Text.Contains("已清空五类固件选择"),
                "clearing inputs appends a result instead of clearing history");
            for (int i = 0; i < 500; i++)
                append.Invoke(form, new object[] { "history-" + i + "-" + new string('x', 70), normal });
            Assert(log.TextLength > 32767 && log.Text.Contains("正常结果") && log.Text.Contains("history-499-"),
                "history retains old and new entries beyond 32 KiB");
            form.Close();
        }
        using (Form reopened = (Form)Activator.CreateInstance(formType, true))
        {
            reopened.Show(); Application.DoEvents();
            Assert(((RichTextBox)Find(reopened, "ResultDetails")).TextLength == 0, "closing and reopening starts a fresh session");
            reopened.Close();
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Assembly app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Assert(app.GetName().Version.ToString() == "0.0.2.0" &&
                System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.GetFullPath(args[0])).FileVersion == "0.0.2.0",
                "assembly and file version match release");
            foreach (string sound in new[] { "click", "success", "failure" })
                using (Stream stream = app.GetManifestResourceStream("Sounds." + sound + ".wav"))
                    Assert(stream != null && stream.Length > 44, "embedded sound " + sound);
            using (Form form = (Form)Activator.CreateInstance(app.GetType("SeccFirmwarePackager.MainForm"), true))
            {
                form.Show(); Application.DoEvents();
                Assert(Digest(Find(form, "FtpAddress").Text) == "7ef91abc146ce03ca586e530df0f03a4633df110ebb23e73a4271e5c5375f604", "requested default FTP address");
                Assert(Digest(Find(form, "FtpUser").Text) == "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", "requested default FTP user");
                Assert(Digest(Find(form, "FtpPassword").Text) == "eb2f983bf53fe54df17ad96abf5ebd0bc414f9493647e244fc4c92f86ac11499" &&
                    ((TextBox)Find(form, "FtpPassword")).UseSystemPasswordChar, "requested default FTP password is masked");
                Assert(form.Text.Contains("V0.0.2"), "tool version separated from format");
                Assert(Find(form, "HelpButton") != null && Find(form, "HelpButton").Visible, "in-content help entry exists");
                Control helpButton = Find(form, "HelpButton");
                Assert(helpButton.Parent.ClientRectangle.Contains(helpButton.Bounds), "help button within parent client bounds");
                Rectangle helpInWindow = form.RectangleToClient(helpButton.RectangleToScreen(helpButton.ClientRectangle));
                Assert(form.ClientRectangle.Contains(helpInWindow), "help button within window viewport");
                Assert(helpButton.ClientSize.Width >= 24 && helpButton.ClientSize.Height >= 20, "help button has usable hit area");
                Assert(Find(form, "HeaderViewButton") != null && !Find(form, "HeaderViewButton").Enabled, "header viewer disabled before generation");
                Assert(Find(form, "FtpToggle") == null && Find(form, "FtpDialog") == null, "no FTP toggle or modal");
                Assert(form.GetType().GetField("ftpDialog", BindingFlags.Instance | BindingFlags.NonPublic) == null, "no private FTP dialog");
                Assert(Find(form, "FtpStatus") == null, "FTP status shares bottom result instead of duplicate label");
                Assert(Find(form, "BrandHeader") == null, "no duplicate banner");
                foreach (string type in new[] { "PFC", "LLC", "M4", "PLC", "ESP32" })
                    foreach (string prefix in new[] { "Path_", "Name_", "Version_", "Select_", "Clear_" })
                        Assert(Find(form, prefix + type) != null, "retained input " + prefix + type);
                Assert(Find(form, "SoundEnabled") == null, "no sound selector");
                object sounds = form.GetType().GetField("sounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                PropertyInfo enabled = sounds.GetType().GetProperty("Enabled");
                Assert((bool)enabled.GetValue(sounds, null), "sound enabled by default");
                enabled.SetValue(sounds, false, null); // Test isolation only; not a user control.
                TestUiRefinement(form);
                Control ftpPanel = Find(form, "FtpInlinePanel");
                Control resultCard = Find(form, "OperationResultCard");
                Assert(ftpPanel != null && ftpPanel.Visible && resultCard != null, "inline FTP and result card visible");
                TableLayoutPanel body = (TableLayoutPanel)resultCard.Parent;
                Assert(body.GetColumnSpan(resultCard) == 2, "operation result spans both columns");
                Label status = (Label)form.GetType().GetField("ftpStatus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Color red = Color.FromArgb(181, 50, 57);
                form.GetType().GetMethod("ReportFtp", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { "上传失败：synthetic-test", red });
                RichTextBox resultHistory = (RichTextBox)Find(form, "ResultDetails");
                int failure = resultHistory.Text.LastIndexOf(status.Text, StringComparison.Ordinal);
                Assert(failure >= 0, "FTP error is appended to bottom result");
                resultHistory.Select(failure, status.Text.Length);
                Assert(resultHistory.SelectionColor.ToArgb() == red.ToArgb(), "FTP error retains red text");
                resultHistory.Select(resultHistory.TextLength, 0);
                DataGridView grid = (DataGridView)Find(form, "PreviewGrid");
                Assert(grid.Rows.Count == 0 && !grid.ColumnHeadersVisible, "empty preview has no rows or column headers");
                Assert(Find(form, "PreviewToggle") == null && Find(form, "FtpUsePackage") == null, "removed detail toggle and package shortcut");
                Assert(Find(form, "FtpAddress").Visible, "FTP visible initially");
                Assert(Find(form, "AutoLoadButton").Enabled && Find(form, "FolderPath").Enabled && Find(form, "ClearAllButton").Enabled, "batch loading and clearing enabled");
                foreach (string name in new[] { "AutoLoadButton", "ClearAllButton", "HeaderViewButton", "BuildButton", "FtpDirectoryButton", "FtpUploadButton" })
                    Assert(Find(form, name).Height == 40 && Math.Abs(Find(form, name).Width - Find(form, "AutoLoadButton").Width) <= 1, "uniform primary button " + name + " actual=" + Find(form, name).Size);
                foreach (string type in new[] { "PFC", "LLC", "M4", "PLC", "ESP32" })
                {
                    Assert(Find(form, "Name_" + type).Text == "", "name placeholder not actual input " + type);
                    Assert(Find(form, "Version_" + type).Text == "", "version placeholder not actual input " + type);
                }
                RichTextBox resultBox = (RichTextBox)Find(form, "ResultDetails");
                Assert(resultBox.ClientSize.Height >= resultBox.Font.Height * 10, "results show at least ten text lines");
                {
                    Assert(((TextBox)Find(form, "FtpFilePath")).Text == "", "empty source before generation");
                    Assert(Find(form, "FtpBrowseFile").Visible, "empty source offers file selection");
                    Assert(((TextBox)Find(form, "FtpFilePath")).AllowDrop, "FTP supports file drop");
                    ((TextBox)Find(form, "FtpPassword")).Text = "synthetic-test-only";
                    Assert(((TextBox)Find(form, "FtpPassword")).UseSystemPasswordChar, "password masked");
                }
                {
                    Assert(((TextBox)Find(form, "FtpPassword")).Text == "synthetic-test-only", "inline FTP retains credentials");
                    ((TextBox)Find(form, "FtpPassword")).Clear();
                }
                string work = Path.GetFullPath(args[1]); Directory.CreateDirectory(work);
                string input = Path.Combine(work, "contract-input.bin");
                string package = Path.Combine(work, "contract-package-" + Guid.NewGuid().ToString("N") + ".bin");
                string ordinary = Path.Combine(work, "ordinary.txt");
                File.WriteAllBytes(input, new byte[] { 1, 2, 3, 4 }); File.WriteAllText(ordinary, "synthetic-upload-fixture");
                ((TextBox)Find(form, "Path_M4")).Text = input;
                ((TextBox)Find(form, "Version_M4")).Text = "V000B01D15";
                ((TextBox)Find(form, "OutputPath")).Text = package;
                ((Button)Find(form, "BuildButton")).PerformClick();
                Assert(File.Exists(package), "fixture package generated");
                Assert(Find(form, "HeaderViewButton").Enabled, "successful generation enables header viewing");
                TestHeaderDialog(form);
                Assert(form.GetType().GetMethod("OpenClicked", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
                    form.GetType().GetMethod("DisplayVerifiedFile", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                    "standalone package verification handler is removed");
                MethodInfo readGenerated = form.GetType().GetMethod("ReadGeneratedHeader", BindingFlags.Instance | BindingFlags.NonPublic);
                byte[] generatedBytes = File.ReadAllBytes(package);
                byte[] snapshot = (byte[])readGenerated.Invoke(form, null);
                Assert(snapshot.Length == 4096, "generated header snapshot fixed boundary");
                byte[] changed = (byte[])generatedBytes.Clone(); changed[0] ^= 1; File.WriteAllBytes(package, changed);
                bool headerRejected = false;
                try { readGenerated.Invoke(form, null); } catch (TargetInvocationException ex) { headerRejected = ex.InnerException is IOException || ex.InnerException is InvalidDataException; }
                Assert(headerRejected, "changed header rejected before opening viewer");
                File.WriteAllBytes(package, generatedBytes); File.Delete(package);
                bool missingRejected = false;
                try { readGenerated.Invoke(form, null); } catch (TargetInvocationException ex) { missingRejected = ex.InnerException is IOException || ex.InnerException is InvalidDataException; }
                Assert(missingRejected, "missing generated package rejected before viewing");
                File.WriteAllBytes(package, generatedBytes);
                changed = (byte[])generatedBytes.Clone(); changed[4096] ^= 1; File.WriteAllBytes(package, changed);
                Assert(Convert.ToBase64String((byte[])readGenerated.Invoke(form, null)) == Convert.ToBase64String(snapshot), "header viewer does not claim payload integrity verification");
                File.WriteAllBytes(package, generatedBytes);
                TestUpgradePage(form, work, package);
                string allResults = Find(form, "ResultDetails").Text;
                int resultStart = allResults.LastIndexOf("打包并校验成功", StringComparison.Ordinal);
                int resultEnd = resultStart < 0 ? -1 : allResults.IndexOf("\n[", resultStart, StringComparison.Ordinal);
                string result = resultStart < 0 ? "" : (resultEnd < 0 ? allResults.Substring(resultStart) : allResults.Substring(resultStart, resultEnd - resultStart));
                Assert(result.Contains("打包并校验成功") && result.Contains(package) && result.Contains("联合包大小"), "useful result retained");
                Assert(!result.Contains("包格式") && !result.Contains("包头") && !result.Contains("MD5") && !result.Contains("0x"), "format internals removed from success report");

                Assert(((TextBox)Find(form, "FtpFilePath")).Text == package, "generated package is default FTP source");
                {
                Type formType = form.GetType();
                FieldInfo packageMode = formType.GetField("ftpPackageSource", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert((bool)packageMode.GetValue(form), "generated source retains package verification");
                MethodInfo setSource = formType.GetMethod("SetFtpFile", BindingFlags.Instance | BindingFlags.NonPublic);
                setSource.Invoke(form, new object[] { ordinary, false });
                Assert(((TextBox)Find(form, "FtpFilePath")).Text == ordinary && !(bool)packageMode.GetValue(form), "ordinary source switches upload mode");

                setSource.Invoke(form, new object[] { package, true });
                Assert(((TextBox)Find(form, "FtpFilePath")).Text == package && (bool)packageMode.GetValue(form), "generated source restores verification mode");
                    MethodInfo setBusy = formType.GetMethod("SetFtpBusy", BindingFlags.Instance | BindingFlags.NonPublic);
                    string[] guarded = { "FtpDirectoryButton", "FtpUploadButton", "FtpBrowseFile", "FtpAddress", "FtpUser", "FtpPassword", "BuildButton" };
                    setBusy.Invoke(form, new object[] { true });
                    foreach (string name in guarded) Assert(!Find(form, name).Enabled, "busy disables " + name);
                    setBusy.Invoke(form, new object[] { false });
                    foreach (string name in guarded) Assert(Find(form, name).Enabled, "idle restores " + name);
                    using (Bitmap image = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(work, "inline-ftp-render.png")); }
                }
                Assert(((TextBox)Find(form, "Path_M4")).Text == input && ((TextBox)Find(form, "OutputPath")).Text == package, "FTP roundtrip preserves main window state");
                ((Button)Find(form, "Clear_M4")).PerformClick();

                Application.DoEvents();
                Assert(grid.Rows.Count == 0, "clearing final module removes preview row");
                Directory.CreateDirectory(args[1]);
                using (Bitmap image = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(args[1], "form-render.png")); }
                int selected = 0;
                foreach (string type in new[] { "PFC", "LLC", "M4", "PLC", "ESP32" })
                {
                    string path = Path.Combine(work, type + "_V001B02.bin");
                    File.WriteAllBytes(path, new byte[17 + selected]);
                    object selectedRow = null;
                    foreach (object row in (System.Collections.IEnumerable)form.GetType().GetField("rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form))
                        if (row.GetType().GetField("Type").GetValue(row).ToString() == type) selectedRow = row;
                    form.GetType().GetMethod("SetInputFile", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { selectedRow, path });
                    Assert(Find(form, "Name_" + type).Text == type + "_V001B02" && Find(form, "Version_" + type).Text == "V001B02", "five-file fixture uses real filename extraction " + type);
                    selected++;
                    Assert(grid.Rows.Count == selected, "dynamic preview selected count " + selected);
                }
                string fivePackage = Path.Combine(work, "five-" + Guid.NewGuid().ToString("N") + ".bin");
                ((TextBox)Find(form, "OutputPath")).Text = fivePackage;
                ((Button)Find(form, "BuildButton")).PerformClick();
                Application.DoEvents();
                Assert(File.Exists(fivePackage), "five file fixture generated");
                Assert(Find(form, "UpgradePackagePath").Text == fivePackage && Find(form, "UpgradePackageInfo").Text.Contains("ESP32"),
                    "newly generated five-file package replaces the upgrade selection");
                ((Button)Find(form, "UpgradePageTab")).PerformClick(); Application.DoEvents();
                TextBox upgradeInfo = (TextBox)Find(form, "UpgradePackageInfo");
                Assert(upgradeInfo.Lines.Length == 5 && upgradeInfo.ClientSize.Height >= upgradeInfo.Font.Height * 5,
                    "all five upgrade package versions are visible without vertical clipping");
                using (Font enlarged = new Font(upgradeInfo.Font.FontFamily, upgradeInfo.Font.Size * 1.25F, upgradeInfo.Font.Style))
                    Assert(upgradeInfo.ClientSize.Height >= enlarged.Height * 5, "five upgrade versions fit 125 percent text");
                using (Bitmap image = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(work, "upgrade-five-render.png")); }
                Panel upgradeScroll = (Panel)Find(form, "UpgradeScroll");
                Control upgradeStart = Find(form, "StartUpgradeButton");
                upgradeScroll.ScrollControlIntoView(upgradeStart); Application.DoEvents();
                Assert(upgradeScroll.RectangleToScreen(upgradeScroll.ClientRectangle).Contains(upgradeStart.RectangleToScreen(upgradeStart.ClientRectangle)),
                    "five-file upgrade action remains reachable by scrolling");
                ((Button)Find(form, "PackPageTab")).PerformClick(); Application.DoEvents();
                Assert(grid.Visible && grid.Rows.Count == 5 && grid.DisplayedRowCount(false) >= 5, "all five rows fully displayed");
                int latestReport = resultBox.Text.LastIndexOf("打包并校验成功", StringComparison.Ordinal);
                Assert(latestReport >= 0 && resultBox.Text.Substring(latestReport).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length <= 10,
                    "latest five-file report within ten lines");
                using (Bitmap image = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(work, "five-files-render.png")); }
                Control fullContent = resultCard.Parent;
                using (Bitmap image = new Bitmap(fullContent.Width, fullContent.Height))
                {
                    fullContent.DrawToBitmap(image, new Rectangle(Point.Empty, fullContent.Size));
                    image.Save(Path.Combine(work, "five-files-full-content.png"));
                }
                using (Font enlarged = new Font(grid.Font.FontFamily, grid.Font.Size * 1.25F, grid.Font.Style))
                {
                    Font original = grid.Font; grid.Font = enlarged; Application.DoEvents();
                    foreach (DataGridViewRow row in grid.Rows)
                        Assert(row.Height >= enlarged.Height + 4, "preview row fits 125 percent text");
                    Assert(grid.DisplayedRowCount(false) >= 5, "all five rows visible at 125 percent text");
                    using (Bitmap image = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(work, "five-files-125-text.png")); }
                    grid.Font = original;
                }
                TestHelpDialog(form, work);
                TestFilenameWorkflow(form, work);
                form.Close();
            }
            TestResultHistory(app);
            Console.WriteLine("PASS UI contract assertions=" + assertions + " (not native interactive GUI validation)"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
