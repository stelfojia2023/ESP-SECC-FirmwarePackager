using System;

namespace SeccFirmwarePackager
{
    // Filename recognition only. Package validation and batch-selection policy belong to callers.
    internal static class FirmwareFileName
    {
        private static string BaseFileName(string path)
        {
            int separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return path.Substring(separator + 1);
        }

        internal static bool TryIdentifyType(string path, out FirmwareType type)
        {
            type = (FirmwareType)0;
            if (String.IsNullOrEmpty(path)) return false;
            string file = BaseFileName(path);
            foreach (FirmwareType candidate in new[] { FirmwareType.PFC, FirmwareType.LLC, FirmwareType.M4, FirmwareType.PLC, FirmwareType.ESP32 })
                if (file.StartsWith(candidate.ToString(), StringComparison.OrdinalIgnoreCase)) { type = candidate; return true; }
            return false;
        }

        internal static string PlcFileStem(string path)
        {
            string file = BaseFileName(path);
            int dot = file.LastIndexOf('.');
            if (dot <= 0) return file;
            for (int i = 0; i + 4 <= file.Length; i++)
            {
                if (i > 0 && file[i - 1] != '_') continue;
                if (String.Compare(file, i, "maya", 0, 4, StringComparison.OrdinalIgnoreCase) != 0) continue;
                int end = file.IndexOf('_', i);
                if (end < 0) end = file.Length;
                if (dot <= i + 3 || dot >= end || dot + 1 == end) continue;
                bool numeric = true;
                for (int n = dot + 1; n < end; n++)
                    if (file[n] < '0' || file[n] > '9') { numeric = false; break; }
                // Without a separate suffix, the final numeric segment is part of a maya version.
                if (numeric) return file;
            }
            return file.Substring(0, dot);
        }

        internal static string PlcVersionFromName(string stem)
        {
            for (int i = 0; i < stem.Length; i++)
            {
                if (i > 0 && stem[i - 1] != '_') continue;
                if (i + 4 <= stem.Length && String.Compare(stem, i, "maya", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    int end = i + 4;
                    while (end < stem.Length && stem[end] != '_') end++;
                    return stem.Substring(i, end - i);
                }
                if (i > 0 && (stem[i] == 'V' || stem[i] == 'v'))
                {
                    int end = i + 1;
                    while (end < stem.Length && stem[end] != '_' && stem[end] != '.') end++;
                    return stem.Substring(i, end - i);
                }
            }
            return String.Empty;
        }

        internal static string VersionFromName(string stem)
        {
            string version = String.Empty;
            // First underscore-prefixed V/v token; preserve its original spelling.
            for (int i = 0; i + 1 < stem.Length; i++)
            {
                if (stem[i] != '_' || (stem[i + 1] != 'V' && stem[i + 1] != 'v')) continue;
                int end = i + 2;
                while (end < stem.Length && stem[end] != '_' && stem[end] != '.') end++;
                version = stem.Substring(i + 1, end - i - 1);
                break;
            }
            return version;
        }

        internal static bool TryParse(string path, out FirmwareType type, out string name, out string version)
        {
            type = (FirmwareType)0;
            name = String.Empty;
            version = String.Empty;
            if (String.IsNullOrEmpty(path)) return false;
            string file = BaseFileName(path);
            FirmwareType detected;
            if (!TryIdentifyType(path, out detected)) return false;
            if (detected != FirmwareType.PLC && !file.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) return false;
            string stem = detected == FirmwareType.PLC ? PlcFileStem(path) : file.Substring(0, file.Length - 4);
            type = detected;
            name = stem;
            version = detected == FirmwareType.PLC ? PlcVersionFromName(stem) : VersionFromName(stem);
            return true;
        }
    }
}
