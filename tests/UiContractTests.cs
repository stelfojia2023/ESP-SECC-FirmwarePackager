using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// In-process form contract and render checks. NOT a substitute for interactive Windows release tests.
internal static class UiContractTests
{
    private static int assertions;
    private static void Assert(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static Control FindTree(Control root, string name)
    {
        if (root.Name == name) return root;
        foreach (Control child in root.Controls) { Control found = FindTree(child, name); if (found != null) return found; }
        return null;
    }
    private static Control Find(Control root, string name) { return FindTree(root, name); }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Assembly app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            foreach (string sound in new[] { "click", "success", "failure" })
                using (Stream stream = app.GetManifestResourceStream("Sounds." + sound + ".wav"))
                    Assert(stream != null && stream.Length > 44, "embedded sound " + sound);
            using (Form form = (Form)Activator.CreateInstance(app.GetType("SeccFirmwarePackager.MainForm"), true))
            {
                form.Show(); Application.DoEvents();
                Assert(form.Text.Contains("V0.0.1"), "tool version separated from format");
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
                Control ftpPanel = Find(form, "FtpInlinePanel");
                Control resultCard = Find(form, "OperationResultCard");
                Assert(ftpPanel != null && ftpPanel.Visible && resultCard != null, "inline FTP and result card visible");
                TableLayoutPanel body = (TableLayoutPanel)resultCard.Parent;
                Assert(body.GetColumnSpan(resultCard) == 2, "operation result spans both columns");
                Label status = (Label)form.GetType().GetField("ftpStatus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                status.ForeColor = Color.FromArgb(181, 50, 57); status.Text = "上传失败：synthetic-test";
                Assert(Find(form, "ResultDetails").Text == status.Text && Find(form, "ResultDetails").ForeColor == status.ForeColor,
                    "FTP errors synchronize to red bottom result");
                DataGridView grid = (DataGridView)Find(form, "PreviewGrid");
                Assert(grid.Rows.Count == 0 && !grid.ColumnHeadersVisible, "empty preview has no rows or column headers");
                Assert(Find(form, "PreviewToggle") == null && Find(form, "FtpUsePackage") == null, "removed detail toggle and package shortcut");
                Assert(Find(form, "FtpAddress").Visible, "FTP visible initially");
                Assert(!Find(form, "AutoLoadButton").Enabled && Find(form, "FolderPath") != null, "automatic loading explicitly disabled");
                foreach (string name in new[] { "AutoLoadButton", "OpenVerifyButton", "BuildButton", "FtpUploadButton" })
                    Assert(Find(form, name).Size == new Size(180, 40), "uniform primary button " + name + " actual=" + Find(form, name).Size);
                foreach (string type in new[] { "PFC", "LLC", "M4", "PLC", "ESP32" })
                {
                    Assert(Find(form, "Name_" + type).Text == "", "name placeholder not actual input " + type);
                    Assert(Find(form, "Version_" + type).Text == "", "version placeholder not actual input " + type);
                }
                TextBox resultBox = (TextBox)Find(form, "ResultDetails");
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
                string result = Find(form, "ResultDetails").Text;
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
                    string[] guarded = { "FtpUploadButton", "FtpBrowseFile", "FtpAddress", "FtpUser", "FtpPassword", "BuildButton" };
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
                    string path = Path.Combine(work, type + "-five.bin");
                    File.WriteAllBytes(path, new byte[17 + selected]);
                    ((TextBox)Find(form, "Path_" + type)).Text = path;
                    ((TextBox)Find(form, "Version_" + type)).Text = "V000B01D15";
                    selected++;
                    Assert(grid.Rows.Count == selected, "dynamic preview selected count " + selected);
                }
                string fivePackage = Path.Combine(work, "five-" + Guid.NewGuid().ToString("N") + ".bin");
                ((TextBox)Find(form, "OutputPath")).Text = fivePackage;
                ((Button)Find(form, "BuildButton")).PerformClick();
                Application.DoEvents();
                Assert(File.Exists(fivePackage), "five file fixture generated");
                Assert(grid.Visible && grid.Rows.Count == 5 && grid.DisplayedRowCount(false) >= 5, "all five rows fully displayed");
                Assert(resultBox.Text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length <= 10, "five-file report within ten lines");
                using (Bitmap image = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(work, "five-files-render.png")); }
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
                form.Close();
            }
            Console.WriteLine("PASS UI contract assertions=" + assertions + " (not native interactive GUI validation)"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
