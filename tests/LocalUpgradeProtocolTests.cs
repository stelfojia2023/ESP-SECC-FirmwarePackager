using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Threading;
using SeccFirmwarePackager;

internal static class LocalUpgradeProtocolTests
{
    private sealed class PartialTransport : ILupTransport
    {
        private readonly byte[] reply;
        private int read;
        public readonly List<byte> Sent = new List<byte>();
        public PartialTransport(byte[] reply) { this.reply = reply; }
        public int Send(byte[] data, int offset, int count, int timeoutMs)
        {
            int n = Math.Min(3, count);
            for (int i = 0; i < n; i++) Sent.Add(data[offset + i]);
            return n;
        }
        public int Receive(byte[] data, int offset, int count, int timeoutMs)
        {
            int n = Math.Min(Math.Min(2, count), reply.Length - read);
            if (n > 0) Buffer.BlockCopy(reply, read, data, offset, n);
            read += n;
            return n;
        }
        public void Dispose() { }
    }

    private static void TestShortWritesAndReads()
    {
        PartialTransport transport = new PartialTransport(new byte[] {
            0x4c, 0x55, 0x50, 0x31, 1, 0x81, 0, 0, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0
        });
        using (Lup1Connection client = new Lup1Connection(transport))
        {
            if (client.QueryStatus(1000) != 0) throw new Exception("Valid status was not decoded.");
        }
        byte[] expected = new byte[] {
            0x4c, 0x55, 0x50, 0x31, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0
        };
        if (transport.Sent.Count != expected.Length) throw new Exception("A short write omitted request bytes.");
        for (int i = 0; i < expected.Length; i++)
            if (transport.Sent[i] != expected[i]) throw new Exception("Short writes changed the request frame.");
    }

