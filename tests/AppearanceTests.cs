using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class AppearanceTests
{
    private static int checks;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr a, IntPtr b);

    private static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static Control Find(Control root, string name)
    {
        Control[] found = root.Controls.Find(name, true);
        if (found.Length != 1) throw new Exception("Missing or duplicate control: " + name);
        return found[0];
    }

    private static Color Token(Assembly app, string name)
    {
        return (Color)app.GetType("SeccFirmwarePackager.UiColors", true).GetField(name).GetValue(null);
    }

    private static void Match(Color actual, string expected, string label)
    {
        Assert(actual.ToArgb() == ColorTranslator.FromHtml(expected).ToArgb(), label + " expected " + expected);
    }

    private static double Luminance(Color c)
    {
        double[] rgb = { c.R / 255.0, c.G / 255.0, c.B / 255.0 };
        for (int i = 0; i < rgb.Length; i++)
            rgb[i] = rgb[i] <= 0.04045 ? rgb[i] / 12.92 : Math.Pow((rgb[i] + 0.055) / 1.055, 2.4);
        return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2];
    }

    private static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static void Select(Form form, string key)
    {
        ((Button)Find(form, "Appearance_" + key)).PerformClick(); Application.DoEvents();
    }

    private static bool Selected(Form form, string key)
    {
        return (Find(form, "Appearance_" + key).AccessibilityObject.State & AccessibleStates.Checked) != 0;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string evidence = Path.GetFullPath(args[1]); Directory.CreateDirectory(evidence);
            string settings = Path.Combine(evidence, "appearance-settings");
            Environment.SetEnvironmentVariable("SECC_PACKAGER_SETTINGS_DIR", settings);
            Application.EnableVisualStyles();
            Assembly app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type formType = app.GetType("SeccFirmwarePackager.MainForm", true);
            if (args.Length > 2 && args[2] == "--check-restart")
            {
                using (Form check = (Form)Activator.CreateInstance(formType, true))
                {
                    check.Show(); Application.DoEvents();
                    Assert(Selected(check, "green"), "new process restores green");
                    Match(check.BackColor, "#E7F1E9", "new process canvas");
                    check.Close();
                }
                Console.WriteLine("PASS new process restores saved appearance");
                return 0;
            }
            string[] names = { "silver", "blue", "purple", "pink", "orange", "yellow", "green" };
            string[] labels = { "银色", "蓝色", "紫色", "粉色", "橙色", "黄色", "绿色" };
            string[] swatchColors = { "#C9CED2", "#B4CEE9", "#C5B9E4", "#EAC0CF", "#F2C6AA", "#ECDC9B", "#B6D8BF" };
            string[] tokens = { "Canvas", "Card", "CardTop", "CardBottom", "Field", "Blue", "SoftBlue", "Border", "ButtonPrimary", "ButtonSecondary", "ButtonIcon" };
            string[,] values = {
                { "#EEEFF1", "#F7F8FA", "#FFFFFF", "#ECEEF1", "#F0F1F3", "#506575", "#E3E9E8", "#B9BEC5", "#DEE6E2", "#E7E9ED", "#F5F6F8" },
                { "#E6EDF7", "#F4F8FD", "#FBFDFF", "#E3EBF6", "#ECF2FA", "#426080", "#D7E5F5", "#AABED4", "#C8DAEE", "#DCE8F5", "#F1F6FC" },
                { "#EDE9F6", "#F8F6FC", "#FDFBFF", "#EEE9F8", "#F2EEF9", "#6B5A80", "#E4DBF2", "#C0B5D2", "#DDD2EE", "#E9E2F3", "#F6F3FB" },
                { "#F7E9ED", "#FCF7F8", "#FFFCFD", "#F6E8EC", "#F9F0F2", "#875B68", "#F1DCE3", "#D2B5BE", "#EECED9", "#F4E1E7", "#FBF4F6" },
                { "#F8EBE2", "#FEF8F4", "#FFFCFA", "#F7E9E0", "#FAF1EA", "#886048", "#F3E1D3", "#D4B9A6", "#EECFB9", "#F4E2D6", "#FCF5F0" },
                { "#F7F1DF", "#FCFAF1", "#FFFDF7", "#F4ECD4", "#FAF6E9", "#796743", "#F1E8CC", "#D1C6A7", "#E9DDB3", "#F3EBD4", "#FBF9EF" },
                { "#E7F1E9", "#F5FAF6", "#FCFFFC", "#E4EFE6", "#EFF6F0", "#496B54", "#D8EADD", "#AEC6B4", "#C9E2CF", "#DEEEE1", "#F3F9F4" }
            };
            using (Form form = (Form)Activator.CreateInstance(formType, true))
            {
                form.Show(); Application.DoEvents();
                Control swatches = Find(form, "AppearanceSwatches");
                Assert(swatches.Controls.Count == 7 && !Find(form, "PageNavigation").Contains(swatches), "seven swatches outside navigation");
                Assert(Selected(form, "silver"), "first use defaults to silver");
                Assert(!File.Exists(Path.Combine(settings, "appearance.txt")), "startup does not write a setting");
                ToolTip tips = (ToolTip)formType.GetField("tooltips", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                for (int i = 0; i < names.Length; i++)
                {
                    Control swatch = Find(form, "Appearance_" + names[i]);
                    Assert(swatch.Visible && swatch.AccessibleRole == AccessibleRole.RadioButton &&
                        swatch.AccessibleName.Contains(labels[i]) && tips.GetToolTip(swatch).Contains(labels[i]), "named accessible swatch " + names[i]);
                    object paletteInfo = swatch.GetType().GetField("Palette").GetValue(swatch);
                    Match((Color)paletteInfo.GetType().GetField("Swatch").GetValue(paletteInfo), swatchColors[i], "swatch " + names[i]);
                }
                Find(form, "OutputPath").Text = Path.Combine(evidence, "retained.bin");
                Find(form, "FtpUser").Text = "synthetic-user";
                RichTextBox log = (RichTextBox)Find(form, "ResultDetails");
                MethodInfo append = formType.GetMethod("AppendResult", BindingFlags.Instance | BindingFlags.NonPublic);
                append.Invoke(form, new object[] { "保留错误", ColorTranslator.FromHtml("#B53239") });
                string oldLog = log.Text;
                int error = oldLog.IndexOf("保留错误", StringComparison.Ordinal);
                log.Select(error, "保留错误".Length);
                MethodInfo busy = formType.GetMethod("SetFtpBusy", BindingFlags.Instance | BindingFlags.NonPublic);
                busy.Invoke(form, new object[] { true });
                for (int i = 0; i < names.Length; i++)
                {
                    Select(form, names[i]);
                    foreach (string name in names)
                        Assert(Selected(form, name) == (name == names[i]), "one selected swatch " + names[i]);
                    for (int n = 0; n < tokens.Length; n++) Match(Token(app, tokens[n]), values[i, n], names[i] + "/" + tokens[n]);
                    Match(form.BackColor, values[i, 0], names[i] + " page");
                    Match(Find(form, "Path_PFC").BackColor, values[i, 4], names[i] + " input");
                    Match(Find(form, "UpgradePackagePath").BackColor, values[i, 4], names[i] + " upgrade input");
                    Match(Find(form, "BuildButton").BackColor, values[i, 8], names[i] + " main button");
                    Match(((DataGridView)Find(form, "PreviewGrid")).BackgroundColor, values[i, 1], names[i] + " grid");
                    Match(((DataGridView)Find(form, "PreviewGrid")).DefaultCellStyle.SelectionBackColor, values[i, 6], names[i] + " grid selection");
                    Assert(!Find(form, "FtpUploadButton").Enabled, "theme switch preserves busy state");
                    using (Form header = (Form)Activator.CreateInstance(app.GetType("SeccFirmwarePackager.HeaderViewer", true), new object[] { new byte[4096] }))
                    {
                        Match(header.BackColor, values[i, 0], names[i] + " header dialog");
                        Match(Find(header, "HeaderHex").BackColor, values[i, 4], names[i] + " header field");
                    }
                    Type resultType = app.GetType("SeccFirmwarePackager.FtpDirectoryResult", true);
                    object directoryResult = Activator.CreateInstance(resultType);
                    resultType.GetField("DirectoryUrl").SetValue(directoryResult, "ftp://example.invalid/");
                    resultType.GetField("Entries").SetValue(directoryResult, new string[0]);
                    using (Form directory = (Form)Activator.CreateInstance(app.GetType("SeccFirmwarePackager.FtpDirectoryViewer", true),
                        new object[] { directoryResult, "" }))
                    {
                        Match(directory.BackColor, values[i, 0], names[i] + " directory dialog");
                        Match(Find(directory, "FtpDirectoryAddress").BackColor, values[i, 4], names[i] + " directory field");
                    }
                    foreach (string foreground in new[] { "Ink", "Muted", "Error", "Success", "Blue" })
                        foreach (string surface in new[] { "Canvas", "Card", "CardBottom", "Field" })
                            Assert(Contrast(Token(app, foreground), Token(app, surface)) >= 4.5,
                                names[i] + " contrast " + foreground + "/" + surface + " = " +
                                Contrast(Token(app, foreground), Token(app, surface)).ToString("F2", CultureInfo.InvariantCulture));
                    Assert(Contrast(Token(app, "Ink"), Token(app, "ButtonPrimary")) >= 4.5 &&
                        Contrast(Token(app, "Ink"), Token(app, "ButtonSecondary")) >= 4.5 &&
                        Contrast(Token(app, "Muted"), Token(app, "ButtonIcon")) >= 4.5, names[i] + " button contrast");
                    Assert(log.Text == oldLog && log.SelectionStart == error && log.SelectionLength == "保留错误".Length &&
                        log.SelectionColor.ToArgb() == ColorTranslator.FromHtml("#B53239").ToArgb(), "history and red selection retained");
                    Assert(Find(form, "OutputPath").Text.EndsWith("retained.bin") && Find(form, "FtpUser").Text == "synthetic-user",
                        "theme does not clear inputs");
                    Assert(form.ClientRectangle.Contains(form.RectangleToClient(swatches.RectangleToScreen(swatches.ClientRectangle))),
                        "swatches fit in the window");
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(evidence, "appearance-" + names[i] + ".png")); }
                }
                busy.Invoke(form, new object[] { false });
                Assert(File.ReadAllText(Path.Combine(settings, "appearance.txt")) == "green", "last choice persisted without other user data");
                ((Button)Find(form, "UpgradePageTab")).PerformClick(); Application.DoEvents();
                Assert(Find(form, "UpgradePage").Visible, "upgrade page remains accessible");
                Match(Find(form, "UpgradePage").BackColor, values[6, 0], "upgrade page uses selected theme");
                form.Size = form.MinimumSize; form.PerformLayout(); Application.DoEvents();
                Assert(form.ClientRectangle.Contains(form.RectangleToClient(Find(form, "AppearanceSwatches").RectangleToScreen(Find(form, "AppearanceSwatches").ClientRectangle))),
                    "swatches fit the minimum upgrade window");
                form.Close();
            }
            using (Form reopened = (Form)Activator.CreateInstance(formType, true))
            {
                reopened.Show(); Application.DoEvents();
                Assert(Selected(reopened, "green"), "restart restores chosen color");
                Match(reopened.BackColor, values[6, 0], "restart restores canvas");
                reopened.Close();
            }
            ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath,
                "\"" + Path.GetFullPath(args[0]) + "\" \"" + evidence + "\" --check-restart");
            start.UseShellExecute = false; start.RedirectStandardOutput = true; start.RedirectStandardError = true; start.CreateNoWindow = true;
            using (Process child = Process.Start(start))
            {
                Assert(child.WaitForExit(30000), "restart verification process finishes");
                string stdout = child.StandardOutput.ReadToEnd(), stderr = child.StandardError.ReadToEnd();
                Assert(child.ExitCode == 0 && stdout.Contains("PASS new process"), "new process restores preference: " + stderr);
            }
            File.WriteAllText(Path.Combine(settings, "appearance.txt"), "unknown-color");
            using (Form invalid = (Form)Activator.CreateInstance(formType, true))
            {
                invalid.Show(); Application.DoEvents();
                Assert(Selected(invalid, "silver"), "invalid preference falls back to silver");
                invalid.Close();
            }
            string blocked = Path.Combine(evidence, "not-a-directory"); File.WriteAllText(blocked, "fixture");
            Environment.SetEnvironmentVariable("SECC_PACKAGER_SETTINGS_DIR", blocked);
            Type paletteType = app.GetType("SeccFirmwarePackager.UiPalette", true);
            object palette = paletteType.GetMethod("Find").Invoke(null, new object[] { "blue" });
            Type settingsType = app.GetType("SeccFirmwarePackager.AppearanceSettings", true);
            Assert(!(bool)settingsType.GetMethod("Save").Invoke(null, new[] { palette }), "unwritable preference is reported");
            Assert(File.ReadAllText(blocked) == "fixture", "failed save does not overwrite unrelated data");
            using (Form failedSave = (Form)Activator.CreateInstance(formType, true))
            using (System.Windows.Forms.Timer dismiss = new System.Windows.Forms.Timer())
            {
                failedSave.Show(); Application.DoEvents();
                bool warned = false; DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                dismiss.Interval = 100;
                dismiss.Tick += delegate
                {
                    IntPtr warning = FindWindow(null, "配色未保存");
                    uint pid;
                    if (warning != IntPtr.Zero && GetWindowThreadProcessId(warning, out pid) != 0 && pid == (uint)Process.GetCurrentProcess().Id)
                    { warned = true; dismiss.Stop(); PostMessage(warning, 0x0010, IntPtr.Zero, IntPtr.Zero); }
                    if (DateTime.UtcNow >= deadline) { dismiss.Stop(); if (warning != IntPtr.Zero) PostMessage(warning, 0x0010, IntPtr.Zero, IntPtr.Zero); }
                };
                dismiss.Start(); Select(failedSave, "blue"); dismiss.Stop();
                Assert(warned && Selected(failedSave, "blue") && Find(failedSave, "ResultDetails").Text.Contains("无法保存"),
                    "failed save preserves current theme and visibly warns");
                failedSave.Close();
            }
            Console.WriteLine("PASS seven palettes, actual controls, contrast, history, preferences and invalid settings; assertions=" + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
