using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SeccFirmwarePackager
{
    public enum FirmwareType : ushort { PFC = 1, LLC = 2, M4 = 3, PLC = 4, ESP32 = 5 }

    public sealed class FirmwareInput
    {
        public FirmwareType Type;
        public string Path;
        public string Name;
        public string Version;
    }

    public sealed class FirmwareEntry
    {
        public FirmwareType Type;
        public string Name;
        public string Version;
        public uint Offset;
        public uint Size;
        public byte[] Md5;
        internal string SourcePath;
    }

    public sealed class PackageReport
    {
        public readonly List<FirmwareEntry> Entries = new List<FirmwareEntry>();
        public readonly List<string> Warnings = new List<string>();
        public uint TotalSize;
        public byte[] PackageMd5;
    }

    public static class Packager
    {
        public const int HeaderSize = 4096;
        public const int Alignment = 4096;
        public const int MaxPackageSize = 6 * 1024 * 1024;
        public const int MaxEsp32Size = 5 * 1024 * 1024 / 2;
        public const int DirectoryOffset = 128;
        public const int EntrySize = 256;
        public const int PackageMd5Offset = 24;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SECCPKG1");
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static string Hex(byte[] bytes)
        {
            if (bytes == null) return "";
            StringBuilder s = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) s.Append(b.ToString("x2"));
            return s.ToString();
        }

        private static long Align(long n)
        {
            return (n + Alignment - 1) / Alignment * Alignment;
        }

        private static int Budget(FirmwareType type)
        {
            switch (type)
            {
                case FirmwareType.PFC:
                case FirmwareType.LLC: return 250 * 1024;
                case FirmwareType.M4: return 512 * 1024;
                case FirmwareType.PLC: return 2 * 1024 * 1024;
                case FirmwareType.ESP32: return MaxEsp32Size;
                default: throw new InvalidDataException("不支持的固件类型：" + (int)type);
            }
        }

        private static void CheckText(string value, int maxBytes, string label)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidDataException(label + "不能为空。");
            foreach (char c in value)
                if (Char.IsControl(c)) throw new InvalidDataException(label + "不能包含控制字符。");
            int count;
            try { count = Utf8.GetByteCount(value); }
            catch (EncoderFallbackException) { throw new InvalidDataException(label + "不是有效的 Unicode 字符串。"); }
            if (count > maxBytes)
                throw new InvalidDataException(String.Format("{0}的 UTF-8 长度为 {1} 字节，最多允许 {2} 字节；未截断。", label, count, maxBytes));
        }

        private static void AddBudgetWarning(PackageReport report, FirmwareType type, long size)
        {
            int budget = Budget(type);
            if (type == FirmwareType.ESP32 && size > budget)
                throw new InvalidDataException("ESP32 原始固件超过候选应用槽上限 2.5 MiB，不能打包。");
            if (type != FirmwareType.ESP32 && size > budget)
                report.Warnings.Add(String.Format("{0} 原始固件 {1} 字节超过建议预算 {2} 字节；总包满足限制时仍允许打包。", type, size, budget));
        }

        public static PackageReport Plan(IEnumerable<FirmwareInput> inputs)
        {
            if (inputs == null) throw new InvalidDataException("请至少选择一个固件。");
            List<FirmwareInput> selected = new List<FirmwareInput>();
            foreach (FirmwareInput input in inputs)
            {
                if (input == null) throw new InvalidDataException("固件选择包含空条目。");
                Budget(input.Type);
                selected.Add(input);
            }
            if (selected.Count == 0) throw new InvalidDataException("未选择固件，请至少选择一种固件。");
            if (selected.Count > 5) throw new InvalidDataException("固件数量不能超过 5 个。");
            selected.Sort(delegate(FirmwareInput a, FirmwareInput b) { return ((int)a.Type).CompareTo((int)b.Type); });
            PackageReport report = new PackageReport();
            long next = HeaderSize;
            FirmwareType last = 0;
            foreach (FirmwareInput input in selected)
            {
                if (last == input.Type) throw new InvalidDataException("固件类型重复：" + input.Type);
                last = input.Type;
                if (String.IsNullOrWhiteSpace(input.Path)) throw new InvalidDataException(input.Type + " 未选择固件文件。");
                CheckText(input.Name, 96, input.Type + " 固件名称");
                CheckText(input.Version, 64, input.Type + " 固件版本");
                long size;
                try
                {
                    using (FileStream stream = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        size = stream.Length;
                }
                catch (Exception e)
                {
                    if (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                        throw new InvalidDataException(input.Type + " 固件文件不可读取：" + e.Message, e);
                    throw;
                }
                if (size == 0) throw new InvalidDataException(input.Type + " 固件文件为空。");
                AddBudgetWarning(report, input.Type, size);
                if (size > MaxPackageSize || next + size > MaxPackageSize)
                    throw new InvalidDataException("联合包预计大小超过候选暂存区上限 6 MiB。");
                FirmwareEntry entry = new FirmwareEntry();
                entry.Type = input.Type;
                entry.Name = input.Name;
                entry.Version = input.Version;
                entry.Offset = (uint)next;
                entry.Size = (uint)size;
                entry.SourcePath = input.Path;
                report.Entries.Add(entry);
                next = Align(next + size);
                if (next > MaxPackageSize) throw new InvalidDataException("联合包加上对齐填充后超过候选暂存区上限 6 MiB。");
            }
            report.TotalSize = (uint)next;
            return report;
        }

        private static void Put16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static void Put32(byte[] data, int offset, uint value)
        {
            for (int i = 0; i < 4; i++) data[offset + i] = (byte)(value >> (8 * i));
        }

        private static ushort Get16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint Get32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private static byte[] Hash(byte[] data, int offset, int count)
        {
            using (MD5 md5 = MD5.Create()) return md5.ComputeHash(data, offset, count);
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static byte[] Slice(byte[] data, int offset, int count)
        {
            byte[] output = new byte[count];
            Buffer.BlockCopy(data, offset, output, 0, count);
            return output;
        }

        private static void CheckZero(byte[] data, int start, int end, string label)
        {
            for (int i = start; i < end; i++)
                if (data[i] != 0) throw new InvalidDataException(label + "必须全为 0x00，偏移 0x" + i.ToString("X") + " 非零。");
        }

        private static string ReadText(byte[] data, int offset, int length, int capacity, string label)
        {
            if (length < 1 || length > capacity) throw new InvalidDataException(label + "长度无效。");
            string result;
            try { result = Utf8.GetString(data, offset, length); }
            catch (DecoderFallbackException) { throw new InvalidDataException(label + "不是有效的 UTF-8。"); }
            CheckText(result, capacity, label);
            CheckZero(data, offset + length, offset + capacity, label + "未使用区域");
            return result;
        }

        public static PackageReport VerifyBytes(byte[] data)
        {
            if (data == null || data.Length < HeaderSize) throw new InvalidDataException("文件被截断：不足 4 KiB 包头。");
            if (data.Length > MaxPackageSize) throw new InvalidDataException("联合包超过候选 6 MiB 上限。");
            for (int i = 0; i < Magic.Length; i++) if (data[i] != Magic[i]) throw new InvalidDataException("包标识错误，文件不是本工具的 V1 联合包。");
            ushort version = Get16(data, 8);
            if (version != 1) throw new InvalidDataException("不支持的包格式版本：" + version);
            if (Get16(data, 10) != HeaderSize) throw new InvalidDataException("包头长度错误。");
            if (Get32(data, 12) != (uint)data.Length) throw new InvalidDataException("声明总长度与实际文件长度不一致，文件可能被截断。");
            if (data.Length % Alignment != 0) throw new InvalidDataException("联合包总长度未按 4 KiB 对齐。");
            int count = Get16(data, 16);
            if (count < 1 || count > 5) throw new InvalidDataException("固件数量必须为 1～5。");
            if (Get16(data, 18) != EntrySize || Get32(data, 20) != DirectoryOffset)
                throw new InvalidDataException("固件目录格式不受支持。");
            CheckZero(data, 40, DirectoryOffset, "包头预留区");
            CheckZero(data, DirectoryOffset + count * EntrySize, HeaderSize, "未使用的目录和包头区域");
            PackageReport report = new PackageReport();
            report.TotalSize = (uint)data.Length;
            report.PackageMd5 = Slice(data, PackageMd5Offset, 16);
            uint next = HeaderSize;
            int previousType = 0;
            for (int i = 0; i < count; i++)
            {
                int baseOffset = DirectoryOffset + i * EntrySize;
                int typeValue = Get16(data, baseOffset);
                if (typeValue < 1 || typeValue > 5 || typeValue <= previousType)
                    throw new InvalidDataException("固件类型不支持、重复或未按规定顺序排列。");
                previousType = typeValue;
                CheckZero(data, baseOffset + 2, baseOffset + 4, "目录预留区");
                CheckZero(data, baseOffset + 192, baseOffset + EntrySize, "目录扩展预留区");
                FirmwareEntry entry = new FirmwareEntry();
                entry.Type = (FirmwareType)typeValue;
                entry.Offset = Get32(data, baseOffset + 4);
                entry.Size = Get32(data, baseOffset + 8);
                entry.Md5 = Slice(data, baseOffset + 12, 16);
                entry.Name = ReadText(data, baseOffset + 32, Get16(data, baseOffset + 28), 96, entry.Type + " 固件名称");
                entry.Version = ReadText(data, baseOffset + 128, Get16(data, baseOffset + 30), 64, entry.Type + " 固件版本");
                if (entry.Size == 0) throw new InvalidDataException(entry.Type + " 固件大小为零。");
                if (entry.Offset != next || entry.Offset % Alignment != 0)
                    throw new InvalidDataException(entry.Type + " 固件偏移错误、未对齐、区域重叠或存在额外空隙。");
                long end = (long)entry.Offset + entry.Size;
                if (end > data.Length) throw new InvalidDataException(entry.Type + " 固件区域越界或文件被截断。");
                AddBudgetWarning(report, entry.Type, entry.Size);
                byte[] actual = Hash(data, (int)entry.Offset, (int)entry.Size);
                if (!Equal(actual, entry.Md5)) throw new InvalidDataException(entry.Type + " 固件 MD5 校验失败。");
                long alignedEnd = Align(end);
                if (alignedEnd > data.Length) throw new InvalidDataException(entry.Type + " 固件对齐区域越界。");
                for (long p = end; p < alignedEnd; p++)
                    if (data[(int)p] != 0xFF) throw new InvalidDataException(entry.Type + " 后的对齐填充不是 0xFF，偏移 0x" + p.ToString("X") + "。");
                next = (uint)alignedEnd;
                report.Entries.Add(entry);
            }
            if (next != data.Length) throw new InvalidDataException("最后一个固件之后存在多余区域，或包尾长度错误。");
            byte[] copy = (byte[])data.Clone();
            Array.Clear(copy, PackageMd5Offset, 16);
            byte[] packageHash = Hash(copy, 0, copy.Length);
            if (!Equal(packageHash, report.PackageMd5)) throw new InvalidDataException("整包 MD5 校验失败。");
            return report;
        }

        public static PackageReport VerifyFile(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new InvalidDataException("请选择要查看和校验的联合包。");
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > MaxPackageSize) throw new InvalidDataException("联合包超过候选 6 MiB 上限。");
                    byte[] data = new byte[(int)stream.Length];
                    ReadExactly(stream, data, 0, data.Length);
                    if (stream.ReadByte() != -1) throw new InvalidDataException("校验期间文件长度发生变化。");
                    return VerifyBytes(data);
                }
            }
            catch (Exception e)
            {
                if (e is InvalidDataException) throw;
                if (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                    throw new InvalidDataException("联合包不可读取：" + e.Message, e);
                throw;
            }
        }

        private static void ReadExactly(Stream stream, byte[] data, int offset, int count)
        {
            int done = 0;
            while (done < count)
            {
                int n = stream.Read(data, offset + done, count - done);
                if (n <= 0) throw new InvalidDataException("文件读取不完整，可能在读取期间发生变化。");
                done += n;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdentity
        {
            public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileIdentity information);

        private static bool SameExistingFile(string first, string second)
        {
            using (FileStream a = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (FileStream b = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                FileIdentity ia, ib;
                if (!GetFileInformationByHandle(a.SafeFileHandle, out ia) || !GetFileInformationByHandle(b.SafeFileHandle, out ib))
                    throw new IOException("无法确认输入与输出是否指向同一文件。");
                return ia.VolumeSerial == ib.VolumeSerial && ia.IndexHigh == ib.IndexHigh && ia.IndexLow == ib.IndexLow;
            }
        }

        public static PackageReport Build(IEnumerable<FirmwareInput> inputs, string outputPath)
        {
            PackageReport plan = Plan(inputs);
            if (String.IsNullOrWhiteSpace(outputPath)) throw new InvalidDataException("请选择联合包输出位置。");
            string destination = Path.GetFullPath(outputPath);
            if (Directory.Exists(destination)) throw new InvalidDataException("输出位置是目录，请选择 BIN 文件名。");
            string parent = Path.GetDirectoryName(destination);
            if (String.IsNullOrEmpty(parent) || !Directory.Exists(parent)) throw new InvalidDataException("输出目录不存在。");
            bool exists = File.Exists(destination);
            if (exists && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("为保护原有文件，输出不能覆盖符号链接或其他重解析点。");
            foreach (FirmwareEntry entry in plan.Entries)
            {
                string source = Path.GetFullPath(entry.SourcePath);
                bool same = String.Equals(destination, source, StringComparison.OrdinalIgnoreCase);
                if (!same && exists)
                {
                    try { same = SameExistingFile(destination, source); }
                    catch (Exception e)
                    {
                        if (e is IOException || e is UnauthorizedAccessException)
                            throw new InvalidDataException("无法核对已有输出与输入文件，请检查文件访问权限或占用状态：" + e.Message, e);
                        throw;
                    }
                }
                if (same)
                    throw new InvalidDataException("输出文件与输入固件指向同一个文件，已拒绝以保护输入固件。");
            }
            byte[] data = new byte[plan.TotalSize];
            for (int i = HeaderSize; i < data.Length; i++) data[i] = 0xFF;
            Buffer.BlockCopy(Magic, 0, data, 0, Magic.Length);
            Put16(data, 8, 1);
            Put16(data, 10, HeaderSize);
            Put32(data, 12, plan.TotalSize);
            Put16(data, 16, (ushort)plan.Entries.Count);
            Put16(data, 18, EntrySize);
            Put32(data, 20, DirectoryOffset);
            for (int i = 0; i < plan.Entries.Count; i++)
            {
                FirmwareEntry entry = plan.Entries[i];
                using (FileStream stream = new FileStream(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length != entry.Size) throw new InvalidDataException(entry.Type + " 固件大小已变化，请重新预览。");
                    ReadExactly(stream, data, (int)entry.Offset, (int)entry.Size);
                    if (stream.ReadByte() != -1) throw new InvalidDataException(entry.Type + " 固件大小已变化，请重新预览。");
                }
                entry.Md5 = Hash(data, (int)entry.Offset, (int)entry.Size);
                int p = DirectoryOffset + i * EntrySize;
                Put16(data, p, (ushort)entry.Type);
                Put32(data, p + 4, entry.Offset);
                Put32(data, p + 8, entry.Size);
                Buffer.BlockCopy(entry.Md5, 0, data, p + 12, 16);
                byte[] name = Utf8.GetBytes(entry.Name);
                byte[] version = Utf8.GetBytes(entry.Version);
                Put16(data, p + 28, (ushort)name.Length);
                Put16(data, p + 30, (ushort)version.Length);
                Buffer.BlockCopy(name, 0, data, p + 32, name.Length);
                Buffer.BlockCopy(version, 0, data, p + 128, version.Length);
            }
            byte[] hash = Hash(data, 0, data.Length);
            Buffer.BlockCopy(hash, 0, data, PackageMd5Offset, 16);
            VerifyBytes(data);
            string temporary = Path.Combine(parent, ".seccpkg-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                VerifyFile(temporary);
                if (exists) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            catch (Exception e)
            {
                if (e is IOException || e is UnauthorizedAccessException)
                    throw new InvalidDataException("输出写入、校验或替换失败：" + e.Message, e);
                throw;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return VerifyFile(destination);
        }
    }
}
