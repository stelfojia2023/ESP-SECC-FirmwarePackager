using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SeccFirmwarePackager;

internal static class GuiSmokeTests
{
    private static int result = 1;
    private static string workDir;

    private static T Find<T>(Control root, string name) where T : Control
    {
        Control[] found = root.Controls.Find(name, true);
        if (found.Length != 1 || !(found[0] is T)) throw new Exception("Missing UI control: " + name);
        return (T)found[0];
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
                        File.WriteAllBytes(input, new byte[] { 1, 2, 3, 4, 5 });
                        Find<TextBox>(form, "Path_M4").Text = input;
                        Find<TextBox>(form, "Name_M4").Text = "M4 测试固件.bin";
                        Find<TextBox>(form, "Version_M4").Text = "V000B01D15";
                        Find<TextBox>(form, "OutputPath").Text = output;
                        DataGridView preview = Find<DataGridView>(form, "PreviewGrid");
                        if (preview.Rows.Count != 1 || Convert.ToString(preview.Rows[0].Cells[4].Value) != "0x001000")
                            throw new Exception("Preview did not display the selected firmware and offset");
                        Find<Button>(form, "BuildButton").PerformClick();
                        if (!File.Exists(output)) throw new Exception("Generate button did not create the package");
                        string resultText = Find<TextBox>(form, "ResultDetails").Text;
                        if (!resultText.Contains("打包并校验成功") || !resultText.Contains("V000B01D15") || !resultText.Contains("整包 MD5"))
                            throw new Exception("Generated package details not displayed");
                        form.DisplayVerifiedFile(output);
                        resultText = Find<TextBox>(form, "ResultDetails").Text;
                        if (!resultText.Contains("校验通过") || !resultText.Contains("M4 测试固件.bin"))
                            throw new Exception("View and verification details not displayed");
                        if (Packager.VerifyFile(output).Entries.Count != 1) throw new Exception("GUI package invalid");
                        Find<Button>(form, "Clear_M4").PerformClick();
                        string overBudget = Path.Combine(workDir, "PFC 超预算.bin");
                        string warningOutput = Path.Combine(workDir, "允许超预算.bin");
                        File.WriteAllBytes(overBudget, new byte[250 * 1024 + 1]);
                        Find<TextBox>(form, "Path_PFC").Text = overBudget;
                        Find<TextBox>(form, "Name_PFC").Text = "PFC 超预算.bin";
                        Find<TextBox>(form, "Version_PFC").Text = "V000B01D15";
                        Find<TextBox>(form, "OutputPath").Text = warningOutput;
                        if (!Find<Label>(form, "PreviewStatus").Text.Contains("警告"))
                            throw new Exception("Over-budget advisory not shown in preview");
                        Find<Button>(form, "BuildButton").PerformClick();
                        if (!File.Exists(warningOutput) || Packager.VerifyFile(warningOutput).Warnings.Count != 1)
                            throw new Exception("Advisory over-budget firmware should remain buildable");
                        using (Bitmap picture = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(picture, new Rectangle(0, 0, picture.Width, picture.Height));
                            picture.Save(Path.Combine(workDir, "gui-smoke.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        Console.WriteLine("PASS visible WinForms UI: preview, generate, display, verify and advisory budget warning");
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
