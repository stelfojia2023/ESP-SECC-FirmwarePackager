using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SeccFirmwarePackager;

// The public window is exercised against local-only test inputs and, in later
// cases, a loopback LUP1 server. It does not validate a physical device.
internal static class LocalUpgradeUiTests
{
    private static int versionConfirmationCount;
    private static int rejectNextVersionConfirmation;
    private static string versionConfirmationText;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr hwnd, int item);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);

    private static void StartVersionConfirmationHelper()
    {
        Thread helper = new Thread(delegate()
        {
            while (true)
            {
                IntPtr dialog = FindWindow(null, "确认设备版本");
                uint pid;
                if (dialog != IntPtr.Zero && GetWindowThreadProcessId(dialog, out pid) != 0 &&
                    pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                {
                    bool reject = Interlocked.CompareExchange(ref rejectNextVersionConfirmation, 0, 0) == 1;
                    int buttonId = reject ? 7 : 6;
                    IntPtr button = GetDlgItem(dialog, buttonId);
                    if (button != IntPtr.Zero)
                    {
                        if (reject) Interlocked.Exchange(ref rejectNextVersionConfirmation, 0);
                        System.Text.StringBuilder message = new System.Text.StringBuilder(4096);
                        GetWindowText(GetDlgItem(dialog, 65535), message, message.Capacity);
                        versionConfirmationText = message.ToString();
                        Interlocked.Increment(ref versionConfirmationCount);
                        SendMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                        while (FindWindow(null, "确认设备版本") == dialog) Thread.Sleep(10);
                    }
                }
                Thread.Sleep(10);
            }
        });
        helper.IsBackground = true; helper.Start();
    }

    private static Thread InitialConfirmation(Action<IntPtr> beforeClick)
    {
        Thread helper = new Thread(delegate()
        {
            DateTime until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until)
            {
                IntPtr dialog = FindWindow(null, "确认本地升级");
                uint pid;
                if (dialog != IntPtr.Zero && GetWindowThreadProcessId(dialog, out pid) != 0 &&
                    pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                {
                    IntPtr yes = GetDlgItem(dialog, 6);
                    if (yes != IntPtr.Zero)
                    {
                        if (beforeClick != null) beforeClick(dialog);
                        SendMessage(yes, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }
                }
                Thread.Sleep(20);
            }
        });
        helper.IsBackground = true;
        return helper;
    }

    private static T Find<T>(Control root, string name) where T : Control
    {
        Control[] found = root.Controls.Find(name, true);
        if (found.Length != 1 || !(found[0] is T)) throw new Exception("Missing control: " + name);
        return (T)found[0];
    }

    private static void DropWithoutPump(Control target, string path)
    {
        DataObject data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { path });
        DragEventArgs args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
        typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, new object[] { args });
    }

    private static void Drop(Control target, string path)
    {
        DropWithoutPump(target, path);
        Application.DoEvents();
        if (target.Name == "UpgradePackagePath")
        {
            TextBox info = Find<TextBox>(target.FindForm(), "UpgradePackageInfo");
            WaitFor(delegate { return !info.Text.Contains("正在校验联合包"); }, "Package validation did not finish.");
        }
    }

    private static void WaitFor(Func<bool> condition, string message)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        if (!condition()) throw new Exception(message);
    }

    private static void CheckReadAllFirmwareVersionsWithoutPackage(MainForm form)
    {
        TextBox path = Find<TextBox>(form, "UpgradePackagePath");
        TextBox info = Find<TextBox>(form, "UpgradePackageInfo");
        TextBox ip = Find<TextBox>(form, "LocalDeviceIp");
        TextBox portBox = Find<TextBox>(form, "LocalDevicePort");
        Label status = Find<Label>(form, "UpgradeStatus");
        Button start = Find<Button>(form, "StartUpgradeButton");
        Button stop = Find<Button>(form, "StopUpgradeButton");
        Button read = Find<Button>(form, "ReadFirmwareVersionsButton");
        string oldIp = ip.Text, oldPort = portBox.Text, oldInfo = info.Text, oldStatus = status.Text;
        if (path.Text.Length != 0 || start.Enabled || stop.Enabled || !read.Enabled)
            throw new Exception("An empty local package must allow read only, not start or stop.");

        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        IPEndPoint endpoint = (IPEndPoint)server.LocalEndpoint;
        string failure = null, report = null;
        int dialogClosed = 0;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Request query = ReadRequest(stream, 6);
                    byte[] expected = { 5, 1, 2, 3, 4, 5 };
                    if (query.Payload.Length != expected.Length) throw new Exception("Wrong version query length.");
                    for (int i = 0; i < expected.Length; i++)
                        if (query.Payload[i] != expected[i]) throw new Exception("Wrong version query target count or order.");
                    Reply(stream, query, new byte[] { 5, 1, 0, 2, (byte)'V', (byte)'1',
                        2, 1, 0, 3, 2, 0, 4, 3, 0, 5, 0, 2, (byte)'V', (byte)'5' });
                    if (stream.ReadByte() >= 0) throw new Exception("The read-only action sent another command.");
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        Thread dismiss = new Thread(delegate()
        {
            DateTime until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until)
            {
                IntPtr dialog = FindWindow(null, "读取固件版本");
                uint pid;
                if (dialog != IntPtr.Zero && GetWindowThreadProcessId(dialog, out pid) != 0 &&
                    pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                {
                    IntPtr ok = GetDlgItem(dialog, 1);
                    if (ok != IntPtr.Zero)
                    {
                        System.Text.StringBuilder message = new System.Text.StringBuilder(4096);
                        GetWindowText(GetDlgItem(dialog, 65535), message, message.Capacity);
                        report = message.ToString();
                        SendMessage(ok, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                        Interlocked.Exchange(ref dialogClosed, 1);
                        return;
                    }
                }
                Thread.Sleep(20);
            }
        });
        peer.IsBackground = true; dismiss.IsBackground = true;
        try
        {
            ip.Text = endpoint.Address.ToString(); portBox.Text = endpoint.Port.ToString();
            peer.Start(); dismiss.Start();
            read.PerformClick();
            if (read.Enabled || start.Enabled || stop.Enabled)
                throw new Exception("The read must not allow concurrent read, start, or stop.");
            WaitFor(delegate { return Interlocked.CompareExchange(ref dialogClosed, 0, 0) == 1 && read.Enabled; },
                "The read-only version result did not appear.");
            if (report == null || !report.Contains(endpoint.ToString()) || !report.Contains("PFC：V1") ||
                !report.Contains("LLC：未就绪") || !report.Contains("M4：不支持查询") ||
                !report.Contains("PLC：不可用") || !report.Contains("ESP32：V5"))
                throw new Exception("The version result did not distinguish all five firmware versions and states: " + report);
            if (path.Text.Length != 0 || info.Text != oldInfo || status.Text != oldStatus || start.Enabled || stop.Enabled)
                throw new Exception("Read-only version results changed the upgrade package or task state.");
            if (!peer.Join(3000) || failure != null || server.Pending())
                throw new Exception("The read-only command was not the sole request: " + failure);
        }
        finally { server.Stop(); ip.Text = oldIp; portBox.Text = oldPort; }
    }

    private static void CheckRejectedDeviceState(MainForm form, string package, byte state)
    {
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool gotStatus = false, gotBegin = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 5000;
                    byte[] request = new byte[16];
                    int n = 0;
                    while (n < request.Length)
                    {
                        int read = stream.Read(request, n, request.Length - n);
                        if (read == 0) throw new Exception("Status request was truncated.");
                        n += read;
                    }
                    if (request[0] != 0x4c || request[1] != 0x55 || request[2] != 0x50 ||
                        request[3] != 0x31 || request[4] != 1 || request[5] != 1 ||
                        request[6] != 0 || request[7] != 0 || request[12] != 0 ||
                        request[13] != 0 || request[14] != 0 || request[15] != 0)
                        throw new Exception("The status request did not match LUP1.");
                    gotStatus = true;
                    byte[] reply = new byte[] { 0x4c, 0x55, 0x50, 0x31, 1, 0x81, 0, 0,
                        request[8], request[9], request[10], request[11], 0, 0, 0, 3, 0, 0, state };
                    stream.Write(reply, 0, reply.Length);
                    stream.ReadTimeout = 1000;
                    try { gotBegin = stream.ReadByte() >= 0; }
                    catch (IOException) { }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        Thread confirm = InitialConfirmation(null);
        try
        {
            Drop(Find<TextBox>(form, "UpgradePackagePath"), package);
            Find<TextBox>(form, "LocalDeviceIp").Text = "127.0.0.1";
            Find<TextBox>(form, "LocalDevicePort").Text = port.ToString();
            confirm.Start();
            Find<Button>(form, "StartUpgradeButton").PerformClick();
            string expected = state == 1 ? "拔枪" : "状态未知";
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains(expected); },
                "A plugged or unknown device state must stop the transfer.");
            if (!peer.Join(3000)) throw new Exception("The local simulator did not finish.");
            if (!gotStatus || gotBegin || failure != null)
                throw new Exception("Plugged device was not safely rejected: " + failure);
        }
        finally { server.Stop(); }
    }

    private sealed class Request
    {
        public byte Command;
        public int Sequence;
        public byte[] Payload;
    }

    private static byte[] ReadExactly(NetworkStream stream, int length)
    {
        byte[] bytes = new byte[length];
        int done = 0;
        while (done < length)
        {
            int n = stream.Read(bytes, done, length - done);
            if (n == 0) throw new Exception("Client closed before the full LUP1 request.");
            done += n;
        }
        return bytes;
    }

    private static int ReadNetworkInt(byte[] bytes, int offset)
    {
        return IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bytes, offset));
    }

    private static byte[] NetworkInt(int value)
    {
        return BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value));
    }

    private static Request ReadRequest(NetworkStream stream, byte expected)
    {
        byte[] header = ReadExactly(stream, 16);
        if (header[0] != 0x4c || header[1] != 0x55 || header[2] != 0x50 || header[3] != 0x31 ||
            header[4] != 1 || header[5] != expected || header[6] != 0 || header[7] != 0)
            throw new Exception("Unexpected LUP1 request header, expected command " + expected);
        int length = ReadNetworkInt(header, 12);
        if (length < 0 || length > 4116) throw new Exception("Request length outside protocol bounds.");
        return new Request { Command = expected, Sequence = ReadNetworkInt(header, 8), Payload = ReadExactly(stream, length) };
    }

    private static void Reply(NetworkStream stream, Request request, byte[] data)
    {
        byte[] frame = new byte[18 + data.Length];
        frame[0] = 0x4c; frame[1] = 0x55; frame[2] = 0x50; frame[3] = 0x31;
        frame[4] = 1; frame[5] = (byte)(request.Command | 0x80);
        Buffer.BlockCopy(NetworkInt(request.Sequence), 0, frame, 8, 4);
        Buffer.BlockCopy(NetworkInt(data.Length + 2), 0, frame, 12, 4);
        Buffer.BlockCopy(data, 0, frame, 18, data.Length);
        // A real byte stream may split a frame at any boundary.
        stream.Write(frame, 0, 5); Thread.Sleep(2);
        stream.Write(frame, 5, frame.Length - 5);
    }

    private static void ReplyError(NetworkStream stream, Request request, byte code)
    {
        byte[] frame = new byte[] { 0x4c, 0x55, 0x50, 0x31, 1, (byte)(request.Command | 0x80), 0, 0,
            0, 0, 0, 0, 0, 0, 0, 2, 0, code };
        Buffer.BlockCopy(NetworkInt(request.Sequence), 0, frame, 8, 4);
        stream.Write(frame, 0, frame.Length);
    }

    private static byte[] MatchingVersions()
    {
        byte[] text = System.Text.Encoding.UTF8.GetBytes("V000B01D15");
        byte[] reply = new byte[1 + 2 * (3 + text.Length)];
        reply[0] = 2;
        int at = 1;
        foreach (byte type in new byte[] { 3, 5 })
        {
            reply[at++] = type; reply[at++] = 0; reply[at++] = (byte)text.Length;
            Buffer.BlockCopy(text, 0, reply, at, text.Length);
            at += text.Length;
        }
        return reply;
    }

    private static void CheckCompleteTransfer(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "frozen-input.bin");
        PackageReport fixture = Packager.VerifyFile(source);
        FirmwareInput[] inputs = new FirmwareInput[fixture.Entries.Count];
        for (int i = 0; i < inputs.Length; i++)
        {
            FirmwareEntry entry = fixture.Entries[i];
            string path = Path.Combine(work, "progress-" + entry.Type + ".bin");
            File.WriteAllBytes(path, new byte[8 * 4096]); // Make each ACK advance by less than ten points.
            inputs[i] = new FirmwareInput { Type = entry.Type, Path = path, Name = entry.Name, Version = entry.Version };
        }
        Packager.Build(inputs, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool received = false, confirmed = false, observedWaiting = false;
        string confirmationError = null;
        int priorVersionConfirmations = versionConfirmationCount;
        ManualResetEvent showFirstProgress = new ManualResetEvent(false);
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    client.NoDelay = true;
                    stream.ReadTimeout = 10000;
                    Request status = ReadRequest(stream, 1);
                    if (status.Payload.Length != 0) throw new Exception("Status request must have no payload.");
                    Reply(stream, status, new byte[] { 0 });
                    Request versions = ReadRequest(stream, 6);
                    if (versions.Payload.Length != 3 || versions.Payload[0] != 2 ||
                        versions.Payload[1] != 3 || versions.Payload[2] != 5)
                        throw new Exception("Before-upgrade version request must use the package's actual targets.");
                    Reply(stream, versions, new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                    Request begin = ReadRequest(stream, 2);
                    if (versionConfirmationCount <= priorVersionConfirmations)
                        throw new Exception("BEGIN was sent before the before-version confirmation.");
                    if (begin.Payload.Length != 4 || ReadNetworkInt(begin.Payload, 0) != expected.Length)
                        throw new Exception("Begin must declare the complete package length.");
                    Reply(stream, begin, task);
                    int offset = 0;
                    while (offset < expected.Length)
                    {
                        Request data = ReadRequest(stream, 3);
                        if (data.Payload.Length < 21 || data.Payload.Length > 4116 || ReadNetworkInt(data.Payload, 16) != offset)
                            throw new Exception("Data block length or offset was invalid.");
                        for (int i = 0; i < task.Length; i++)
                            if (data.Payload[i] != task[i]) throw new Exception("Data block task ID differs.");
                        int length = data.Payload.Length - 20;
                        for (int i = 0; i < length; i++)
                            if (data.Payload[20 + i] != expected[offset + i]) throw new Exception("Sent bytes differ from the confirmed snapshot.");
                        offset += length;
                        byte[] ack = new byte[20];
                        Buffer.BlockCopy(task, 0, ack, 0, 16);
                        Buffer.BlockCopy(NetworkInt(offset), 0, ack, 16, 4);
                        Reply(stream, data, ack);
                        if (offset == 4096 && !showFirstProgress.WaitOne(10000))
                            throw new Exception("The UI did not show in-flight progress after the first ACK.");
                    }
                    Request end = ReadRequest(stream, 4);
                    if (end.Payload.Length != 16) throw new Exception("End request has the wrong task ID length.");
                    for (int i = 0; i < task.Length; i++)
                        if (end.Payload[i] != task[i]) throw new Exception("End task ID differs.");
                    Reply(stream, end, task);
                    received = true;
                    Request waiting = ReadRequest(stream, 5);
                    Reply(stream, waiting, new byte[] { 1 });
                    Request matchedWhileWaiting = ReadRequest(stream, 6);
                    Reply(stream, matchedWhileWaiting, MatchingVersions());
                    observedWaiting = true;
                    Request finished = ReadRequest(stream, 5);
                    Reply(stream, finished, new byte[] { 3 });
                    Request matchedAfterFinish = ReadRequest(stream, 6);
                    Reply(stream, matchedAfterFinish, MatchingVersions());
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        Thread confirm = InitialConfirmation(delegate(IntPtr dialog)
        {
            System.Text.StringBuilder message = new System.Text.StringBuilder(4096);
            GetWindowText(GetDlgItem(dialog, 65535), message, message.Capacity);
            if (!message.ToString().Contains("旧包"))
                confirmationError = "The first confirmation must explain the waiting-package replacement risk.";
            // Replace the selected file after the user sees the snapshot.
            File.WriteAllBytes(candidate, new byte[] { 9, 9, 9 });
            confirmed = true;
        });
        try
        {
            TextBoxBase log = Find<TextBoxBase>(form, "UpgradeOperationLog");
            if (!log.Visible || !log.Multiline || !log.ReadOnly)
                throw new Exception("The upgrade page must expose a readable, read-only operation log.");
            int logStart = log.TextLength;
            Drop(Find<TextBox>(form, "UpgradePackagePath"), candidate);
            Find<TextBox>(form, "LocalDeviceIp").Text = "127.0.0.1";
            Find<TextBox>(form, "LocalDevicePort").Text = port.ToString();
            confirm.Start();
            Find<Button>(form, "StartUpgradeButton").PerformClick();
            int firstPercent = 4096 * 100 / expected.Length;
            WaitFor(delegate
            {
                Control progress = Find<Label>(form, "UpgradeStatus");
                ProgressBar bar = Find<ProgressBar>(form, "UpgradeTransferProgress");
                return progress.Visible && progress.Text == "包传输进度：" + firstPercent + "%" &&
                    bar.Visible && bar.Value == firstPercent;
            }, "In-flight transmission must show the first acknowledged block before the next ACK.");
            showFirstProgress.Set();
            WaitFor(delegate { return Find<RichTextBox>(form, "ResultDetails").Text.Contains("设备已接收并校验"); },
                "Completed transfer must report device receipt and verification.");
            WaitFor(delegate
            {
                string state = Find<Label>(form, "UpgradeStatus").Text;
                return state.Contains("等待安装") && state.Contains("版本核对通过");
            }, "Matching versions must not hide a task that is still waiting for installation.");
            if (!Find<Label>(form, "UpgradeStatus").Text.Contains("不能证明本次重新写入"))
                throw new Exception("Matching versions while waiting must retain the evidence limitation.");
            if (Find<Button>(form, "StartUpgradeButton").Enabled)
                throw new Exception("A second task must remain blocked while the device is waiting.");
            WaitFor(delegate
            {
                string state = Find<Label>(form, "UpgradeStatus").Text;
                return state.Contains("任务已结束") && state.Contains("版本核对通过");
            }, "The finished task and matching versions must be displayed separately.");
            if (!peer.Join(3000)) throw new Exception("The simulated transfer did not finish.");
            if (versionConfirmationText == null || !versionConfirmationText.Contains("未就绪") ||
                !versionConfirmationText.Contains("不能证明本次重新写入"))
                confirmationError = "The version confirmation must show queried versions and the evidence limitation.";
            if (!confirmed || !received || !observedWaiting || failure != null || confirmationError != null)
                throw new Exception("Transfer failed: " + failure + " " + confirmationError);
            AssertTransferMilestones(log.Text.Substring(logStart));
        }
        finally { showFirstProgress.Set(); server.Stop(); }
    }

    private static void AssertTransferMilestones(string log)
    {
        int connected = log.IndexOf("TCP 已连接", StringComparison.Ordinal);
        int accepted = log.IndexOf("设备已受理升级", StringComparison.Ordinal);
        int receipt = log.IndexOf("设备已接收并校验", StringComparison.Ordinal);
        int comparison = log.IndexOf("运行版本对比：", StringComparison.Ordinal);
        System.Text.RegularExpressions.MatchCollection progress =
            System.Text.RegularExpressions.Regex.Matches(log, @"包传输进度：[^\r\n]*?(\d+)%");
        if (connected < 0 || accepted <= connected || progress.Count != 11 ||
            progress[0].Index <= accepted || receipt <= progress[progress.Count - 1].Index || comparison <= receipt)
            throw new Exception("Missing or out-of-order local upgrade log milestones.");
        int previous = -1;
        for (int i = 0; i < progress.Count; i++)
        {
            int percent = Int32.Parse(progress[i].Groups[1].Value);
            if (progress[i].Value != "包传输进度：" + percent + "%" ||
                (i == 0 && percent != 0) || (i == progress.Count - 1 && percent != 100) ||
                percent <= previous || (i > 0 && percent / 10 <= previous / 10))
                throw new Exception("Progress must describe acknowledged offsets at bounded intervals from 0 to 100 percent.");
            previous = percent;
        }
        string versions = log.Substring(comparison);
        if (!System.Text.RegularExpressions.Regex.IsMatch(versions,
            @"M4[^\r\n]*目标[^\r\n]*V000B01D15[^\r\n]*(?:当前|实际|运行)[^\r\n]*V000B01D15") ||
            !System.Text.RegularExpressions.Regex.IsMatch(versions,
            @"ESP32[^\r\n]*目标[^\r\n]*V000B01D15[^\r\n]*(?:当前|实际|运行)[^\r\n]*V000B01D15"))
            throw new Exception("Version comparison must include target and observed versions for each package target.");
        if (log.Contains("安装成功") || log.Contains("升级成功"))
            throw new Exception("Transfer and version evidence must not claim installation success.");
    }

    private static void CheckLostEndResponse(MainForm form, string source, string work, int recoveredState)
    {
        string candidate = Path.Combine(work, "lost-end-" + recoveredState + ".bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool confirmed = false, queried = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Reply(stream, ReadRequest(stream, 1), new byte[] { 0 });
                    Reply(stream, ReadRequest(stream, 6), new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                    Request begin = ReadRequest(stream, 2);
                    if (ReadNetworkInt(begin.Payload, 0) != expected.Length) throw new Exception("Wrong begin length.");
                    Reply(stream, begin, task);
                    int offset = 0;
                    while (offset < expected.Length)
                    {
                        Request data = ReadRequest(stream, 3);
                        if (data.Payload.Length < 21 || ReadNetworkInt(data.Payload, 16) != offset)
                            throw new Exception("Invalid data offset.");
                        int length = data.Payload.Length - 20;
                        for (int i = 0; i < length; i++)
                            if (data.Payload[20 + i] != expected[offset + i]) throw new Exception("Invalid package bytes.");
                        offset += length;
                        byte[] ack = new byte[20];
                        Buffer.BlockCopy(task, 0, ack, 0, 16);
                        Buffer.BlockCopy(NetworkInt(offset), 0, ack, 16, 4);
                        Reply(stream, data, ack);
                    }
                    ReadRequest(stream, 4); // Device accepted the end, but the response is lost.
                }
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Request query = ReadRequest(stream, 5);
                    if (query.Payload.Length != 16) throw new Exception("Recovery did not query the task ID.");
                    for (int i = 0; i < task.Length; i++)
                        if (query.Payload[i] != task[i]) throw new Exception("Recovery queried another task.");
                    queried = true;
                    Reply(stream, query, new byte[] { (byte)recoveredState });
                    Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                    if (recoveredState != 3)
                    {
                        Reply(stream, ReadRequest(stream, 5), new byte[] { 3 });
                        Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                    }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        Thread confirm = InitialConfirmation(delegate(IntPtr dialog) { confirmed = true; });
        try
        {
            Drop(Find<TextBox>(form, "UpgradePackagePath"), candidate);
            Find<TextBox>(form, "LocalDeviceIp").Text = "127.0.0.1";
            Find<TextBox>(form, "LocalDevicePort").Text = port.ToString();
            confirm.Start();
            Find<Button>(form, "StartUpgradeButton").PerformClick();
            if (recoveredState != 3)
            {
                string phase = recoveredState == 1 ? "等待安装" : "安装中";
                WaitFor(delegate
                {
                    string state = Find<Label>(form, "UpgradeStatus").Text;
                    return state.Contains(phase) && state.Contains("版本核对通过");
                }, "A recovered waiting or installing task must continue to be observed.");
            }
            WaitFor(delegate
            {
                string state = Find<Label>(form, "UpgradeStatus").Text;
                return state.Contains("任务已结束") && state.Contains("版本核对通过");
            }, "A lost end response must be resolved by querying the original task.");
            if (!peer.Join(3000)) throw new Exception("The recovery simulator did not finish.");
            if (!confirmed || !queried || failure != null) throw new Exception("Recovery failed: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckLongVersionLayout(MainForm form, string work)
    {
        string firmware = Path.Combine(work, "long-version-M4.bin");
        string candidate = Path.Combine(work, "long-version-package.bin");
        File.WriteAllBytes(firmware, new byte[] { 1, 2, 3 });
        string version = new string('V', 39);
        Packager.Build(new[] { new FirmwareInput { Type = FirmwareType.M4, Path = firmware,
            Name = "M4", Version = version } }, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159 };
        byte[] versionBytes = System.Text.Encoding.UTF8.GetBytes(version);
        byte[] versions = new byte[4 + versionBytes.Length];
        versions[0] = 1; versions[1] = 3; versions[2] = 0; versions[3] = (byte)versionBytes.Length;
        Buffer.BlockCopy(versionBytes, 0, versions, 4, versionBytes.Length);
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task, task, versions);
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 1 });
                    Reply(stream, ReadRequest(stream, 6), versions);
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 3 });
                    Reply(stream, ReadRequest(stream, 6), versions);
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true; peer.Start();
        System.Drawing.Size previous = form.Size;
        try
        {
            form.Size = form.MinimumSize; Application.DoEvents();
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("等待安装"); },
                "The long-version task did not reach observation.");
            TextBox info = Find<TextBox>(form, "UpgradePackageInfo");
            if (!info.WordWrap || (info.ScrollBars & ScrollBars.Vertical) == 0)
                throw new Exception("Long version details must wrap and provide a visible vertical scroll path.");
            info.Focus(); info.SelectionStart = info.TextLength; info.ScrollToCaret(); Application.DoEvents();
            System.Drawing.Point end = info.GetPositionFromCharIndex(info.TextLength - 1);
            if (!info.ClientRectangle.Contains(end))
                throw new Exception("The end of long version details must be reachable at the minimum window size.");
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("任务已结束"); },
                "The long-version task did not finish.");
            if (!peer.Join(3000) || failure != null) throw new Exception("Long-version simulation failed: " + failure);
        }
        finally { form.Size = previous; server.Stop(); }
    }

    private static void CheckObservedVersionStates(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "version-states.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task);
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 1 });
                    Reply(stream, ReadRequest(stream, 6), new byte[] { 2, 3, 3, 0, 5, 2, 0 });
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 2 });
                    byte[] mismatch = MatchingVersions(); mismatch[4] = (byte)'X';
                    Reply(stream, ReadRequest(stream, 6), mismatch);
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 3 });
                    Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true; peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("不支持版本查询"); },
                "Unsupported versions must remain observable without implying that installation is unsupported.");
            string first = Find<TextBox>(form, "UpgradePackageInfo").Text;
            if (!first.Contains("不可用") || !first.Contains("不支持查询"))
                throw new Exception("Unavailable and unsupported targets were not distinguished.");
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("版本待核对"); },
                "A partial mismatch must continue waiting.");
            if (!Find<TextBox>(form, "UpgradePackageInfo").Text.Contains("X000B01D15"))
                throw new Exception("The mismatching target version was not displayed.");
            WaitFor(delegate
            {
                string state = Find<Label>(form, "UpgradeStatus").Text;
                return state.Contains("任务已结束") && state.Contains("版本核对通过");
            }, "All matching versions and a finished task must end observation.");
            if (!peer.Join(3000) || failure != null) throw new Exception("Version-state simulation failed: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckAdmissionChanges(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "admission-changes.bin");
        File.Copy(source, candidate);
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        bool extraRequest = false;
        string failure = null;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Reply(stream, ReadRequest(stream, 1), new byte[] { 0 });
                    Reply(stream, ReadRequest(stream, 6), new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                    ReplyError(stream, ReadRequest(stream, 2), 4);
                    stream.ReadTimeout = 1500;
                    try { extraRequest = stream.ReadByte() >= 0; }
                    catch (IOException) { }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true; peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("拔枪"); },
                "A final BEGIN admission rejection must be shown.");
            if (!peer.Join(3000) || failure != null || extraRequest)
                throw new Exception("Data was sent after the device rejected BEGIN: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckCancelVersionConfirmation(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "cancel-version-confirmation.bin");
        File.Copy(source, candidate);
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        bool unexpectedBegin = false, connectionClosed = false;
        string failure = null;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Reply(stream, ReadRequest(stream, 1), new byte[] { 0 });
                    Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                    stream.ReadTimeout = 3000;
                    try
                    {
                        int next = stream.ReadByte();
                        unexpectedBegin = next >= 0;
                        connectionClosed = next == -1;
                    }
                    catch (IOException e)
                    {
                        SocketException reset = e.InnerException as SocketException;
                        if (reset == null || (reset.SocketErrorCode != SocketError.ConnectionReset &&
                            reset.SocketErrorCode != SocketError.ConnectionAborted)) throw;
                        connectionClosed = true;
                    }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true; peer.Start();
        try
        {
            RichTextBox log = Find<RichTextBox>(form, "ResultDetails");
            int previous = log.TextLength;
            Interlocked.Exchange(ref rejectNextVersionConfirmation, 1);
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("已取消本地升级"); },
                "Declining the before-version confirmation must cancel the upgrade.");
            string result = log.Text.Substring(previous);
            if (!result.Contains("已取消本地升级") ||
                !System.Text.RegularExpressions.Regex.IsMatch(result, @"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]"))
                throw new Exception("Cancellation must be recorded with a timestamp.");
            if (!peer.Join(4000) || failure != null || unexpectedBegin || !connectionClosed)
                throw new Exception("Declining before-versions must close the connection and prevent BEGIN: " + failure);
        }
        finally { Interlocked.Exchange(ref rejectNextVersionConfirmation, 0); server.Stop(); }
    }

    private static void SetWaitSeconds(MainForm form, int seconds, bool checkDefaults)
    {
        Button button = Find<Button>(form, "UpgradeTimeoutSettings");
        bool seen = false;
        Exception failure = null;
        using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
        {
            timer.Interval = 20;
            timer.Tick += delegate
            {
                foreach (Form dialog in Application.OpenForms)
                {
                    if (dialog.Name != "UpgradeTimeoutDialog") continue;
                    timer.Stop();
                    try
                    {
                        if (checkDefaults && (Find<TextBox>(dialog, "UpgradeConnectSeconds").Text != "5" ||
                            Find<TextBox>(dialog, "UpgradeBlockSeconds").Text != "10" ||
                            Find<TextBox>(dialog, "UpgradeOperationSeconds").Text != "120" ||
                            Find<TextBox>(dialog, "UpgradeWaitSeconds").Text != "600"))
                            throw new Exception("Unexpected default upgrade timeouts.");
                        Button save = Find<Button>(dialog, "UpgradeTimeoutSave");
                        Button cancel = (Button)dialog.CancelButton;
                        if (Math.Abs(save.Font.SizeInPoints - 9F) > 0.01F || !save.Font.Bold ||
                            Math.Abs(cancel.Font.SizeInPoints - 9F) > 0.01F || cancel.Font.Bold)
                            throw new Exception("Timeout buttons must follow the primary and secondary button typography.");
                        Find<TextBox>(dialog, "UpgradeWaitSeconds").Text = seconds.ToString();
                        seen = true;
                        Find<Button>(dialog, "UpgradeTimeoutSave").PerformClick();
                    }
                    catch (Exception e) { failure = e; dialog.Close(); }
                    break;
                }
            };
            timer.Start();
            button.PerformClick();
            timer.Stop();
        }
        if (failure != null) throw failure;
        if (!seen) throw new Exception("Timeout settings were not displayed.");
    }

    private static void CheckTimeoutSettings(MainForm form) { SetWaitSeconds(form, 6, true); }

    private static void ReceiveBundle(NetworkStream stream, byte[] expected, byte[] task)
    {
        ReceiveBundle(stream, expected, task, task);
    }

    private static void ReceiveBundle(NetworkStream stream, byte[] expected, byte[] task, byte[] endReply)
    {
        ReceiveBundle(stream, expected, task, endReply, new byte[] { 2, 3, 1, 0, 5, 1, 0 });
    }

    private static void ReceiveBundle(NetworkStream stream, byte[] expected, byte[] task, byte[] endReply, byte[] before)
    {
        Reply(stream, ReadRequest(stream, 1), new byte[] { 0 });
        Reply(stream, ReadRequest(stream, 6), before);
        Request begin = ReadRequest(stream, 2);
        if (ReadNetworkInt(begin.Payload, 0) != expected.Length) throw new Exception("Wrong begin length.");
        Reply(stream, begin, task);
        int offset = 0;
        while (offset < expected.Length)
        {
            Request data = ReadRequest(stream, 3);
            if (data.Payload.Length < 21 || ReadNetworkInt(data.Payload, 16) != offset)
                throw new Exception("Invalid data offset.");
            int length = data.Payload.Length - 20;
            for (int i = 0; i < length; i++)
                if (data.Payload[20 + i] != expected[offset + i]) throw new Exception("Invalid package bytes.");
            offset += length;
            byte[] ack = new byte[20];
            Buffer.BlockCopy(task, 0, ack, 0, 16);
            Buffer.BlockCopy(NetworkInt(offset), 0, ack, 16, 4);
            Reply(stream, data, ack);
        }
        Request end = ReadRequest(stream, 4);
        if (endReply != null) Reply(stream, end, endReply);
    }

    private static void CheckLostEndUnconfirmed(MainForm form, string source, string work, int recoveryState)
    {
        string candidate = Path.Combine(work, "lost-unconfirmed-" + recoveryState + ".bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool confirmed = false, extraQuery = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task, null, MatchingVersions());
                }
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Request query = ReadRequest(stream, 5);
                    if (recoveryState == 11) ReplyError(stream, query, 11);
                    else Reply(stream, query, new byte[] { (byte)recoveryState });
                    stream.ReadTimeout = 1500;
                    try { extraQuery = stream.ReadByte() >= 0; }
                    catch (IOException) { }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        Thread confirm = InitialConfirmation(delegate(IntPtr dialog) { confirmed = true; });
        try
        {
            Drop(Find<TextBox>(form, "UpgradePackagePath"), candidate);
            Find<TextBox>(form, "LocalDeviceIp").Text = "127.0.0.1";
            Find<TextBox>(form, "LocalDevicePort").Text = port.ToString();
            confirm.Start();
            Find<Button>(form, "StartUpgradeButton").PerformClick();
            string expectedStatus = recoveryState == 4 ? "设备报告本次任务失败" : "结束应答未确认";
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains(expectedStatus); },
                "The recovered task state was not reported correctly.");
            string text = Find<Label>(form, "UpgradeStatus").Text;
            if (recoveryState == 4 && text.Contains("未确认"))
                throw new Exception("An explicit device failure must not be described as unconfirmed.");
            if (!peer.Join(3000)) throw new Exception("The unconfirmed-task simulator did not finish.");
            if (!confirmed || extraQuery || failure != null)
                throw new Exception("Unconfirmed or failed task was treated as a verified task: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckRecoveryQueryError(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "recovery-query-error.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 1, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, 29, 31 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task, null, new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                }
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 1 });
                    ReplyError(stream, ReadRequest(stream, 6), 3);
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("无法受理"); },
                "The version-query error was not displayed.");
            if (Find<Label>(form, "UpgradeStatus").Text.Contains("结束应答未确认"))
                throw new Exception("A later query error must not erase the already confirmed task state.");
            if (!peer.Join(3000) || failure != null) throw new Exception("Recovery-query simulator failed: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckLostEndContinueAfterLookupFailure(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "lost-end-lookup-unavailable.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        int manualResume = 0;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task, null);
                }
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    using (TcpClient client = server.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 10000;
                        Request query = ReadRequest(stream, 5);
                        if (query.Payload.Length != task.Length) throw new Exception("Missing original task ID.");
                        for (int i = 0; i < task.Length; i++)
                            if (query.Payload[i] != task[i]) throw new Exception("A different task ID was queried.");
                        if (attempt == 0) continue; // Disconnect without answering the first lookup.
                        if (Interlocked.CompareExchange(ref manualResume, 0, 0) == 0)
                            throw new Exception("The second lookup was not triggered manually.");
                        Reply(stream, query, new byte[] { 3 });
                        Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                        if (stream.ReadByte() >= 0)
                            throw new Exception("Unexpected traffic after the read-only re-query.");
                    }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            Label status = Find<Label>(form, "UpgradeStatus");
            Button resume = Find<Button>(form, "ContinueUpgradeButton");
            WaitFor(delegate { return status.Text.Contains("结束应答未确认"); },
                "A failed first lookup must leave the END result unconfirmed.");
            if (!resume.Visible || !resume.Enabled || status.Text.Contains("版本核对通过"))
                throw new Exception("The unresolved END must offer manual continuation without claiming success.");
            if (server.Pending()) throw new Exception("Lookup must not resume before user confirmation.");
            Interlocked.Exchange(ref manualResume, 1);
            resume.PerformClick();
            WaitFor(delegate { return status.Text.Contains("任务已结束") && status.Text.Contains("版本核对通过"); },
                "Manual continuation must finish observing the original task.");
            if (!peer.Join(3000) || failure != null)
                throw new Exception("Read-only lost-END continuation failed: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckDeviceTaskFailure(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "task-failure.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool confirmed = false, extraQuery = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task, task, MatchingVersions());
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 4 });
                    stream.ReadTimeout = 1500;
                    try { extraQuery = stream.ReadByte() >= 0; }
                    catch (IOException) { }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        Thread confirm = InitialConfirmation(delegate(IntPtr dialog) { confirmed = true; });
        try
        {
            Drop(Find<TextBox>(form, "UpgradePackagePath"), candidate);
            Find<TextBox>(form, "LocalDeviceIp").Text = "127.0.0.1";
            Find<TextBox>(form, "LocalDevicePort").Text = port.ToString();
            RichTextBox log = Find<RichTextBox>(form, "ResultDetails");
            int before = log.TextLength;
            confirm.Start();
            Find<Button>(form, "StartUpgradeButton").PerformClick();
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("设备报告本次任务失败"); },
                "A device-reported failure must stop automatic waiting.");
            if (!peer.Join(3000)) throw new Exception("The failure simulator did not finish.");
            if (!confirmed || extraQuery || failure != null || log.Text.Substring(before).Contains("版本核对通过"))
                throw new Exception("An old matching version concealed a device-reported failure: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void StartTestUpgrade(MainForm form, string candidate, int port)
    {
        Drop(Find<TextBox>(form, "UpgradePackagePath"), candidate);
        Find<TextBox>(form, "LocalDeviceIp").Text = "127.0.0.1";
        Find<TextBox>(form, "LocalDevicePort").Text = port.ToString();
        Thread confirm = InitialConfirmation(null);
        confirm.Start();
        Find<Button>(form, "StartUpgradeButton").PerformClick();
    }

    private static void CheckLostBeginResponse(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "lost-begin.bin");
        File.Copy(source, candidate);
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool extraConnection = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Reply(stream, ReadRequest(stream, 1), new byte[] { 0 });
                    Reply(stream, ReadRequest(stream, 6), new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                    ReadRequest(stream, 2); // No task ID is returned to the client.
                }
                DateTime until = DateTime.UtcNow.AddSeconds(2);
                while (!server.Pending() && DateTime.UtcNow < until) Thread.Sleep(10);
                extraConnection = server.Pending();
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("开始应答未确认"); },
                "A lost begin response must be reported as unconfirmed.");
            if (!peer.Join(4000)) throw new Exception("The lost-begin simulator did not finish.");
            if (extraConnection || failure != null) throw new Exception("A lost begin response caused an automatic retry: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckWrongOffsetStops(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "wrong-offset.bin");
        File.Copy(source, candidate);
        byte[] task = new byte[] { 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool extraRequest = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Reply(stream, ReadRequest(stream, 1), new byte[] { 0 });
                    Reply(stream, ReadRequest(stream, 6), new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                    Reply(stream, ReadRequest(stream, 2), task);
                    Request data = ReadRequest(stream, 3);
                    byte[] ack = new byte[20];
                    Buffer.BlockCopy(task, 0, ack, 0, 16); // The next offset is incorrectly zero.
                    Reply(stream, data, ack);
                    stream.ReadTimeout = 1500;
                    try { extraRequest = stream.ReadByte() >= 0; }
                    catch (IOException) { }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("下一偏移"); },
                "An incorrect block acknowledgement must stop the transfer.");
            if (!peer.Join(3000)) throw new Exception("The wrong-offset simulator did not finish.");
            if (extraRequest || failure != null) throw new Exception("A bad acknowledgement allowed more data: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckCancelCloseDuringTransfer(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "close-during-transfer.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool firstBlock = false, rejectedClose = false;
        ManualResetEvent resume = new ManualResetEvent(false);
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Reply(stream, ReadRequest(stream, 1), new byte[] { 0 });
                    Reply(stream, ReadRequest(stream, 6), new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                    Reply(stream, ReadRequest(stream, 2), task);
                    int offset = 0;
                    while (offset < expected.Length)
                    {
                        Request data = ReadRequest(stream, 3);
                        if (ReadNetworkInt(data.Payload, 16) != offset) throw new Exception("Unexpected block offset.");
                        int length = data.Payload.Length - 20;
                        for (int i = 0; i < length; i++)
                            if (data.Payload[20 + i] != expected[offset + i]) throw new Exception("Unexpected package data.");
                        offset += length;
                        if (!firstBlock)
                        {
                            firstBlock = true;
                            if (!resume.WaitOne(10000)) throw new Exception("Close confirmation was not completed.");
                        }
                        byte[] ack = new byte[20];
                        Buffer.BlockCopy(task, 0, ack, 0, 16);
                        Buffer.BlockCopy(NetworkInt(offset), 0, ack, 16, 4);
                        Reply(stream, data, ack);
                    }
                    Reply(stream, ReadRequest(stream, 4), task);
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 3 });
                    Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate { return firstBlock; }, "The simulated transfer did not begin.");
            Thread closeDialog = new Thread(delegate()
            {
                DateTime until = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < until)
                {
                    IntPtr dialog = FindWindow(null, "本地升级进行中");
                    uint pid;
                    if (dialog != IntPtr.Zero && GetWindowThreadProcessId(dialog, out pid) != 0 &&
                        pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                    {
                        IntPtr no = GetDlgItem(dialog, 7);
                        if (no != IntPtr.Zero) { rejectedClose = true; SendMessage(no, 0x00F5, IntPtr.Zero, IntPtr.Zero); return; }
                    }
                    Thread.Sleep(20);
                }
            });
            closeDialog.IsBackground = true;
            closeDialog.Start();
            RichTextBox result = Find<RichTextBox>(form, "ResultDetails");
            int previousLength = result.TextLength;
            form.Close(); Application.DoEvents();
            if (!rejectedClose || form.IsDisposed) throw new Exception("Canceling close did not preserve the window.");
            string closeResult = result.Text.Substring(previousLength);
            if (!closeResult.Contains("取消关闭") ||
                !System.Text.RegularExpressions.Regex.IsMatch(closeResult, @"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]"))
                throw new Exception("Canceling window close must add a timestamped result.");
            resume.Set();
            WaitFor(delegate
            {
                string state = Find<Label>(form, "UpgradeStatus").Text;
                return state.Contains("任务已结束") && state.Contains("版本核对通过");
            }, "The transfer did not continue after canceling close.");
            if (!peer.Join(3000) || failure != null) throw new Exception("Close-cancel transfer failed: " + failure);
        }
        finally { resume.Set(); server.Stop(); resume.Dispose(); }
    }

    private static void CheckConfirmCloseDuringTransfer(string source, string work)
    {
        string candidate = Path.Combine(work, "confirm-close.bin");
        File.Copy(source, candidate);
        byte[] task = new byte[] { 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        bool firstBlock = false, socketClosed = false, confirmed = false;
        string failure = null;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 5000;
                    Reply(stream, ReadRequest(stream, 1), new byte[] { 0 });
                    Reply(stream, ReadRequest(stream, 6), new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                    Reply(stream, ReadRequest(stream, 2), task);
                    ReadRequest(stream, 3);
                    firstBlock = true;
                    try { socketClosed = stream.ReadByte() == -1; }
                    catch (IOException e)
                    {
                        SocketException reset = e.InnerException as SocketException;
                        if (reset == null || (reset.SocketErrorCode != SocketError.ConnectionReset &&
                            reset.SocketErrorCode != SocketError.ConnectionAborted)) throw;
                        socketClosed = true;
                    }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true; peer.Start();
        try
        {
            using (MainForm form = new MainForm())
            {
                form.Show(); Application.DoEvents();
                Find<Button>(form, "UpgradePageTab").PerformClick(); Application.DoEvents();
                StartTestUpgrade(form, candidate, port);
                WaitFor(delegate { return firstBlock; }, "The transfer did not reach the first block.");
                Thread closeDialog = new Thread(delegate()
                {
                    DateTime until = DateTime.UtcNow.AddSeconds(8);
                    while (DateTime.UtcNow < until)
                    {
                        IntPtr dialog = FindWindow(null, "本地升级进行中");
                        uint pid;
                        if (dialog != IntPtr.Zero && GetWindowThreadProcessId(dialog, out pid) != 0 &&
                            pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                        {
                            IntPtr yes = GetDlgItem(dialog, 6);
                            if (yes != IntPtr.Zero) { confirmed = true; SendMessage(yes, 0x00F5, IntPtr.Zero, IntPtr.Zero); return; }
                        }
                        Thread.Sleep(20);
                    }
                });
                closeDialog.IsBackground = true; closeDialog.Start();
                form.Close(); Application.DoEvents();
                if (!confirmed || !form.IsDisposed) throw new Exception("Confirming close did not close the window.");
            }
            if (!peer.Join(7000) || !socketClosed || failure != null)
                throw new Exception("Confirming close must close the active client connection: " + failure);
        }
        finally { server.Stop(); }
    }


    private static void CheckInvalidEndFails(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "invalid-end.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63 };
        byte[] wrong = (byte[])task.Clone(); wrong[0] ^= 1;
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool confirmed = false, queried = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task, wrong);
                }
                DateTime until = DateTime.UtcNow.AddSeconds(3);
                while (!server.Pending() && DateTime.UtcNow < until) Thread.Sleep(10);
                if (server.Pending())
                {
                    queried = true;
                    using (TcpClient client = server.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 5000;
                        Reply(stream, ReadRequest(stream, 5), new byte[] { 3 });
                        Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                    }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        Thread confirm = InitialConfirmation(delegate(IntPtr dialog) { confirmed = true; });
        try
        {
            Drop(Find<TextBox>(form, "UpgradePackagePath"), candidate);
            Find<TextBox>(form, "LocalDeviceIp").Text = "127.0.0.1";
            Find<TextBox>(form, "LocalDevicePort").Text = port.ToString();
            confirm.Start();
            Find<Button>(form, "StartUpgradeButton").PerformClick();
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("任务标识"); },
                "An end response with the wrong task ID must stop as a protocol error.");
            if (!peer.Join(5000)) throw new Exception("The invalid-end simulator did not finish.");
            if (!confirmed || queried || failure != null)
                throw new Exception("An invalid end response was incorrectly recovered as success: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckTimeoutAndNextPackage(MainForm form, string source, string work, bool samePath)
    {
        string candidate = Path.Combine(work, samePath ? "same-path-A.bin" : "package-A.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool confirmed = false, resumedQuery = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task);
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 1 });
                    Reply(stream, ReadRequest(stream, 6), new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                }
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Request query = ReadRequest(stream, 5);
                    if (query.Payload.Length != 16) throw new Exception("Continue did not query the original task.");
                    for (int i = 0; i < task.Length; i++)
                        if (query.Payload[i] != task[i]) throw new Exception("Continue queried another task.");
                    resumedQuery = true;
                    Reply(stream, query, new byte[] { 3 });
                    Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        Thread confirm = InitialConfirmation(delegate(IntPtr dialog) { confirmed = true; });
        try
        {
            Drop(Find<TextBox>(form, "UpgradePackagePath"), candidate);
            Find<TextBox>(form, "LocalDeviceIp").Text = "127.0.0.1";
            Find<TextBox>(form, "LocalDevicePort").Text = port.ToString();
            confirm.Start();
            Find<Button>(form, "StartUpgradeButton").PerformClick();
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("设备等待安装"); },
                "Task A did not enter the simulated waiting state.");

            Find<Button>(form, "PackPageTab").PerformClick(); Application.DoEvents();
            string firmwareB = Path.Combine(work, samePath ? "same-M4_V2.bin" : "M4_V2.bin");
            File.WriteAllBytes(firmwareB, new byte[] { 7, 8, 9, 10 });
            Drop(Find<TextBox>(form, "Path_M4"), firmwareB);
            Find<TextBox>(form, "Name_M4").Text = "M4_V2";
            Find<TextBox>(form, "Version_M4").Text = "V2";
            string packageB = samePath ? candidate : Path.Combine(work, "package-B.bin");
            Find<TextBox>(form, "OutputPath").Text = packageB;
            if (samePath)
            {
                Thread replace = new Thread(delegate()
                {
                    DateTime until = DateTime.UtcNow.AddSeconds(10);
                    while (DateTime.UtcNow < until)
                    {
                        IntPtr dialog = FindWindow(null, "确认替换");
                        uint pid;
                        if (dialog != IntPtr.Zero && GetWindowThreadProcessId(dialog, out pid) != 0 &&
                            pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                        {
                            IntPtr yes = GetDlgItem(dialog, 6);
                            if (yes != IntPtr.Zero) { SendMessage(yes, 0x00F5, IntPtr.Zero, IntPtr.Zero); return; }
                        }
                        Thread.Sleep(20);
                    }
                });
                replace.IsBackground = true; replace.Start();
            }
            Find<Button>(form, "BuildButton").PerformClick(); Application.DoEvents();
            if (!File.Exists(packageB)) throw new Exception("The second package was not generated.");
            if (Find<TextBox>(form, "UpgradePackagePath").Text != candidate)
                throw new Exception("Generating B replaced the active A task.");
            Find<Button>(form, "UpgradePageTab").PerformClick(); Application.DoEvents();
            WaitFor(delegate
            {
                return Find<Label>(form, "UpgradeStatus").Text.Contains("等待超时") &&
                    Find<TextBox>(form, "UpgradePackagePath").Text == packageB;
            }, "Timeout must associate B while retaining A.");
            WaitFor(delegate { return Find<TextBox>(form, "UpgradePackageInfo").Text.Contains("V2"); },
                "The next generated package must be revalidated before it is selectable.");
            Button resume = Find<Button>(form, "ContinueUpgradeButton");
            if (!resume.Enabled || !Find<Label>(form, "UpgradeLastTask").Text.Contains(Path.GetFileName(candidate)))
                throw new Exception("The timed-out A task must remain identifiable and resumable.");
            if (samePath && !Find<Label>(form, "UpgradeLastTask").Text.Contains("路径已用于新包"))
                throw new Exception("A and B at the same path must be identified as separate tasks.");
            Find<TextBox>(form, "LocalDeviceIp").Text = "127.0.0.2";
            Find<TextBox>(form, "LocalDevicePort").Text = "65000";
            string oldEndpoint = new IPEndPoint(IPAddress.Loopback, port).ToString();
            if (!Find<Label>(form, "UpgradeLastTask").Text.Contains(oldEndpoint))
                throw new Exception("The continuation label must expose A's device, not only the next device fields.");
            resume.PerformClick();
            if (!Find<Label>(form, "UpgradeStatus").Text.Contains(oldEndpoint))
                throw new Exception("Starting continuation must identify A's original device.");
            WaitFor(delegate
            {
                string state = Find<Label>(form, "UpgradeStatus").Text;
                return state.Contains("任务已结束") && state.Contains("版本核对通过");
            }, "Continuing A must query its original device.");
            if (Find<TextBox>(form, "UpgradePackagePath").Text != packageB ||
                !Find<TextBox>(form, "UpgradePackageInfo").Text.Contains("V2"))
                throw new Exception("Continuing A changed the selected B package.");
            if (!peer.Join(3000)) throw new Exception("The continue-waiting simulator did not finish.");
            if (!confirmed || !resumedQuery || failure != null) throw new Exception("Continue waiting failed: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckRebootWithUnknownTask(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "reboot-unknown.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool queried = false, continued = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task);
                    // The accepted response was sent; the simulated device then reboots.
                }
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Request query = ReadRequest(stream, 5);
                    if (query.Payload.Length != task.Length) throw new Exception("Wrong reboot task ID length.");
                    for (int i = 0; i < task.Length; i++)
                        if (query.Payload[i] != task[i]) throw new Exception("Reboot query used another task ID.");
                    queried = true;
                    ReplyError(stream, query, 11);
                    Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                }
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Request query = ReadRequest(stream, 5);
                    if (query.Payload.Length != task.Length) throw new Exception("Wrong continued task ID length.");
                    for (int i = 0; i < task.Length; i++)
                        if (query.Payload[i] != task[i]) throw new Exception("Continue queried another task ID.");
                    continued = true;
                    Reply(stream, query, new byte[] { 3 });
                    Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate
            {
                string state = Find<Label>(form, "UpgradeStatus").Text;
                return state.Contains("任务状态未知") && state.Contains("版本核对通过") &&
                    state.Contains("本次任务结果未确认") && Find<Button>(form, "StartUpgradeButton").Enabled;
            }, "An unknown task with matching versions must end observation without claiming task success.");
            Button continueButton = Find<Button>(form, "ContinueUpgradeButton");
            if (!continueButton.Visible || !continueButton.Enabled)
                throw new Exception("An unconfirmed task must remain available for manual re-query.");
            continueButton.PerformClick();
            WaitFor(delegate
            {
                string state = Find<Label>(form, "UpgradeStatus").Text;
                return state.Contains("任务已结束") && state.Contains("版本核对通过");
            }, "Manual continued observation must query the original task.");
            if (!peer.Join(3000)) throw new Exception("The reboot simulator did not finish.");
            if (!queried || !continued || failure != null) throw new Exception("Reboot query failed: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckRejectedNewTaskKeepsPrior(MainForm form, string source, string work)
    {
        string first = Path.Combine(work, "prior-task-A.bin");
        string second = Path.Combine(work, "rejected-task-B.bin");
        File.Copy(source, first); File.Copy(source, second);
        byte[] expected = File.ReadAllBytes(first);
        byte[] task = new byte[] { 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool resumedPrior = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task);
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 1 });
                    Reply(stream, ReadRequest(stream, 6), new byte[] { 2, 3, 1, 0, 5, 1, 0 });
                }
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Reply(stream, ReadRequest(stream, 1), new byte[] { 0 });
                    Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                    ReplyError(stream, ReadRequest(stream, 2), 4);
                }
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    Request query = ReadRequest(stream, 5);
                    if (query.Payload.Length != task.Length) throw new Exception("Wrong retained task ID length.");
                    for (int i = 0; i < task.Length; i++)
                        if (query.Payload[i] != task[i]) throw new Exception("A different task was resumed.");
                    resumedPrior = true;
                    Reply(stream, query, new byte[] { 3 });
                    Reply(stream, ReadRequest(stream, 6), MatchingVersions());
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true; peer.Start();
        try
        {
            StartTestUpgrade(form, first, port);
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("等待超时"); },
                "The first task did not time out while waiting.");
            if (!Find<Button>(form, "ContinueUpgradeButton").Enabled)
                throw new Exception("The first task must be resumable after timeout.");
            StartTestUpgrade(form, second, port);
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("拔枪"); },
                "The new task's BEGIN rejection was not reported.");
            Button resume = Find<Button>(form, "ContinueUpgradeButton");
            if (!resume.Visible || !resume.Enabled || !Find<Label>(form, "UpgradeLastTask").Text.Contains("prior-task-A.bin"))
                throw new Exception("A rejected new task must preserve the prior task's continue entry.");
            resume.PerformClick();
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("任务已结束"); },
                "The prior task could not be observed after rejecting the new task.");
            if (!peer.Join(3000) || !resumedPrior || failure != null)
                throw new Exception("The prior task was not resumed: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckWaitDeadline(MainForm form, string source, string work)
    {
        string candidate = Path.Combine(work, "wait-deadline.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        bool querySeen = false;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task);
                    ReadRequest(stream, 5);
                    querySeen = true;
                    Thread.Sleep(3500); // Keep the connection open without a response.
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            WaitFor(delegate { return querySeen; }, "The task query was not sent.");
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            WaitFor(delegate { return Find<Label>(form, "UpgradeStatus").Text.Contains("等待超时"); },
                "The configured wait did not time out.");
            if (watch.ElapsedMilliseconds > 2500)
                throw new Exception("A silent query exceeded the configured one-second wait.");
            if (!peer.Join(5000) || failure != null) throw new Exception("Deadline simulator failed: " + failure);
        }
        finally { server.Stop(); }
    }

    private static void CheckEndedWithoutVersionMatchTimesOut(MainForm form, string source, string work, bool disconnect)
    {
        string candidate = Path.Combine(work, disconnect ? "ended-version-disconnected.bin" : "ended-version-mismatch.bin");
        File.Copy(source, candidate);
        byte[] expected = File.ReadAllBytes(candidate);
        byte[] task = new byte[] { 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215 };
        TcpListener server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        string failure = null;
        Thread peer = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    ReceiveBundle(stream, expected, task);
                    Reply(stream, ReadRequest(stream, 5), new byte[] { 3 });
                    Request versionQuery = ReadRequest(stream, 6);
                    if (!disconnect)
                    {
                        byte[] mismatch = MatchingVersions(); mismatch[4] = (byte)'X';
                        Reply(stream, versionQuery, mismatch);
                        if (stream.ReadByte() >= 0)
                            throw new Exception("Unexpected request before the one-second observation deadline.");
                    }
                }
                if (disconnect)
                {
                    using (TcpClient client = server.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 10000;
                        Request query = ReadRequest(stream, 5);
                        if (query.Payload.Length != task.Length) throw new Exception("Missing original task ID.");
                        for (int i = 0; i < task.Length; i++)
                            if (query.Payload[i] != task[i]) throw new Exception("A different task ID was queried.");
                        // Close before answering so continuing cannot obtain new evidence.
                    }
                }
            }
            catch (Exception e) { failure = e.Message; }
        });
        peer.IsBackground = true;
        peer.Start();
        try
        {
            StartTestUpgrade(form, candidate, port);
            Label status = Find<Label>(form, "UpgradeStatus");
            Button resume = Find<Button>(form, "ContinueUpgradeButton");
            string expectedVersionResult = disconnect ? "版本待核对" : "版本不一致";
            for (int attempt = 0; attempt < (disconnect ? 2 : 1); attempt++)
            {
                if (attempt != 0) resume.PerformClick();
                WaitFor(delegate { return status.Text.Contains("等待超时"); },
                    "An ended task without a version match must wait for verification before timing out.");
                if (!status.Text.Contains("任务已结束") || !status.Text.Contains(expectedVersionResult) ||
                    status.Text.Contains("执行安装") || status.Text.Contains("版本核对通过") || !resume.Enabled)
                    throw new Exception("Timeout must preserve the observed ended state and unresolved version check.");
            }
            if (!disconnect && !Find<TextBox>(form, "UpgradePackageInfo").Text.Contains("X000B01D15"))
                throw new Exception("Timeout must retain the observed mismatching version.");
            if (!peer.Join(3000) || failure != null)
                throw new Exception("Ended-task version mismatch simulation failed: " + failure);
        }
        finally { server.Stop(); }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string work = Path.Combine(Path.GetTempPath(), "secc-local-upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            Environment.SetEnvironmentVariable("SECC_PACKAGER_SETTINGS_DIR", Path.Combine(work, "settings"));
            Application.EnableVisualStyles();
            StartVersionConfirmationHelper();
            using (MainForm form = new MainForm())
            {
                form.Show(); Application.DoEvents();
                Find<Button>(form, "UpgradePageTab").PerformClick(); Application.DoEvents();
                if (args.Length > 1 && args[1] == "read-firmware-versions")
                {
                    CheckReadAllFirmwareVersionsWithoutPackage(form);
                    form.Close();
                    Console.WriteLine("PASS local UI: read all five firmware versions with one read-only request and no package");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "review-reboot")
                {
                    CheckRebootWithUnknownTask(form, Path.GetFullPath(args[0]), work);
                    form.Close();
                    Console.WriteLine("PASS reboot unknown task, version match and manual re-query");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "review-preflight")
                {
                    CheckCompleteTransfer(form, Path.GetFullPath(args[0]), work);
                    form.Close();
                    Console.WriteLine("PASS preflight versions and evidence limitation");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "review-same-path")
                {
                    SetWaitSeconds(form, 6, false);
                    CheckTimeoutAndNextPackage(form, Path.GetFullPath(args[0]), work, true);
                    form.Close();
                    Console.WriteLine("PASS same-path A/B handoff");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "review-long-version")
                {
                    CheckLongVersionLayout(form, work);
                    form.Close();
                    Console.WriteLine("PASS long-version visibility");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "review-close")
                {
                    form.Hide();
                    CheckConfirmCloseDuringTransfer(Path.GetFullPath(args[0]), work);
                    form.Close();
                    Console.WriteLine("PASS confirmed close disconnects the client");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "review-versions")
                {
                    CheckCancelVersionConfirmation(form, Path.GetFullPath(args[0]), work);
                    CheckAdmissionChanges(form, Path.GetFullPath(args[0]), work);
                    CheckObservedVersionStates(form, Path.GetFullPath(args[0]), work);
                    form.Close();
                    Console.WriteLine("PASS final admission and unavailable, unsupported and mismatching versions");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "review-rejected-new")
                {
                    SetWaitSeconds(form, 1, true);
                    CheckRejectedNewTaskKeepsPrior(form, Path.GetFullPath(args[0]), work);
                    form.Close();
                    Console.WriteLine("PASS prior task retained when a new BEGIN is rejected");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "recovery-query-error")
                {
                    CheckRecoveryQueryError(form, Path.GetFullPath(args[0]), work);
                    CheckLostEndContinueAfterLookupFailure(form, Path.GetFullPath(args[0]), work);
                    form.Close();
                    Console.WriteLine("PASS recovery query preserves confirmed receipt and permits read-only manual re-query");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "wait-deadline")
                {
                    SetWaitSeconds(form, 1, true);
                    CheckEndedWithoutVersionMatchTimesOut(form, Path.GetFullPath(args[0]), work, false);
                    CheckEndedWithoutVersionMatchTimesOut(form, Path.GetFullPath(args[0]), work, true);
                    CheckWaitDeadline(form, Path.GetFullPath(args[0]), work);
                    form.Close();
                    Console.WriteLine("PASS waiting deadline bounds a silent query");
                    return 0;
                }
                CheckReadAllFirmwareVersionsWithoutPackage(form);
                TextBox path = Find<TextBox>(form, "UpgradePackagePath");
                TextBox info = Find<TextBox>(form, "UpgradePackageInfo");
                Button start = Find<Button>(form, "StartUpgradeButton");
                DropWithoutPump(path, Path.GetFullPath(args[0]));
                if (!info.Text.Contains("正在校验联合包") || start.Enabled)
                    throw new Exception("Package validation must run in the background before enabling start.");
                WaitFor(delegate { return info.Text.Contains("M4"); }, "Valid package was not displayed.");
                if (!start.Enabled) throw new Exception("A valid local upgrade bundle must enable the start action.");
                string invalid = Path.Combine(work, "invalid.bin");
                File.WriteAllBytes(invalid, new byte[] { 1, 2, 3 });
                RichTextBox selectionLog = Find<RichTextBox>(form, "ResultDetails");
                int logStart = selectionLog.TextLength;
                DropWithoutPump(path, Path.GetFullPath(args[0]));
                DropWithoutPump(path, invalid);
                WaitFor(delegate { return info.Text.Contains("无法识别"); }, "Invalid package was not reported.");
                Thread.Sleep(100); Application.DoEvents();
                if (start.Enabled) throw new Exception("An invalid replacement must revoke the previous package's send eligibility.");
                if (!info.Text.Contains("无法识别")) throw new Exception("A stale validation replaced the latest error.");
                string selectionResult = selectionLog.Text.Substring(logStart);
                if (!selectionResult.Contains("升级文件") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(selectionResult, @"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]"))
                    throw new Exception("A failed upgrade-file selection must be recorded with a timestamp.");
                if (args.Length > 1 && args[1] == "selection-async")
                {
                    form.Close();
                    Console.WriteLine("PASS background validation and stale-result protection");
                    return 0;
                }
                CheckRejectedDeviceState(form, Path.GetFullPath(args[0]), 1);
                CheckRejectedDeviceState(form, Path.GetFullPath(args[0]), 2);
                Console.WriteLine("PASS local UI: valid/invalid selection and plugged/unknown admission");
                CheckCompleteTransfer(form, Path.GetFullPath(args[0]), work);
                CheckLongVersionLayout(form, work);
                CheckCancelVersionConfirmation(form, Path.GetFullPath(args[0]), work);
                CheckAdmissionChanges(form, Path.GetFullPath(args[0]), work);
                CheckObservedVersionStates(form, Path.GetFullPath(args[0]), work);
                Console.WriteLine("PASS local UI: immutable snapshot, acknowledged transfer and version waiting");
                CheckLostEndResponse(form, Path.GetFullPath(args[0]), work, 1);
                CheckLostEndResponse(form, Path.GetFullPath(args[0]), work, 2);
                CheckLostEndResponse(form, Path.GetFullPath(args[0]), work, 3);
                CheckInvalidEndFails(form, Path.GetFullPath(args[0]), work);
                CheckDeviceTaskFailure(form, Path.GetFullPath(args[0]), work);
                CheckLostEndUnconfirmed(form, Path.GetFullPath(args[0]), work, 0);
                CheckLostEndUnconfirmed(form, Path.GetFullPath(args[0]), work, 11);
                CheckLostEndUnconfirmed(form, Path.GetFullPath(args[0]), work, 4);
                CheckRecoveryQueryError(form, Path.GetFullPath(args[0]), work);
                CheckLostEndContinueAfterLookupFailure(form, Path.GetFullPath(args[0]), work);
                CheckLostBeginResponse(form, Path.GetFullPath(args[0]), work);
                CheckWrongOffsetStops(form, Path.GetFullPath(args[0]), work);
                Console.WriteLine("PASS local UI: lost responses, task states, explicit failure and bad offset");
                CheckCancelCloseDuringTransfer(form, Path.GetFullPath(args[0]), work);
                Console.WriteLine("PASS local UI: canceling window close preserves the transfer");
                CheckTimeoutSettings(form);
                CheckRebootWithUnknownTask(form, Path.GetFullPath(args[0]), work);
                CheckTimeoutAndNextPackage(form, Path.GetFullPath(args[0]), work, false);
                CheckTimeoutAndNextPackage(form, Path.GetFullPath(args[0]), work, true);
                SetWaitSeconds(form, 1, false);
                CheckRejectedNewTaskKeepsPrior(form, Path.GetFullPath(args[0]), work);
                CheckEndedWithoutVersionMatchTimesOut(form, Path.GetFullPath(args[0]), work, false);
                CheckEndedWithoutVersionMatchTimesOut(form, Path.GetFullPath(args[0]), work, true);
                CheckWaitDeadline(form, Path.GetFullPath(args[0]), work);
                Console.WriteLine("PASS local UI: timeout settings, reboot, A/B handoff and continue waiting");
                if (!Find<Button>(form, "ContinueUpgradeButton").Visible)
                    throw new Exception("A timed-out session must expose continued waiting before exit.");
                form.Close();
            }
            using (MainForm reopened = new MainForm())
            {
                reopened.Show(); Application.DoEvents();
                Find<Button>(reopened, "UpgradePageTab").PerformClick(); Application.DoEvents();
                if (Find<Button>(reopened, "ContinueUpgradeButton").Visible)
                    throw new Exception("Reopening must not restore the previous local task.");
                reopened.Close();
            }
            CheckConfirmCloseDuringTransfer(Path.GetFullPath(args[0]), work);
            Console.WriteLine("PASS local UI: confirmed close and session-only task state");
            Console.WriteLine("PASS local upgrade UI and loopback simulator");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
