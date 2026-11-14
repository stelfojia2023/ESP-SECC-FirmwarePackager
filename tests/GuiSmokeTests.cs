using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SeccFirmwarePackager;

internal static class GuiSmokeTests
{
    private static int result = 1;
    private static string workDir;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr hwnd, int item);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    private static T Find<T>(Control root, string name) where T : Control
    {
        Control[] found = root.Controls.Find(name, true);
        if (found.Length != 1 || !(found[0] is T)) throw new Exception("Missing UI control: " + name);
        return (T)found[0];
    }

    private static DragEventArgs SendDrag(Control target, string eventName, string[] paths)
    {
        DataObject data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);
        DragEventArgs args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
        MethodInfo handler = typeof(Control).GetMethod(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (handler == null) throw new Exception("Missing drag event method: " + eventName);
        handler.Invoke(target, new object[] { args });
        return args;
    }

    private static void CheckButtonText(MainForm form)
    {
        foreach (string type in new[] { "PFC", "LLC", "M4", "PLC", "ESP32" })
            foreach (string prefix in new[] { "Select_", "Clear_" }) CheckButton(Find<Button>(form, prefix + type));
        foreach (string name in new[] { "BrowseOutput", "BuildButton", "HeaderViewButton", "FtpDirectoryButton", "FtpUploadButton", "AutoLoadButton" })
            CheckButton(Find<Button>(form, name));
    }

    private static void CheckButton(Button button)
    {
        RaisedButton raised = button as RaisedButton;
        if (raised != null && raised.IconOnly)
        {
            if (String.IsNullOrWhiteSpace(button.AccessibleName) || button.Width < 24 || button.Height < 24)
                throw new Exception("Icon button must be accessible and adequately sized: " + button.Name);
            return;
        }
        Size textSize = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (button.ClientSize.Width < textSize.Width + 12 || button.ClientSize.Height < textSize.Height + 6)
            throw new Exception("Button text has insufficient space: " + button.Name + " control=" + button.ClientSize + " text=" + textSize);
        using (Font enlarged = new Font(button.Font.FontFamily, button.Font.Size * 1.25F, button.Font.Style))
        {
            Size larger = TextRenderer.MeasureText(button.Text, enlarged, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (button.ClientSize.Width < larger.Width + 8 || button.ClientSize.Height < larger.Height + 4)
                throw new Exception("Button text lacks space at enlarged font: " + button.Name + " control=" + button.ClientSize + " text=" + larger);
        }
    }

    private static void WaitForStatus(Control status, string expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!status.Text.Contains(expected) && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        if (!status.Text.Contains(expected)) throw new Exception("FTP UI did not report " + expected + ": " + status.Text);
    }

    private static void TestFtpUi(MainForm form, string package)
    {
        FtpAddressBox address = Find<FtpAddressBox>(form, "FtpAddress");
        TextBox user = Find<TextBox>(form, "FtpUser");
        TextBox password = Find<TextBox>(form, "FtpPassword");
        TextBox status = Find<TextBox>(form, "ResultDetails");
        if (!password.UseSystemPasswordChar) throw new Exception("FTP password is not masked");
        MethodInfo start = typeof(MainForm).GetMethod("StartFtpUpload", BindingFlags.Instance | BindingFlags.NonPublic);
        if (start == null) throw new Exception("Missing FTP upload action");
        using (FtpTests.Server server = new FtpTests.Server("test-user", "fake.with.dot", 226))
        {
            address.PasteAddress("ftp://test-user:fake.with.dot@127.0.0.1:" + server.Port + "/upgrade");
            if (user.Text != "test-user" || password.Text != "fake.with.dot" || address.Text.Contains("fake.with.dot") || address.Text.Contains("@"))
                throw new Exception("FTP URL paste did not parse and sanitize credentials");
            bool accepted = false;
            Thread confirm = new Thread(delegate()
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    IntPtr dialog = FindWindow(null, "确认 FTP 上传");
                    uint pid;
                    if (dialog != IntPtr.Zero && GetWindowThreadProcessId(dialog, out pid) != 0 &&
                        pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                    {
                        IntPtr yes = GetDlgItem(dialog, 6);
                        if (yes != IntPtr.Zero) { accepted = true; SendMessage(yes, 0x00F5, IntPtr.Zero, IntPtr.Zero); return; }
                    }
                    Thread.Sleep(20);
                }
            });
            confirm.IsBackground = true;
            confirm.Start();
            Find<Button>(form, "FtpUploadButton").PerformClick();
            if (!accepted) throw new Exception("FTP confirmation did not appear");
            WaitForStatus(status, "上传成功");
            if (server.Uploaded == null || Convert.ToBase64String(server.Uploaded) != Convert.ToBase64String(File.ReadAllBytes(package)))
                throw new Exception("FTP UI upload bytes mismatch");
        }
        using (FtpTests.Server server = new FtpTests.Server("test-user", "fake.with.dot", 451))
        {
            address.PasteAddress("ftp://test-user:fake.with.dot@127.0.0.1:" + server.Port + "/upgrade");
            start.Invoke(form, new object[] { package, address.Text, user.Text, password.Text });
            WaitForStatus(status, "上传失败");
            if (!status.Text.Contains("451") || status.Text.Contains("fake.with.dot"))
                throw new Exception("FTP UI failure was not reported safely");
        }
        address.PasteAddress("ftp://localhost:2121/elsewhere");
        if (user.Text != "" || password.Text != "") throw new Exception("Changing server must clear old credentials");
        address.Clear();
        Console.WriteLine("PASS FTP UI: paste parses and masks credentials, confirm upload, loopback success and failure status, server change clears credentials");
    }

    private static void TestFtpDirectoryUi(MainForm form, string package, bool found)
    {
        FtpAddressBox address = Find<FtpAddressBox>(form, "FtpAddress");
        TextBox status = Find<TextBox>(form, "ResultDetails");
        Button button = Find<Button>(form, "FtpDirectoryButton");
        using (FtpTests.Server server = new FtpTests.Server("test-user", "fake.with.dot", 226))
        {
            string localName = package == null ? null : Path.GetFileName(package);
            string contents = package == null ? "" : (found ? "/upgrade/" + localName + "\r\n" : "other.bin\r\n") + "固件 空格.bin\r\n";
            server.DirectoryData = System.Text.Encoding.UTF8.GetBytes(contents);
            address.PasteAddress("ftp://test-user:fake.with.dot@127.0.0.1:" + server.Port + "/upgrade");
            bool observed = false;
            string viewError = null;
            System.Windows.Forms.Timer inspect = new System.Windows.Forms.Timer { Interval = 20 };
            inspect.Tick += delegate
            {
                Form viewer = Application.OpenForms["FtpDirectoryViewer"];
                if (viewer == null) return;
                try
                {
                    TextBox listing = Find<TextBox>(viewer, "FtpDirectoryEntries");
                    TextBox url = Find<TextBox>(viewer, "FtpDirectoryAddress");
                    Label summary = Find<Label>(viewer, "FtpDirectorySummary");
                    if (!listing.ReadOnly || !url.ReadOnly || url.Text.Contains("fake.with.dot") || url.Text.Contains("@"))
                        throw new Exception("Directory dialog leaks credentials or allows editing");
                    if (package == null && (!summary.Text.Contains("目录为空") || listing.Text.Length != 0))
                        throw new Exception("Empty directory is not clearly reported");
                    if (package != null && (!listing.Text.Contains("固件 空格.bin") ||
                        !summary.Text.Contains(found ? "已找到同名条目" : "未找到同名条目") || !summary.Text.Contains("仅比较名称")))
                        throw new Exception("Directory entries or name-only comparison is wrong");
                    Size before = viewer.Size;
                    viewer.Size = viewer.MinimumSize; viewer.PerformLayout(); Application.DoEvents();
                    if (!listing.Parent.ClientRectangle.Contains(listing.Bounds) || listing.Height < 80 ||
                        summary.Width < TextRenderer.MeasureText(summary.Text, summary.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width)
                        throw new Exception("Directory dialog content is clipped at minimum size");
                    viewer.Size = before; viewer.PerformLayout();
                    if (package != null && found)
                        using (Bitmap image = new Bitmap(viewer.Width, viewer.Height))
                        { viewer.DrawToBitmap(image, new Rectangle(Point.Empty, viewer.Size)); image.Save(Path.Combine(workDir, "ftp-directory.png")); }
                    observed = true;
                }
                catch (Exception e) { viewError = e.Message; }
                finally { inspect.Stop(); viewer.Close(); }
            };
            inspect.Start();
            button.PerformClick();
            if (button.Enabled || Find<Button>(form, "FtpUploadButton").Enabled)
                throw new Exception("FTP buttons must be disabled while directory is loading");
            WaitForStatus(status, "目录读取成功");
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!observed && viewError == null && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            inspect.Dispose();
            if (viewError != null) throw new Exception(viewError);
            if (!observed || !button.Enabled || server.SawWriteCommand || server.ListedPath == null)
                throw new Exception("Directory popup was not completed read-only");
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Need fresh test directory"); return 1; }
        try
        {
            workDir = Path.GetFullPath(args[0]);
            if (Directory.Exists(workDir)) throw new Exception("Test directory already exists");
            Directory.CreateDirectory(workDir);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MainForm form = new MainForm();
            form.Shown += delegate
            {
                form.BeginInvoke(new MethodInvoker(delegate
                {
                    try
                    {
                        string input = Path.Combine(workDir, "M4 固件 文件.bin");
                        string output = Path.Combine(workDir, "联合 固件 包.bin");
                        if (!form.Text.Contains("V0.0.1")) throw new Exception("Tool version must be independent from package format V1");
                        if (form.Controls.Find("FtpToggle", true).Length != 0 || form.Controls.Find("FtpDialog", true).Length != 0)
                            throw new Exception("FTP must be inline without toggle or dialog");
                        if (form.Controls.Find("BrandHeader", true).Length != 0) throw new Exception("Redundant brand banner must not be present");
                        if (form.Controls.Find("SoundEnabled", true).Length != 0) throw new Exception("Sound selector must not be present");
                        FieldInfo soundsField = typeof(MainForm).GetField("sounds", BindingFlags.Instance | BindingFlags.NonPublic);
                        UiSounds feedback = (UiSounds)soundsField.GetValue(form);
                        if (!feedback.Enabled) throw new Exception("Audio feedback must be enabled by default");
                        feedback.Enabled = false; // Test isolation only.
                        if (Find<DataGridView>(form, "PreviewGrid").Rows.Count != 0 || !Find<Control>(form, "FtpAddress").Visible)
                            throw new Exception("Preview must start empty and inline FTP visible");
                        if (form.Controls.Find("PreviewToggle", true).Length != 0 || form.Controls.Find("FtpUsePackage", true).Length != 0)
                            throw new Exception("Obsolete detail toggle or package shortcut remains");
                        if (!Find<Button>(form, "AutoLoadButton").Enabled)
                            throw new Exception("Automatic loading must be available");
                        if (Find<TextBox>(form, "FtpFilePath").Text != "")
                            throw new Exception("FTP source before generation must be empty");
                        TestFtpDirectoryUi(form, null, false);
                        Application.DoEvents();
                        CheckButtonText(form);
                        foreach (string name in new[] { "BrowseOutput", "BuildButton", "HeaderViewButton" })
                        {
                            Control control = Find<Control>(form, name);
                            if (!control.Visible || control.Bounds.Bottom > control.Parent.ClientSize.Height || control.Height < 18)
                                throw new Exception("Output control is clipped or not visible: " + name);
                        }
                        File.WriteAllBytes(input, new byte[] { 1, 2, 3, 4, 5 });
                        TextBox pathBox = Find<TextBox>(form, "Path_M4");
                        if (!pathBox.AllowDrop || !pathBox.ReadOnly) throw new Exception("Path box must allow drag and prevent unvalidated typing");
                        if (SendDrag(pathBox, "OnDragEnter", new[] { input }).Effect != DragDropEffects.Copy)
                            throw new Exception("Single file was not accepted for drag");
                        if (SendDrag(pathBox, "OnDragEnter", new[] { input, input }).Effect != DragDropEffects.None)
                            throw new Exception("Multiple files must not be accepted by one input");
                        if (!pathBox.Parent.AllowDrop || SendDrag(pathBox.Parent, "OnDragEnter", new[] { input }).Effect != DragDropEffects.Copy)
                            throw new Exception("The entire path field must accept a file drop");
                        Label dragHint = Find<Label>(form, "DragHint_M4");
                        if (!dragHint.Visible || !dragHint.AllowDrop ||
                            SendDrag(dragHint, "OnDragEnter", new[] { input }).Effect != DragDropEffects.Copy ||
                            SendDrag(dragHint, "OnDragEnter", new[] { input, input }).Effect != DragDropEffects.None)
                            throw new Exception("Visible drag prompt must accept exactly one file");
                        SendDrag(dragHint, "OnDragDrop", new[] { input });
                        if (pathBox.Text != input || dragHint.Visible)
                            throw new Exception("Drop on visible prompt did not populate the input");
                        SendDrag(pathBox, "OnDragDrop", new[] { input });
                        if (pathBox.Text != input || Find<TextBox>(form, "Name_M4").Text != Path.GetFileNameWithoutExtension(input))
                            throw new Exception("Drop did not populate path and default name");
                        string second = Path.Combine(workDir, "另一个 固件.bin");
                        File.WriteAllBytes(second, new byte[] { 6, 7, 8 });
                        Find<TextBox>(form, "Name_M4").Text = "自定义 名称.bin";
                        pathBox.Text = second;
                        if (Find<TextBox>(form, "Name_M4").Text != "自定义 名称.bin")
                            throw new Exception("Manual path entry must preserve a customized firmware name");
                        SendDrag(pathBox, "OnDragDrop", new[] { input });
                        Find<TextBox>(form, "Name_M4").Text = "M4 测试固件.bin";
                        Find<TextBox>(form, "Version_M4").Text = "V000B01D15";
                        Find<TextBox>(form, "OutputPath").Text = output;
                        DataGridView preview = Find<DataGridView>(form, "PreviewGrid");
                        if (preview.Rows.Count != 1 || Convert.ToString(preview.Rows[0].Cells["Offset"].Value) != "偏移：0x00001000")
                            throw new Exception("Preview did not display the selected firmware and offset");
                        Find<Button>(form, "BuildButton").PerformClick();
                        if (!File.Exists(output)) throw new Exception("Generate button did not create the package");
                        string resultText = Find<TextBox>(form, "ResultDetails").Text;
                        if (!resultText.Contains("打包并校验成功") || !resultText.Contains("V000B01D15") || !resultText.Contains("联合包大小"))
                            throw new Exception("Generated package details not displayed");
                        if (Packager.VerifyFile(output).Entries.Count != 1) throw new Exception("GUI package invalid");
                        Find<Button>(form, "Clear_M4").PerformClick();
                        if (preview.Rows.Count != 0 || preview.Visible) throw new Exception("Cleared preview must hide without empty rows");
                        string overBudget = Path.Combine(workDir, "PFC 超限.bin");
                        File.WriteAllBytes(overBudget, new byte[250 * 1024 + 1]);
                        // Bypass the read-only control programmatically to exercise final Plan guard.
                        Find<TextBox>(form, "Path_PFC").Text = overBudget;
                        Find<TextBox>(form, "Name_PFC").Text = "PFC 超限";
                        Find<TextBox>(form, "Version_PFC").Text = "V000B01D15";
                        if (!Find<TextBox>(form, "ResultDetails").Text.Contains("上限"))
                            throw new Exception("Oversized file rejection must be visible before generation");
                        form.Refresh();
                        Application.DoEvents();
                        using (Bitmap picture = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(picture, new Rectangle(0, 0, picture.Width, picture.Height));
                            picture.Save(Path.Combine(workDir, "gui-smoke.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        Find<Button>(form, "Clear_PFC").PerformClick();
                        string[] types = { "PFC", "LLC", "M4", "PLC", "ESP32" };
                        int[] sizes = { 1, 4097, 8193, 137, 4095 };
                        for (int i = 0; i < types.Length; i++)
                        {
                            string file = Path.Combine(workDir, types[i] + "_test.bin");
                            File.WriteAllBytes(file, new byte[sizes[i]]);
                            Find<TextBox>(form, "Path_" + types[i]).Text = file;
                            Find<TextBox>(form, "Version_" + types[i]).Text = "V000B01D15";
                            if (preview.Rows.Count != i + 1) throw new Exception("Preview must grow one row per selected firmware");
                        }
                        if (preview.Rows.Count != 5 || preview.DisplayedRowCount(false) < 5)
                            throw new Exception("The full five-file preview must be visible; rows=" + preview.Rows.Count + ", displayed=" + preview.DisplayedRowCount(false) + ", grid=" + preview.ClientSize);
                        string allOutput = Path.Combine(workDir, "all-five.bin");
                        Find<TextBox>(form, "OutputPath").Text = allOutput;
                        Find<Button>(form, "BuildButton").PerformClick();
                        if (Packager.VerifyFile(allOutput).Entries.Count != 5)
                            throw new Exception("GUI failed to build a five-file package");
                        TextBox fiveReport = Find<TextBox>(form, "ResultDetails");
                        if (fiveReport.Text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length > 10)
                            throw new Exception("Five-firmware success report must fit ten lines");
                        TestFtpUi(form, allOutput);
                        TestFtpDirectoryUi(form, allOutput, true);
                        TestFtpDirectoryUi(form, allOutput, false);
                        Console.WriteLine("PASS FTP directory UI: no local file, empty, matched and missing names, sizing and credential masking");
                        form.Refresh();
                        Application.DoEvents();
                        using (Bitmap picture = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(picture, new Rectangle(0, 0, picture.Width, picture.Height));
                            picture.Save(Path.Combine(workDir, "gui-five.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        string retainedFtpAddress = Find<TextBox>(form, "FtpAddress").Text;
                        {
                            if (Find<TextBox>(form, "FtpAddress").Text != retainedFtpAddress)
                                throw new Exception("Inline FTP must retain its state");
                        }
                        form.PerformLayout();
                        Application.DoEvents();
                        if (Find<TextBox>(form, "FtpAddress").Text != retainedFtpAddress || preview.Rows.Count != 5)
                            throw new Exception("Relayout must retain FTP and preview data");
                        form.Size = form.MinimumSize;
                        form.PerformLayout();
                        Application.DoEvents();
                        CheckButtonText(form);
                        Panel scroll = Find<Panel>(form, "ContentScroll");
                        Button buildButton = Find<Button>(form, "BuildButton");
                        if (!scroll.VerticalScroll.Visible) throw new Exception("Small windows must provide vertical scrolling");
                        scroll.ScrollControlIntoView(buildButton);
                        Application.DoEvents();
                        Rectangle view = scroll.RectangleToScreen(scroll.ClientRectangle);
                        Rectangle button = buildButton.RectangleToScreen(buildButton.ClientRectangle);
                        if (button.Top < view.Top || button.Bottom > view.Bottom)
                            throw new Exception("Build button is not reachable in the small window");
                        Console.WriteLine("PASS visible WinForms UI: button text spacing at default and minimum sizes, 125 percent font metrics, drag handlers, manual input, five-file preview, generate, verify, hard-limit rejection and small window");
                        result = 0;
                    }
                    catch (Exception ex) { Console.Error.WriteLine("FAIL GUI " + ex); result = 1; }
                    finally { form.Close(); }
                }));
            };
            Application.Run(form);
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL GUI " + ex); result = 1; }
        return result;
    }
}

