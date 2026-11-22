using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SeccFirmwarePackager
{
    // The exact bytes verified here are retained privately for the whole task.
    internal sealed class LocalUpgradeSnapshot
    {
        private readonly byte[] bytes;
        public readonly string Path;
        public readonly PackageReport Report;

        private LocalUpgradeSnapshot(string path, byte[] data, PackageReport report)
        {
            Path = path;
            bytes = data;
            Report = report;
        }

        public int Length { get { return bytes.Length; } }

        public byte[] ReadBlock(int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > bytes.Length - count)
                throw new ArgumentOutOfRangeException("offset");
            byte[] block = new byte[count];
            Buffer.BlockCopy(bytes, offset, block, 0, count);
            return block;
        }

        public static LocalUpgradeSnapshot Load(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new InvalidDataException("请选择联合包。");
            string fullPath = System.IO.Path.GetFullPath(path);
            byte[] data;
            using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > Packager.MaxPackageSize) throw new InvalidDataException("联合包超过 6 MiB 上限。");
                data = new byte[(int)stream.Length];
                int done = 0;
                while (done < data.Length)
                {
                    int n = stream.Read(data, done, data.Length - done);
                    if (n <= 0) throw new InvalidDataException("联合包读取不完整。");
                    done += n;
                }
                if (stream.ReadByte() != -1) throw new InvalidDataException("读取期间联合包长度发生变化。");
            }
            return new LocalUpgradeSnapshot(fullPath, data, Packager.VerifyBytes(data));
        }
    }

    internal sealed class LupDeviceException : IOException
    {
        public readonly int Code;
        public LupDeviceException(int code, string message) : base(message) { Code = code; }
    }

    internal sealed class LupVersion
    {
        public FirmwareType Type;
        public byte Status;
        public string Value;
    }

    // A boundary around the operating-system byte stream permits deterministic
    // short-read/write tests without replacing the LUP1 client.
    internal interface ILupTransport : IDisposable
    {
        int Send(byte[] data, int offset, int count, int timeoutMs);
        int Receive(byte[] data, int offset, int count, int timeoutMs);
    }

    internal sealed class LupSocketTransport : ILupTransport
    {
        private readonly Socket socket;
        public LupSocketTransport(Socket connected) { socket = connected; }
        public int Send(byte[] data, int offset, int count, int timeoutMs)
        {
            socket.SendTimeout = timeoutMs;
            return socket.Send(data, offset, count, SocketFlags.None);
        }
        public int Receive(byte[] data, int offset, int count, int timeoutMs)
        {
            socket.ReceiveTimeout = timeoutMs;
            return socket.Receive(data, offset, count, SocketFlags.None);
        }
        public void Dispose() { socket.Close(); }
    }

    internal sealed class Lup1Connection : IDisposable
    {
        private const int MaxPayload = 4116;
        private readonly ILupTransport transport;
        private uint sequence;

        internal static void WaitForConnect(WaitHandle completed, int timeoutMs, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            int signaled = WaitHandle.WaitAny(new WaitHandle[] { cancellation.WaitHandle, completed }, timeoutMs);
            if (signaled == 0) cancellation.ThrowIfCancellationRequested();
            if (signaled == WaitHandle.WaitTimeout) throw new TimeoutException("连接设备超时，请稍后人工重试。");
            cancellation.ThrowIfCancellationRequested();
        }

        public Lup1Connection(IPAddress address, int port, int connectTimeoutMs, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                IAsyncResult pending = socket.BeginConnect(address, port, null, null);
                using (System.Threading.WaitHandle wait = pending.AsyncWaitHandle)
                {
                    WaitForConnect(wait, connectTimeoutMs, cancellation);
                    socket.EndConnect(pending);
                }
                transport = new LupSocketTransport(socket);
            }
            catch { socket.Close(); throw; }
        }

        internal Lup1Connection(ILupTransport transport)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            this.transport = transport;
        }

        private static uint Get32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
                ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        private static void Put32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24); data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8); data[offset + 3] = (byte)value;
        }

        private int Remaining(Stopwatch watch, int timeoutMs)
        {
            long remaining = timeoutMs - watch.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException("设备响应超时，请稍后人工重试。");
            return (int)Math.Min(Int32.MaxValue, remaining);
        }

        private void SendAll(byte[] data, Stopwatch watch, int timeoutMs)
        {
            int done = 0;
            while (done < data.Length)
            {
                int n = transport.Send(data, done, data.Length - done, Remaining(watch, timeoutMs));
                if (n <= 0 || n > data.Length - done) throw new IOException("向设备发送时连接已关闭。");
                done += n;
            }
        }

        private void ReceiveAll(byte[] data, Stopwatch watch, int timeoutMs)
        {
            int done = 0;
            while (done < data.Length)
            {
                int n = transport.Receive(data, done, data.Length - done, Remaining(watch, timeoutMs));
                if (n <= 0 || n > data.Length - done) throw new IOException("设备应答不完整或连接已关闭。");
                done += n;
            }
        }

        private static string ResultMessage(int code)
        {
            switch (code)
            {
                case 1: return "设备报告帧格式错误。";
                case 2: return "设备不支持该协议版本或命令。";
                case 3: return "设备忙或无法受理，请检查容量和设备状态后人工重试。";
                case 4: return "设备已插枪，请先拔枪。";
                case 5: return "设备状态未知或升级准入不满足。";
                case 6: return "设备报告长度、偏移或参数错误。";
                case 7: return "设备暂存或内部处理失败。";
                case 8: return "设备报告联合包格式错误。";
                case 9: return "设备报告联合包 MD5 错误。";
                case 10: return "设备当前会话状态不允许此操作。";
                case 11: return "设备无法关联本次任务。";
                case 12: return "联合包包含当前设备不可执行的目标。";
                case 13: return "设备未能暂停平台连接。";
                default: return "设备返回未知结果码：" + code + "。";
            }
        }

        public byte[] Exchange(byte command, byte[] payload, int timeoutMs)
        {
            if (payload == null) payload = new byte[0];
            if (payload.Length > MaxPayload) throw new InvalidDataException("LUP1 请求超过长度上限。");
            byte[] request = new byte[16 + payload.Length];
            request[0] = 0x4c; request[1] = 0x55; request[2] = 0x50; request[3] = 0x31;
            request[4] = 1; request[5] = command;
            uint current = ++sequence;
            Put32(request, 8, current); Put32(request, 12, (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, request, 16, payload.Length);
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                SendAll(request, watch, timeoutMs);
                byte[] header = new byte[16];
                ReceiveAll(header, watch, timeoutMs);
                if (header[0] != 0x4c || header[1] != 0x55 || header[2] != 0x50 || header[3] != 0x31 ||
                    header[4] != 1 || header[5] != (byte)(command | 0x80) || header[6] != 0 || header[7] != 0 ||
                    Get32(header, 8) != current)
                    throw new InvalidDataException("设备应答帧头、命令或序号错误。");
                uint length = Get32(header, 12);
                if (length < 2 || length > MaxPayload) throw new InvalidDataException("设备应答长度无效。");
                byte[] response = new byte[(int)length];
                ReceiveAll(response, watch, timeoutMs);
                int code = (response[0] << 8) | response[1];
                if (code != 0)
                {
                    if (response.Length != 2) throw new InvalidDataException("设备错误应答长度无效。");
                    throw new LupDeviceException(code, ResultMessage(code));
                }
                byte[] result = new byte[response.Length - 2];
                Buffer.BlockCopy(response, 2, result, 0, result.Length);
                return result;
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.TimedOut)
                    throw new TimeoutException("设备响应超时，请稍后人工重试。", e);
                throw new IOException("设备连接异常：" + e.Message, e);
            }
        }

        public byte QueryStatus(int timeoutMs)
        {
            byte[] result = Exchange(1, null, timeoutMs);
            if (result.Length != 1 || result[0] > 2) throw new InvalidDataException("设备状态应答无效。");
            return result[0];
        }

        public List<LupVersion> QueryVersions(IList<FirmwareEntry> entries, int timeoutMs)
        {
            if (entries == null || entries.Count < 1 || entries.Count > 5)
                throw new InvalidDataException("版本查询目标数量无效。");
            byte[] payload = new byte[entries.Count + 1];
            payload[0] = (byte)entries.Count;
            for (int i = 0; i < entries.Count; i++) payload[i + 1] = (byte)entries[i].Type;
            byte[] reply = Exchange(6, payload, timeoutMs);
            if (reply.Length < 1 || reply[0] != entries.Count)
                throw new InvalidDataException("设备版本应答的目标数量错误。");
            List<LupVersion> versions = new List<LupVersion>();
            UTF8Encoding utf8 = new UTF8Encoding(false, true);
            int offset = 1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (reply.Length - offset < 3) throw new InvalidDataException("设备版本应答被截断。");
                int type = reply[offset++], state = reply[offset++], length = reply[offset++];
                if (type != (int)entries[i].Type || state > 3 || length > reply.Length - offset ||
                    (state == 0 && length == 0) || (state != 0 && length != 0))
                    throw new InvalidDataException("设备版本应答的目标、状态或长度无效。");
                string value = null;
                if (state == 0)
                {
                    try { value = utf8.GetString(reply, offset, length); }
                    catch (DecoderFallbackException) { throw new InvalidDataException("设备版本不是有效文本。"); }
                    foreach (char c in value)
                        if (Char.IsControl(c)) throw new InvalidDataException("设备版本包含无效字符。");
                }
                offset += length;
                versions.Add(new LupVersion { Type = entries[i].Type, Status = (byte)state, Value = value });
            }
            if (offset != reply.Length) throw new InvalidDataException("设备版本应答存在多余数据。");
            return versions;
        }

        private static void CheckTask(byte[] task)
        {
            if (task == null || task.Length != 16) throw new InvalidDataException("设备任务标识无效。");
        }

        private static bool SameTask(byte[] actual, byte[] expected)
        {
            if (actual.Length < 16) return false;
            for (int i = 0; i < 16; i++) if (actual[i] != expected[i]) return false;
            return true;
        }

        public byte[] BeginPackage(int length, int timeoutMs)
        {
            if (length < 8192 || length > Packager.MaxPackageSize || length % Packager.Alignment != 0)
                throw new InvalidDataException("联合包长度不符合设备协议。");
            byte[] request = new byte[4];
            Put32(request, 0, (uint)length);
            byte[] task = Exchange(2, request, timeoutMs);
            CheckTask(task);
            return task;
        }

        public void SendBlock(byte[] task, int offset, byte[] block, int timeoutMs)
        {
            CheckTask(task);
            if (offset < 0 || block == null || block.Length < 1 || block.Length > 4096)
                throw new InvalidDataException("升级数据块长度或偏移无效。");
            byte[] request = new byte[20 + block.Length];
            Buffer.BlockCopy(task, 0, request, 0, 16);
            Put32(request, 16, (uint)offset);
            Buffer.BlockCopy(block, 0, request, 20, block.Length);
            byte[] reply = Exchange(3, request, timeoutMs);
            if (reply.Length != 20 || !SameTask(reply, task) || Get32(reply, 16) != (uint)(offset + block.Length))
                throw new InvalidDataException("设备确认的任务或下一偏移不正确。");
        }

        public void EndPackage(byte[] task, int timeoutMs)
        {
            CheckTask(task);
            byte[] reply = Exchange(4, task, timeoutMs);
            if (reply.Length != 16 || !SameTask(reply, task))
                throw new InvalidDataException("设备结束应答的任务标识不正确。");
        }

        public byte QueryTask(byte[] task, int timeoutMs)
        {
            CheckTask(task);
            byte[] reply = Exchange(5, task, timeoutMs);
            if (reply.Length != 1 || reply[0] > 4)
                throw new InvalidDataException("设备任务状态应答无效。");
            return reply[0];
        }

        public void Dispose() { transport.Dispose(); }
    }
}
