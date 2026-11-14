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
                    foreach (string prefix in new[] { "Path_", "Name_", "Version_", "Size_", "Select_", "Clear_" })
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
                Control grid = Find(form, "PreviewGrid");
                Assert(!grid.Visible && Find(form, "FtpAddress").Visible, "preview collapsed and FTP visible initially");
                Button preview = (Button)Find(form, "PreviewToggle");
                preview.PerformClick(); Application.DoEvents(); Assert(grid.Visible, "preview opens inline");
                {
                    Assert(((TextBox)Find(form, "FtpFilePath")).Text == "", "empty source before generation");
                    Assert(Find(form, "FtpSourceKind").Text == "选择文件", "empty source gives concise selection hint");
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
                Assert(!Find(form, "FtpSourceKind").Text.Contains("V1"), "FTP mode has no format version");
                Assert(((TextBox)Find(form, "FtpFilePath")).Text == package, "generated package is default FTP source");
                {
                Type formType = form.GetType();
                FieldInfo packageMode = formType.GetField("ftpPackageSource", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert((bool)packageMode.GetValue(form), "generated source retains package verification");
                MethodInfo setSource = formType.GetMethod("SetFtpFile", BindingFlags.Instance | BindingFlags.NonPublic);
                setSource.Invoke(form, new object[] { ordinary, false });
                Assert(((TextBox)Find(form, "FtpFilePath")).Text == ordinary && !(bool)packageMode.GetValue(form), "ordinary source switches upload mode");
                Assert(Find(form, "FtpSourceKind").Text == "本地文件", "ordinary mode visibly disclosed");
                LinkLabel usePackage = (LinkLabel)Find(form, "FtpUsePackage");
                typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(usePackage,
                    new object[] { new LinkLabelLinkClickedEventArgs(usePackage.Links[0]) });
                Assert(((TextBox)Find(form, "FtpFilePath")).Text == package && (bool)packageMode.GetValue(form), "return to generated package restores verification mode");
                    MethodInfo setBusy = formType.GetMethod("SetFtpBusy", BindingFlags.Instance | BindingFlags.NonPublic);
                    string[] guarded = { "FtpUploadButton", "FtpBrowseFile", "FtpUsePackage", "FtpAddress", "FtpUser", "FtpPassword", "BuildButton" };
                    setBusy.Invoke(form, new object[] { true });
                    foreach (string name in guarded) Assert(!Find(form, name).Enabled, "busy disables " + name);
                    setBusy.Invoke(form, new object[] { false });
                    foreach (string name in guarded) Assert(Find(form, name).Enabled, "idle restores " + name);
                    using (Bitmap image = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(work, "inline-ftp-render.png")); }
                }
                Assert(((TextBox)Find(form, "Path_M4")).Text == input && ((TextBox)Find(form, "OutputPath")).Text == package, "FTP roundtrip preserves main window state");
                ((Button)Find(form, "Clear_M4")).PerformClick();

                preview.PerformClick(); Application.DoEvents();
                Directory.CreateDirectory(args[1]);
                using (Bitmap image = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(args[1], "form-render.png")); }
                form.Close();
            }
            Console.WriteLine("PASS UI contract assertions=" + assertions + " (not native interactive GUI validation)"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
