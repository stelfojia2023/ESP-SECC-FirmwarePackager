using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Reports whether the desktop clipboard can serve the file-drop and text
// payloads the interactive tests need. A warning here is an environment
// problem, not a product failure.
internal static class ClipboardPreflight
{
    private const int ClipboardCannotOpen = unchecked((int)0x800401D0);

    [STAThread]
    public static int Main(string[] args)
    {
        int rounds = 10;
        if (args.Length > 0) Int32.TryParse(args[0], out rounds);
        if (rounds < 1) rounds = 10;
        string directory = Path.Combine(Path.GetTempPath(), "secc-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "a.bin");
        string second = Path.Combine(directory, "b.bin");
        File.WriteAllBytes(first, new byte[] { 1 });
        File.WriteAllBytes(second, new byte[] { 2 });
        int fileFail = 0, fileStale = 0, textFail = 0, textStale = 0;
        try
        {
            for (int round = 0; round < rounds; round++)
            {
                System.Collections.Specialized.StringCollection files =
                    new System.Collections.Specialized.StringCollection();
                files.Add(first); files.Add(second);
                try
                {
                    Clipboard.SetFileDropList(files);
                    System.Collections.Specialized.StringCollection current = Clipboard.GetFileDropList();
                    if (current.Count != 2 || current[0] != first || current[1] != second) fileStale++;
                }
                catch (ExternalException ex)
                {
                    if (ex.HResult == ClipboardCannotOpen) fileFail++; else throw;
                }
                try
                {
                    string expected = "preflight-" + round;
                    Clipboard.SetText(expected);
                    if (Clipboard.GetText() != expected) textStale++;
                }
                catch (ExternalException ex)
                {
                    if (ex.HResult == ClipboardCannotOpen) textFail++; else throw;
                }
            }
        }
        finally
        {
            try { Clipboard.Clear(); } catch (ExternalException) { }
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
        Console.WriteLine("PREFLIGHT clipboard file-drop: open-fail=" + fileFail + "/" + rounds +
            " stale=" + fileStale + "/" + rounds);
        Console.WriteLine("PREFLIGHT clipboard text: open-fail=" + textFail + "/" + rounds +
            " stale=" + textStale + "/" + rounds);
        if (fileFail + fileStale + textFail + textStale == 0)
        {
            Console.WriteLine("PREFLIGHT OK");
            return 0;
        }
        Console.WriteLine("PREFLIGHT WARN: 剪贴板存在系统级争用。请关闭剪贴板工具/同步软件，确认桌面已解锁后重试；该结果不代表产品缺陷。");
        return 0;
    }
}
