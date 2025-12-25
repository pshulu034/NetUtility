using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NetUtility
{
    /// <summary>
    /// FTP 常用功能封装：
    /// - 列目录
    /// - 上传 / 下载 文件
    /// - 删除文件
    /// - 创建目录
    /// - 简单存在性检查
    /// 基于 System.Net.FtpWebRequest 实现
    /// </summary>
    public class FtpClientEx
    {
        public string Host { get; }
        public int Port { get; }
        public string UserName { get; }
        public string Password { get; }

        /// <summary>
        /// 是否使用系统默认代理（WebRequest.DefaultWebProxy），默认 true。
        /// 为 false 时，将显式禁用代理（request.Proxy = null）。
        /// </summary>
        public bool UseDefaultProxy { get; set; } = true;

        /// <summary>
        /// 是否使用被动模式（默认 true，一般网络环境推荐）
        /// </summary>
        public bool UsePassive { get; set; } = true;

        /// <summary>
        /// 是否使用二进制传输（默认 true）
        /// </summary>
        public bool UseBinary { get; set; } = true;

        /// <summary>
        /// 是否启用 SSL（FTPS），默认为 false
        /// </summary>
        public bool EnableSsl { get; set; } = false;

        public FtpClientEx(string host, string userName, string password, int port = 21, bool useDefaultProxy = true)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("FTP 主机不能为空", nameof(host));

            Host = host.TrimEnd('/');
            UserName = userName;
            Password = password;
            Port = port;
            UseDefaultProxy = useDefaultProxy;            
        }

        private FtpWebRequest CreateRequest(string path, string method)
        {
            var uriBuilder = new StringBuilder();
            if (!Host.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) &&
                !Host.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
            {
                uriBuilder.Append("ftp://");
            }

            uriBuilder.Append(Host.TrimEnd('/'));

            if (Port != 21)
            {
                uriBuilder.Append($":{Port}");
            }

            if (!string.IsNullOrEmpty(path))
            {
                
                if (!path.StartsWith("/"))
                {
                    uriBuilder.Append("/").Append(path);
                }
                else
                {
                    uriBuilder.Append(path);
                }
            }

            var uri = new Uri(uriBuilder.ToString());

            var request = (FtpWebRequest)WebRequest.Create(uri);
            request.Method = method;
            request.Credentials = new NetworkCredential(UserName, Password);
            request.UsePassive = UsePassive;
            request.UseBinary = UseBinary;
            request.EnableSsl = EnableSsl;
            request.KeepAlive = false; // 每次操作独立连接
            request.Proxy = UseDefaultProxy ? WebRequest.DefaultWebProxy : null;

            //可以指定代理
            //request.Proxy = new WebProxy(uri);

            return request;
        }

        /// <summary>
        /// 获取 FTP 目录的简单列表（文件/目录名）
        /// </summary>
        public async Task<IReadOnlyList<string>> ListAsync(string remotePath = "")
        {
            var request = CreateRequest(remotePath, WebRequestMethods.Ftp.ListDirectory);

            using var response = (FtpWebResponse)await request.GetResponseAsync();
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream!, Encoding.UTF8);

            var list = new List<string>();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                    list.Add(line);
            }

            return list;
        }

        /// <summary>
        /// 上传本地文件到 FTP
        /// </summary>
        public async Task UploadFileAsync(string localFilePath, string remotePath)
        {
            if (!File.Exists(localFilePath))
                throw new FileNotFoundException("本地文件不存在", localFilePath);

            var request = CreateRequest(remotePath, WebRequestMethods.Ftp.UploadFile);

            using var fileStream = File.OpenRead(localFilePath);
            using var requestStream = await request.GetRequestStreamAsync();
            await fileStream.CopyToAsync(requestStream);

            using var response = (FtpWebResponse)await request.GetResponseAsync();
            // 可以根据 response.StatusDescription 做日志
        }

        /// <summary>
        /// 从 FTP 下载文件到本地
        /// </summary>
        public async Task DownloadFileAsync(string remotePath, string localFilePath)
        {
            var request = CreateRequest(remotePath, WebRequestMethods.Ftp.DownloadFile);

            using var response = (FtpWebResponse)await request.GetResponseAsync();
            using var responseStream = response.GetResponseStream();
            Directory.CreateDirectory(Path.GetDirectoryName(localFilePath)!);
            using var fileStream = File.Create(localFilePath);

            await responseStream!.CopyToAsync(fileStream);
        }

        /// <summary>
        /// 删除 FTP 上的文件
        /// </summary>
        public async Task DeleteFileAsync(string remotePath)
        {
            var request = CreateRequest(remotePath, WebRequestMethods.Ftp.DeleteFile);
            using var response = (FtpWebResponse)await request.GetResponseAsync();
        }

        /// <summary>
        /// 创建 FTP 目录（已存在时静默成功）
        /// </summary>
        public async Task CreateDirectoryAsync(string remotePath)
        {
            var request = CreateRequest(remotePath, WebRequestMethods.Ftp.MakeDirectory);

            try
            {
                using var response = (FtpWebResponse)await request.GetResponseAsync();
            }
            catch (WebException ex)
            {
                // 目录已存在等情况下，一般可以忽略 550 错误
                if (ex.Response is FtpWebResponse ftpResp &&
                    ftpResp.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                {
                    return;
                }

                throw;
            }
        }

        /// <summary>
        /// 判断 FTP 文件是否存在（通过尝试获取文件大小）
        /// </summary>
        public async Task<bool> FileExistsAsync(string remotePath)
        {
            var request = CreateRequest(remotePath, WebRequestMethods.Ftp.GetFileSize);
            try
            {
                using var response = (FtpWebResponse)await request.GetResponseAsync();
                return true;
            }
            catch (WebException ex)
            {
                if (ex.Response is FtpWebResponse ftpResp &&
                    ftpResp.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                {
                    return false;
                }

                throw;
            }
        }
    }
}
