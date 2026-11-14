using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SeccFirmwarePackager;

internal static class FtpTests
{
    internal sealed class Server : IDisposable
    {
        private readonly TcpListener listener;
        private readonly Thread thread;
        private readonly string expectedUser;
        private readonly string expectedPassword;
        private readonly int finalCode;
        public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
        public byte[] Uploaded;
        public string StoredPath;
        public Exception Error;

        public Server(string user, string password, int code)
        {
            expectedUser = user;
            expectedPassword = password;
            finalCode = code;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Start();
        }

        private void Run()
        {
            TcpListener passive = null;
            try
            {
                using (TcpClient control = listener.AcceptTcpClient())
                using (NetworkStream stream = control.GetStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                {
                    control.ReceiveTimeout = 15000;
                    control.SendTimeout = 15000;
                    writer.NewLine = "\r\n";
                    writer.AutoFlush = true;
                    writer.WriteLine("220 Local FTP test fixture");
                    string current = "/";
                    bool correctUser = false;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int space = line.IndexOf(' ');
                        string command = (space < 0 ? line : line.Substring(0, space)).ToUpperInvariant();
                        string value = space < 0 ? "" : line.Substring(space + 1);
                        switch (command)
                        {
                            case "USER": correctUser = value == expectedUser; writer.WriteLine("331 Password required"); break;
                            case "PASS": writer.WriteLine(correctUser && value == expectedPassword ? "230 Logged in" : "530 Authentication failed"); break;
                            case "OPTS": writer.WriteLine("200 UTF8 enabled"); break;
                            case "FEAT": writer.WriteLine("211-Features\r\n UTF8\r\n211 End"); break;
                            case "SYST": writer.WriteLine("215 UNIX Type: L8"); break;
                            case "PWD": writer.WriteLine("257 \"/\" is current directory"); break;
                            case "CWD": current = value.StartsWith("/") ? value : current.TrimEnd('/') + "/" + value; writer.WriteLine("250 Directory changed"); break;
                            case "TYPE": writer.WriteLine("200 Type set"); break;
                            case "PASV":
                            case "EPSV":
                                if (passive != null) passive.Stop();
                                passive = new TcpListener(IPAddress.Loopback, 0);
                                passive.Start();
                                int p = ((IPEndPoint)passive.LocalEndpoint).Port;
                                writer.WriteLine(command == "EPSV" ? "229 Entering Extended Passive Mode (|||" + p + "|)" :
                                    "227 Entering Passive Mode (127,0,0,1," + (p / 256) + "," + (p % 256) + ")");
                                break;
                            case "STOR":
                                StoredPath = value.StartsWith("/") ? value : current.TrimEnd('/') + "/" + value;
                                if (finalCode == 550) { writer.WriteLine("550 Cannot store file"); break; }
                                if (passive == null) { writer.WriteLine("425 No data connection"); break; }
                                writer.WriteLine("150 Open data connection");
                                using (TcpClient data = passive.AcceptTcpClient())
                                using (NetworkStream incoming = data.GetStream())
                                using (MemoryStream received = new MemoryStream())
                                {
                                    incoming.CopyTo(received);
                                    Uploaded = received.ToArray();
                                }
                                passive.Stop(); passive = null;
                                writer.WriteLine(finalCode == 226 ? "226 Transfer complete" : (finalCode == 250 ? "250 Transfer complete" : "451 Transfer failed after data"));
                                break;
                            case "QUIT": writer.WriteLine("221 Goodbye"); return;
                            default: writer.WriteLine("200 OK"); break;
                        }
                    }
                }
            }
            catch (Exception e) { if (!(e is SocketException) && !(e is ObjectDisposedException) && !(e is IOException)) Error = e; }
            finally { if (passive != null) passive.Stop(); }
        }

        public void Dispose()
        {
            listener.Stop();
            thread.Join(2000);
        }
    }

    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void MustFail(Action action, string expected, string secret)
    {
        try { action(); }
        catch (InvalidDataException e)
        {
            Check(e.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0, "Unexpected error classification");
            Check(String.IsNullOrEmpty(secret) || e.ToString().IndexOf(secret, StringComparison.Ordinal) < 0, "Credential leaked in error");
            return;
        }
        throw new Exception("Expected FTP operation to fail: " + expected);
    }

