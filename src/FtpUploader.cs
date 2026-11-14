using System;
using System.IO;
using System.Net;

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
        public int StatusCode;
        public long Bytes;
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
                    return new FtpUploadResult { RemoteUrl = target.AbsoluteUri, StatusCode = code, Bytes = sent };
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

