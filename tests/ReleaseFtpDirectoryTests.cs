using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// Exercises the standalone delivered EXE, using only a synthetic loopback FTP server.
internal static class ReleaseFtpDirectoryTests
{
    private const uint WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, WM_CLOSE = 0x0010, BM_CLICK = 0x00F5;
    private delegate bool EnumProc(IntPtr hwnd, IntPtr extra);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private sealed class Node { public IntPtr Handle; public string ClassName; public string Text; public Rect Bounds; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr extra);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern IntPtr SendText(IntPtr window, uint message, IntPtr length, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern IntPtr GetText(IntPtr window, uint message, IntPtr length, StringBuilder text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern IntPtr SendNumber(IntPtr window, uint message, IntPtr a, IntPtr b);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr a, IntPtr b);

    private static Node Describe(IntPtr handle)
    {
        StringBuilder kind = new StringBuilder(256);
        GetClassName(handle, kind, kind.Capacity);
        int count = Math.Min(65535, Math.Max(0, (int)SendNumber(handle, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero)));
        StringBuilder text = new StringBuilder(Math.Max(2, count + 1));
        GetText(handle, WM_GETTEXT, (IntPtr)text.Capacity, text);
        Rect bounds; GetWindowRect(handle, out bounds);
        return new Node { Handle = handle, ClassName = kind.ToString(), Text = text.ToString(), Bounds = bounds };
    }

    private static List<Node> Children(IntPtr root)
    {
        List<Node> nodes = new List<Node>();
        EnumChildWindows(root, delegate(IntPtr h, IntPtr ignored) { nodes.Add(Describe(h)); return true; }, IntPtr.Zero);
        return nodes;
    }

    private static IntPtr Window(uint pid, string title)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr ignored)
        {
            uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner != pid || !IsWindowVisible(h)) return true;
            StringBuilder text = new StringBuilder(256); GetWindowText(h, text, text.Capacity);
            if (text.ToString() == title) { result = h; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static void WaitFor(Func<bool> condition, string message)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 15000) { if (condition()) return; Thread.Sleep(50); }
        throw new Exception(message);
    }

    private static Node Button(IntPtr root, string caption)
    {
        foreach (Node node in Children(root))
            if (node.ClassName.IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0 && node.Text == caption) return node;
        throw new Exception("Missing button: " + caption);
    }

    private static Node LabeledEdit(IntPtr root, string labelText)
    {
        List<Node> nodes = Children(root);
        Node label = null;
        foreach (Node node in nodes) if (node.Text == labelText) { label = node; break; }
        if (label == null) throw new Exception("Missing FTP field label");
        foreach (Node node in nodes)
            if (node.ClassName.IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) >= 0 && IsWindowVisible(node.Handle) &&
                node.Bounds.Left >= label.Bounds.Right && Math.Abs((node.Bounds.Top + node.Bounds.Bottom) - (label.Bounds.Top + label.Bounds.Bottom)) < 20)
                return node;
        throw new Exception("Missing FTP field");
    }

    private static string Digest(string value)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }

    private static bool Contains(IntPtr root, string text)
    {
        foreach (Node node in Children(root)) if (node.Text.IndexOf(text, StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    private static void Click(IntPtr control)
    {
        if (!PostMessage(control, BM_CLICK, IntPtr.Zero, IntPtr.Zero)) throw new Exception("Button click failed");
    }

    public static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Need standalone EXE"); return 1; }
        Process process = null;
        try
        {
            string exe = Path.GetFullPath(args[0]);
            string[] files = Directory.GetFiles(Path.GetDirectoryName(exe));
            if (files.Length != 1 || files[0] != exe) throw new Exception("Standalone directory must contain only the EXE");
            process = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
            WaitFor(delegate { process.Refresh(); return process.MainWindowHandle != IntPtr.Zero; }, "Standalone window did not appear");
            IntPtr root = process.MainWindowHandle; uint pid = (uint)process.Id;
            Node directory = Button(root, "打开 FTP 目录"), upload = Button(root, "上传到 FTP");
            if (!IsWindowVisible(directory.Handle) || directory.Bounds.Right > upload.Bounds.Left ||
                directory.Bounds.Right - directory.Bounds.Left != upload.Bounds.Right - upload.Bounds.Left ||
                directory.Bounds.Bottom - directory.Bounds.Top != upload.Bounds.Bottom - upload.Bounds.Top)
                throw new Exception("Directory button placement or size is incorrect");
            Node address = LabeledEdit(root, "URL");
            Node user = LabeledEdit(root, "用户名");
            Node password = LabeledEdit(root, "密码");
            if (address.Text != "" ||
                user.Text != "" ||
                password.Text != "" ||
                SendNumber(password.Handle, 0x00D2, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero)
                throw new Exception("Standalone FTP defaults or password masking are incorrect");
            using (FtpTests.Server server = new FtpTests.Server("release-user", "synthetic-secret", 226))
            {
                server.DirectoryData = Encoding.UTF8.GetBytes("package.bin\r\n固件 空格.bin\r\n");
                string value = "ftp://release-user:synthetic-secret@127.0.0.1:" + server.Port + "/upgrade";
                if (SendText(address.Handle, WM_SETTEXT, IntPtr.Zero, value) == IntPtr.Zero) throw new Exception("Cannot set synthetic FTP URL");
                Click(directory.Handle);
                IntPtr popup = IntPtr.Zero;
                WaitFor(delegate { popup = Window(pid, "FTP 目录"); return popup != IntPtr.Zero; }, "Directory popup did not appear");
                if (!Contains(popup, "package.bin") || !Contains(popup, "固件 空格.bin") || !Contains(popup, "目录共 2 个条目") ||
                    Contains(popup, "synthetic-secret") || Contains(popup, "release-user") || server.SawWriteCommand || server.ListedPath == null)
                    throw new Exception("Standalone directory listing is incomplete, leaked credentials, or performed a write");
                if (Describe(address.Handle).Text.Contains("synthetic-secret")) throw new Exception("FTP address retains password");
                if (!PostMessage(popup, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)) throw new Exception("Cannot close directory popup");
                WaitFor(delegate { return Window(pid, "FTP 目录") == IntPtr.Zero; }, "Directory popup did not close");
            }
            using (FtpTests.Server server = new FtpTests.Server("correct-user", "synthetic-secret", 226))
            {
                string value = "ftp://wrong-user:synthetic-secret@127.0.0.1:" + server.Port + "/upgrade";
                if (SendText(address.Handle, WM_SETTEXT, IntPtr.Zero, value) == IntPtr.Zero) throw new Exception("Cannot set failure test URL");
                Click(directory.Handle);
                IntPtr popup = IntPtr.Zero;
                WaitFor(delegate { popup = Window(pid, "打开 FTP 目录失败"); return popup != IntPtr.Zero; }, "Failure dialog did not appear");
                if (!Contains(popup, "认证失败") || Contains(popup, "synthetic-secret") || server.SawWriteCommand || server.ListedPath != null)
                    throw new Exception("Standalone directory error is incorrect or leaked credentials");
                PostMessage(popup, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                WaitFor(delegate { return Window(pid, "打开 FTP 目录失败") == IntPtr.Zero; }, "Failure dialog did not close");
            }
            process.CloseMainWindow();
            if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new Exception("Standalone process did not exit cleanly");
            Console.WriteLine("PASS standalone EXE: read-only loopback FTP directory, Unicode names, sanitized URL, auth failure, uniform button placement");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL standalone FTP directory " + e.Message); return 1; }
        finally { if (process != null) { process.Refresh(); if (!process.HasExited) process.Kill(); process.Dispose(); } }
    }
}