    private static void AddressTests()
    {
        FtpLocation location = FtpUploader.ParseAddress("ftp://test-user:pass.with.dot@127.0.0.1:2121/upgrade");
        Check(location.HasCredentials && location.Username == "test-user" && location.Password == "pass.with.dot", "Credentials not parsed");
        Check(location.DirectoryUri.Host == "127.0.0.1" && location.DirectoryUri.Port == 2121 && location.DirectoryUri.AbsolutePath == "/upgrade", "Host, port or path wrong");
        Check(location.DirectoryUri.UserInfo == "" && !location.DirectoryUri.AbsoluteUri.Contains("pass.with.dot"), "Address not sanitized");
        location = FtpUploader.ParseAddress("ftp://u%40example:p%40ss%3Aword@localhost/up%20grade");
        Check(location.Username == "u@example" && location.Password == "p@ss:word" && location.DirectoryUri.Port == 21, "Encoded credentials or default port wrong");
        Check(FtpUploader.FileUri(location, "firmware.bin").AbsoluteUri == "ftp://localhost/up%20grade/firmware.bin", "Destination path wrong");
        location = FtpUploader.ParseAddress("ftp://localhost:2121/upgrade/");
        Check(!location.HasCredentials, "Unexpected credential inference");
        MustFail(delegate { FtpUploader.ParseAddress("https://localhost/path"); }, "FTP 地址无效", null);
        MustFail(delegate { FtpUploader.ParseAddress("ftp://bad%0Auser:fake-secret@localhost/path"); }, "换行", "fake-secret");
        MustFail(delegate { FtpUploader.ParseAddress("ftp://localhost/path?query=1"); }, "查询参数", null);
        Console.WriteLine("PASS FTP address parsing, credential masking and encoded characters");
    }

    private static void UploadTests(string packagePath, string work)
    {
        byte[] expected = File.ReadAllBytes(packagePath);
        using (Server server = new Server("test-user", "fake.with.dot", 226))
        {
            string address = "ftp://test-user:fake.with.dot@127.0.0.1:" + server.Port + "/upgrade";
            FtpUploadResult result = FtpUploader.Upload(packagePath, address, "", "");
            Check(result.StatusCode == 226 && result.Bytes == expected.Length, "Transfer did not complete");
            Check(result.RemoteUrl.IndexOf("test-user", StringComparison.Ordinal) < 0 && result.RemoteUrl.IndexOf("fake.with.dot", StringComparison.Ordinal) < 0, "Credentials in returned URL");
            Check(server.StoredPath == "/upgrade/" + Path.GetFileName(packagePath), "Remote file path mismatch: " + server.StoredPath);
            Check(server.Uploaded != null && Convert.ToBase64String(server.Uploaded) == Convert.ToBase64String(expected), "Uploaded bytes mismatch");
            Check(server.Error == null, "FTP fixture error");
        }
        string chinesePath = Path.Combine(work, "固件 包 测试.bin");
        File.WriteAllBytes(chinesePath, expected);
        using (Server server = new Server("u@example", "p@ss:word", 250))
        {
            string address = "ftp://u%40example:p%40ss%3Aword@127.0.0.1:" + server.Port + "/目录%20测试";
            FtpUploadResult result = FtpUploader.Upload(chinesePath, address, "", "");
            Check(result.StatusCode == 250 && result.Bytes == expected.Length, "250 final completion was not accepted");
            Check(server.StoredPath == "/目录 测试/固件 包 测试.bin", "Unicode or space in remote path was not preserved: " + server.StoredPath);
            Check(server.Uploaded != null && Convert.ToBase64String(server.Uploaded) == Convert.ToBase64String(expected), "Unicode-path upload bytes mismatch");
        }
        using (Server server = new Server("correct", "fake-password", 226))
        {
            string address = "ftp://wrong:do-not-print@127.0.0.1:" + server.Port + "/upgrade";
            MustFail(delegate { FtpUploader.Upload(packagePath, address, "", ""); }, "认证失败", "do-not-print");
            Check(server.Uploaded == null, "Authentication failure must not upload data");
        }
        using (Server server = new Server("test-user", "fake-password", 550))
        {
            string address = "ftp://127.0.0.1:" + server.Port + "/upgrade";
            MustFail(delegate { FtpUploader.Upload(packagePath, address, "test-user", "fake-password"); }, "拒绝写入", "fake-password");
        }
        using (Server server = new Server("test-user", "fake-password", 451))
        {
            string address = "ftp://127.0.0.1:" + server.Port + "/upgrade";
            MustFail(delegate { FtpUploader.Upload(packagePath, address, "test-user", "fake-password"); }, "451", "fake-password");
            Check(server.Uploaded != null, "Fixture must receive data before rejecting final completion");
        }
        string corrupt = Path.Combine(work, "invalid-package.bin");
        File.WriteAllBytes(corrupt, new byte[] { 1, 2, 3 });
        MustFail(delegate { FtpUploader.Upload(corrupt, "ftp://127.0.0.1/upgrade", "test", "fake"); }, "截断", "fake");
        Console.WriteLine("PASS loopback FTP: 226 success and byte match, auth failure, write refusal, final 451 failure and invalid package");
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new Exception("Usage: FtpTests.exe <V1 sample> <fresh directory>");
            string work = Path.GetFullPath(args[1]);
            if (Directory.Exists(work)) throw new Exception("Test directory already exists.");
            Directory.CreateDirectory(work);
            AddressTests();
            UploadTests(Path.GetFullPath(args[0]), work);
            Console.WriteLine("PASS FTP assertions=" + checks);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL FTP " + e.Message); return 1; }
    }
}