    private static byte[] Frame(byte command, int sequence, params byte[] payload)
    {
        byte[] frame = new byte[16 + payload.Length];
        frame[0] = 0x4c; frame[1] = 0x55; frame[2] = 0x50; frame[3] = 0x31;
        frame[4] = 1; frame[5] = (byte)(command | 0x80);
        Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(sequence)), 0, frame, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length)), 0, frame, 12, 4);
        Buffer.BlockCopy(payload, 0, frame, 16, payload.Length);
        return frame;
    }

    private static byte[] Concat(byte[] left, byte[] right)
    {
        byte[] joined = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, joined, 0, left.Length);
        Buffer.BlockCopy(right, 0, joined, left.Length, right.Length);
        return joined;
    }

    private static void MustRejectStatus(byte[] response, string scenario)
    {
        bool rejected = false;
        using (Lup1Connection client = new Lup1Connection(new PartialTransport(response)))
        {
            try { client.QueryStatus(1000); }
            catch (InvalidDataException) { rejected = true; }
            catch (IOException) { rejected = true; }
        }
        if (!rejected) throw new Exception("Invalid status response accepted: " + scenario);
    }

    private static void TestInvalidFrames()
    {
        byte[] valid = Frame(1, 1, 0, 0, 0);
        foreach (int index in new[] { 0, 4, 5, 6, 7, 11 })
        {
            byte[] bad = (byte[])valid.Clone(); bad[index] ^= 1;
            MustRejectStatus(bad, "header byte " + index);
        }
        byte[] oversized = (byte[])valid.Clone();
        oversized[12] = 0; oversized[13] = 0; oversized[14] = 0x10; oversized[15] = 0x15;
        MustRejectStatus(oversized, "oversized payload");
        MustRejectStatus(Frame(1, 1, 0), "result code truncated");
        MustRejectStatus(Frame(1, 1, 0, 3, 0), "error response has extra bytes");
        MustRejectStatus(Frame(1, 1, 0, 0, 3), "unknown status");
        MustRejectStatus(Frame(1, 1, 0, 0, 0, 0), "extra status payload");
        byte[] truncated = new byte[17]; Buffer.BlockCopy(valid, 0, truncated, 0, truncated.Length);
        MustRejectStatus(truncated, "truncated response");

        bool busy = false;
        using (Lup1Connection client = new Lup1Connection(new PartialTransport(Frame(1, 1, 0, 3))))
        {
            try { client.QueryStatus(1000); }
            catch (LupDeviceException e) { busy = e.Code == 3 && e.Message.Contains("无法受理"); }
        }
        if (!busy) throw new Exception("Device busy result was not preserved.");
    }

    private static List<FirmwareEntry> Targets()
    {
        return new List<FirmwareEntry> {
            new FirmwareEntry { Type = FirmwareType.M4, Version = "V1" },
            new FirmwareEntry { Type = FirmwareType.ESP32, Version = "V2" }
        };
    }

    private static void MustRejectVersions(byte[] payload, string scenario)
    {
        bool rejected = false;
        using (Lup1Connection client = new Lup1Connection(new PartialTransport(Frame(6, 1, payload))))
        {
            try { client.QueryVersions(Targets(), 1000); }
            catch (InvalidDataException) { rejected = true; }
            catch (IOException) { rejected = true; }
        }
        if (!rejected) throw new Exception("Invalid version response accepted: " + scenario);
    }

    private static void TestVersionsAndCoalescing()
    {
        byte[] versions = new byte[] { 0, 0, 2, 3, 0, 2, 0x56, 0x31, 5, 2, 0 };
        PartialTransport transport = new PartialTransport(Concat(Frame(1, 1, 0, 0, 0), Frame(6, 2, versions)));
        using (Lup1Connection client = new Lup1Connection(transport))
        {
            if (client.QueryStatus(1000) != 0) throw new Exception("First coalesced frame was incorrect.");
            List<LupVersion> actual = client.QueryVersions(Targets(), 1000);
            if (actual.Count != 2 || actual[0].Value != "V1" || actual[1].Status != 2)
                throw new Exception("Second coalesced frame was incorrect.");
        }
        MustRejectVersions(new byte[] { 0, 0, 1 }, "wrong target count");
        MustRejectVersions(new byte[] { 0, 0, 2, 5, 1, 0, 3, 1, 0 }, "wrong target order");
        MustRejectVersions(new byte[] { 0, 0, 2, 3, 4, 0, 5, 1, 0 }, "unknown state");
        MustRejectVersions(new byte[] { 0, 0, 2, 3, 2, 1, 0x41, 5, 1, 0 }, "nonempty unavailable version");
        MustRejectVersions(new byte[] { 0, 0, 2, 3, 0, 0, 5, 1, 0 }, "empty valid version");
        MustRejectVersions(new byte[] { 0, 0, 2, 3, 0, 1, 0xff, 5, 1, 0 }, "invalid UTF-8");
        MustRejectVersions(new byte[] { 0, 0, 2, 3, 1, 0, 5, 1, 0, 0 }, "trailing byte");
    }

    private static void TestBadBlockAcknowledgements()
    {
        byte[] task = new byte[16];
        for (int i = 0; i < task.Length; i++) task[i] = (byte)i;
        byte[] reply = new byte[22];
        Buffer.BlockCopy(task, 0, reply, 2, 16);
        Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(4)), 0, reply, 18, 4);
        foreach (bool wrongId in new[] { false, true })
        {
            byte[] bad = (byte[])reply.Clone();
            if (wrongId) bad[2] ^= 1; else bad[21] = 3;
            bool rejected = false;
            using (Lup1Connection client = new Lup1Connection(new PartialTransport(Frame(3, 1, bad))))
            {
                try { client.SendBlock(task, 0, new byte[] { 1, 2, 3, 4 }, 1000); }
                catch (InvalidDataException) { rejected = true; }
            }
            if (!rejected) throw new Exception("Bad block acknowledgement accepted.");
        }
    }

    private static void TestPendingConnectCanBeCanceled()
    {
        using (ManualResetEvent pending = new ManualResetEvent(false))
        using (CancellationTokenSource cancel = new CancellationTokenSource())
        {
            Thread closer = new Thread(delegate() { Thread.Sleep(50); cancel.Cancel(); });
            closer.IsBackground = true; closer.Start();
            Stopwatch elapsed = Stopwatch.StartNew();
            bool stopped = false;
            try { Lup1Connection.WaitForConnect(pending, 10000, cancel.Token); }
            catch (OperationCanceledException) { stopped = true; }
            if (!closer.Join(1000) || !stopped || elapsed.ElapsedMilliseconds > 2000)
                throw new Exception("Closing must promptly cancel a pending connect wait.");
        }
    }

    private static int Main()
    {
        try
        {
            TestShortWritesAndReads();
            TestInvalidFrames();
            TestVersionsAndCoalescing();
            TestBadBlockAcknowledgements();
            TestPendingConnectCanBeCanceled();
            Console.WriteLine("PASS LUP1 short I/O, coalescing, malformed frames, version replies, block acknowledgements and connect cancellation");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
