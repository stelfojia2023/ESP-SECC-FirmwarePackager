using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace SeccFirmwarePackager
{
    public sealed class FtpLocation
    {
        public Uri DirectoryUri;
        public bool HasCredentials;
        public string Username;
        public string Password;
    }

    public sealed class FtpUploadResult
    {
        public string RemoteUrl;
        public string OcppDownloadUrl;
        public string OcppDownloadProblem;
        public int StatusCode;
        public long Bytes;
    }

    public sealed class FtpDirectoryResult
    {
        public string DirectoryUrl;
        public string[] Entries;
        public string EncodingName;
    }

    public static class FtpUploader
    {
        public static FtpLocation ParseAddress(string address)
        {
            if (!String.IsNullOrEmpty(address) && (address.IndexOf('?') >= 0 || address.IndexOf('#') >= 0))
                throw new InvalidDataException("FTP 地址不能包含未转义的查询参数或片段。");
            Uri uri;
            if (String.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out uri) ||
                !String.Equals(uri.Scheme, Uri.UriSchemeFtp, StringComparison.OrdinalIgnoreCase) ||
                String.IsNullOrWhiteSpace(uri.Host) || uri.Port < 1 || uri.Port > 65535)
                throw new InvalidDataException("FTP 地址无效，请使用 ftp://服务器:端口/目录 格式。");
            if (!String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment))
                throw new InvalidDataException("FTP 地址不能包含查询参数或片段。");

            FtpLocation location = new FtpLocation();
            string userInfo = uri.GetComponents(UriComponents.UserInfo, UriFormat.UriEscaped);
            location.HasCredentials = !String.IsNullOrEmpty(userInfo);
            if (location.HasCredentials)
            {
                int separator = userInfo.IndexOf(':');
                string user = separator < 0 ? userInfo : userInfo.Substring(0, separator);
                string password = separator < 0 ? "" : userInfo.Substring(separator + 1);
                try
                {
                    location.Username = Uri.UnescapeDataString(user);
                    location.Password = Uri.UnescapeDataString(password);
                }
                catch (UriFormatException) { throw new InvalidDataException("FTP 用户名或密码的 URL 编码无效。"); }
                ValidateCredentials(location.Username, location.Password);
            }
            UriBuilder safe = new UriBuilder(uri);
            safe.UserName = "";
            safe.Password = "";
            location.DirectoryUri = safe.Uri;
            return location;
        }

        private static void ValidateCredentials(string username, string password)
        {
            if (String.IsNullOrWhiteSpace(username)) throw new InvalidDataException("请输入 FTP 用户名。");
            if (username.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                (password != null && password.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
                throw new InvalidDataException("FTP 用户名或密码不能包含换行或空字符。");
        }

        public static Uri FileUri(FtpLocation location, string fileName)
        {
            if (location == null || location.DirectoryUri == null) throw new InvalidDataException("FTP 地址未设置。");
            if (String.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
                throw new InvalidDataException("上传文件名无效。");
            string prefix = location.DirectoryUri.AbsoluteUri;
            if (!prefix.EndsWith("/", StringComparison.Ordinal)) prefix += "/";
            return new Uri(prefix + Uri.EscapeDataString(fileName), UriKind.Absolute);
        }

        private static bool UserInfoChar(char value, bool password)
        {
            return (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') ||
                (value >= '0' && value <= '9') || "-._~!$&'()*+,;=".IndexOf(value) >= 0 ||
                (password && value == ':');
        }

        // ESP-SECC receives at most 122 URL bytes through OCPP and its FTP URL
        // parser does not decode percent escapes. Never offer a different path
        // or credentials as a usable device URL; FTP upload itself is unaffected.
        public static string OcppDownloadUrl(Uri target, string username, string password, out string problem)
        {
            problem = null;
            password = password ?? "";
            if (String.IsNullOrEmpty(username) || username.Length > 47 || password.Length > 47)
            { problem = "账号或密码超过设备支持的长度。"; return null; }
            foreach (char c in username)
                if (!UserInfoChar(c, false)) { problem = "账号包含设备下载地址不支持的字符。"; return null; }
            foreach (char c in password)
                if (!UserInfoChar(c, true)) { problem = "密码包含设备下载地址不支持的字符。"; return null; }
            if (target == null || !target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeFtp ||
                target.HostNameType == UriHostNameType.IPv6 || target.Host.Length > 95 ||
                !String.IsNullOrEmpty(target.UserInfo) || !String.IsNullOrEmpty(target.Query) || !String.IsNullOrEmpty(target.Fragment))
            { problem = "FTP 服务器地址不符合设备下载要求。"; return null; }
            string baseUrl = target.AbsoluteUri;
            string path = target.AbsolutePath;
            int slash = path.LastIndexOf('/');
            if (slash < 0 || path.Length - slash - 1 < 1 || path.Length - slash - 1 > 80 || slash > 128)
            { problem = "FTP 目录或文件名超过设备支持的长度。"; return null; }
            foreach (char c in baseUrl)
                if (c > 0x7E || c <= 0x20 || c == '%' || c == '?' || c == '#' || c == '|' || c == '[' || c == ']')
                { problem = "FTP 目录或文件名含有设备不支持的字符或转义，请调整后重新上传。"; return null; }
            string result = "ftp://" + username + ":" + password + "@" + baseUrl.Substring("ftp://".Length);
            if (result.Length > 122)
            { problem = "下载地址超过设备支持的 122 字节，请缩短目录、文件名或账号。"; return null; }
            return result;
        }

        // NLST is read-only. Report success only after the server confirms the
        // complete transfer; a partial listing must not be mistaken for absence.
        public static FtpDirectoryResult ListDirectory(string address, string username, string password)
        {
            FtpLocation location = ParseAddress(address);
            if (location.HasCredentials)
            {
                username = location.Username;
                password = location.Password;
            }
            ValidateCredentials(username, password);
            string directory = location.DirectoryUri.AbsoluteUri;
            if (!directory.EndsWith("/", StringComparison.Ordinal)) directory += "/";
            FtpWebRequest request = null;
            bool completed = false;
            try
            {
                request = (FtpWebRequest)WebRequest.Create(new Uri(directory));
                request.Method = WebRequestMethods.Ftp.ListDirectory;
                request.Credentials = new NetworkCredential(username, password ?? "");
                request.UseBinary = true;
                request.UsePassive = true;
                request.KeepAlive = false;
                request.Proxy = null;
                request.Timeout = 60000;
                request.ReadWriteTimeout = 60000;
                byte[] bytes;
                int code;
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    using (Stream stream = response.GetResponseStream())
                    using (MemoryStream content = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192];
                        int count;
                        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (content.Length + count > 4 * 1024 * 1024)
                                throw new InvalidDataException("FTP 目录内容超过 4 MiB，无法完整显示。");
                            content.Write(buffer, 0, count);
                        }
                        bytes = content.ToArray();
                    }
                    code = (int)response.StatusCode;
                    if (code != 226 && code != 250)
                        throw new InvalidDataException("FTP 服务器未确认目录读取完成（状态 " + code + "）。");
                }
                string listing;
                string encodingName = "UTF-8";
                try { listing = new UTF8Encoding(false, true).GetString(bytes); }
                catch (DecoderFallbackException)
                {
                    try
                    {
                        listing = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
                        encodingName = "GBK";
                    }
                    catch (DecoderFallbackException)
                    {
                        throw new InvalidDataException("无法识别 FTP 目录字符编码，请检查服务器编码。");
                    }
                }
                List<string> entries = new List<string>();
                using (StringReader reader = new StringReader(listing))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        if (entries.Count >= 10000)
                            throw new InvalidDataException("FTP 目录超过 10,000 个条目，无法完整显示。");
                        entries.Add(line);
                    }
                }
                completed = true;
                return new FtpDirectoryResult { DirectoryUrl = directory, Entries = entries.ToArray(), EncodingName = encodingName };
            }
            catch (WebException e)
            {
                int code = 0;
                FtpWebResponse response = e.Response as FtpWebResponse;
                if (response != null) { code = (int)response.StatusCode; response.Close(); }
                if (code == 530) throw new InvalidDataException("FTP 认证失败（530），请核对用户名和密码。");
                if (code == 550) throw new InvalidDataException("FTP 拒绝读取目录（550），请检查目标目录及权限。");
                if (code != 0) throw new InvalidDataException("FTP 服务器返回失败（" + code + "）。");
                if (e.Status == WebExceptionStatus.Timeout) throw new InvalidDataException("FTP 连接或读取目录超时。");
                if (e.Status == WebExceptionStatus.NameResolutionFailure) throw new InvalidDataException("无法解析 FTP 服务器地址。");
                if (e.Status == WebExceptionStatus.ConnectFailure) throw new InvalidDataException("无法连接 FTP 服务器。");
                throw new InvalidDataException("FTP 连接或读取目录失败，请检查网络、目录及服务器状态。");
            }
            catch (InvalidDataException) { throw; }
            catch (IOException) { throw new InvalidDataException("读取 FTP 目录失败，请检查网络及服务器状态。"); }
            finally
            {
                if (!completed && request != null) try { request.Abort(); } catch (Exception) { }
            }
        }

        public static FtpUploadResult Upload(string packagePath, string address, string username, string password)
        {
            FtpLocation location = ParseAddress(address);
            if (location.HasCredentials)
            {
                username = location.Username;
                password = location.Password;
            }
            ValidateCredentials(username, password);
            if (String.IsNullOrWhiteSpace(packagePath)) throw new InvalidDataException("请选择已生成的联合包文件。");
            byte[] bytes;
            try
            {
                using (FileStream input = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length > Packager.MaxPackageSize) throw new InvalidDataException("联合包超过候选 6 MiB 上限。");
                    bytes = new byte[(int)input.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int n = input.Read(bytes, offset, bytes.Length - offset);
                        if (n <= 0) throw new InvalidDataException("读取联合包不完整。");
                        offset += n;
                    }
                    if (input.ReadByte() != -1) throw new InvalidDataException("读取期间联合包大小发生变化。");
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception e)
            {
                if (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                    throw new InvalidDataException("联合包不可读取：" + e.Message, e);
                throw;
            }
            Packager.VerifyBytes(bytes);
            Uri target = FileUri(location, Path.GetFileName(packagePath));
            using (MemoryStream input = new MemoryStream(bytes, false))
                return UploadStream(input, bytes.Length, target, username, password);
        }

        // Ordinary files are streamed as-is. This is deliberately separate from Upload,
        // whose package format verification and candidate 6 MiB limit remain mandatory.
        public static FtpUploadResult UploadFile(string path, string address, string username, string password)
        {
            FtpLocation location = ParseAddress(address);
            if (location.HasCredentials)
            {
                username = location.Username;
                password = location.Password;
            }
            ValidateCredentials(username, password);
            if (String.IsNullOrWhiteSpace(path)) throw new InvalidDataException("请选择要上传的文件。");
            try
            {
                using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Uri target = FileUri(location, Path.GetFileName(path));
                    return UploadStream(input, input.Length, target, username, password);
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception e)
            {
                if (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                    throw new InvalidDataException("上传文件不可读取，请检查文件路径及访问权限。");
                throw;
            }
        }

        private static FtpUploadResult UploadStream(Stream input, long length, Uri target, string username, string password)
        {
            FtpWebRequest request = null;
            bool completed = false;
            try
            {
                request = (FtpWebRequest)WebRequest.Create(target);
                request.Method = WebRequestMethods.Ftp.UploadFile;
                request.Credentials = new NetworkCredential(username, password ?? "");
                request.UseBinary = true;
                request.UsePassive = true;
                request.KeepAlive = false;
                request.Proxy = null;
                request.Timeout = 60000;
                request.ReadWriteTimeout = 60000;
                request.ContentLength = length;
                long sent = 0;
                byte[] buffer = new byte[64 * 1024];
                using (Stream stream = request.GetRequestStream())
                {
                    while (sent < length)
                    {
                        int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, length - sent));
                        if (read <= 0) throw new InvalidDataException("读取上传文件不完整，传输已中止。");
                        stream.Write(buffer, 0, read);
                        sent += read;
                    }
                    if (input.ReadByte() != -1) throw new InvalidDataException("读取期间文件大小发生变化，传输已中止。");
                }
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    if (code != 226 && code != 250)
                        throw new InvalidDataException("FTP 服务器未确认传输完成（状态 " + code + "）。");
                    completed = true;
                    string problem;
                    string downloadUrl = OcppDownloadUrl(target, username, password, out problem);
                    return new FtpUploadResult { RemoteUrl = target.AbsoluteUri, OcppDownloadUrl = downloadUrl,
                        OcppDownloadProblem = problem, StatusCode = code, Bytes = sent };
                }
            }
            catch (WebException e)
            {
                int code = 0;
                FtpWebResponse response = e.Response as FtpWebResponse;
                if (response != null) { code = (int)response.StatusCode; response.Close(); }
                if (code == 530) throw new InvalidDataException("FTP 认证失败（530），请核对用户名和密码。");
                if (code == 550) throw new InvalidDataException("FTP 拒绝写入（550），请检查目标目录及权限。");
                if (code != 0) throw new InvalidDataException("FTP 服务器返回失败（" + code + "）。");
                if (e.Status == WebExceptionStatus.Timeout) throw new InvalidDataException("FTP 连接或上传超时。");
                if (e.Status == WebExceptionStatus.NameResolutionFailure) throw new InvalidDataException("无法解析 FTP 服务器地址。");
                if (e.Status == WebExceptionStatus.ConnectFailure) throw new InvalidDataException("无法连接 FTP 服务器。");
                throw new InvalidDataException("FTP 连接或上传失败，请检查网络、目录及服务器状态。");
            }
            catch (InvalidDataException) { throw; }
            catch (IOException)
            {
                throw new InvalidDataException("读取文件或传输数据失败，请检查文件及网络状态。");
            }
            finally
            {
                if (!completed && request != null) try { request.Abort(); } catch (Exception) { }
            }
        }
    }
}

