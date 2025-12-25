using NetUtility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EthernetUtilityDemo
{
    /// <summary>
    /// FtpManager 测试 / 示例类
    /// 注意：需要你自己提供可访问的 FTP 服务器信息后再运行
    /// </summary>ftp://192.168.2.118:2122
    public static class TFtpManager
    {
        // 请根据实际情况修改这些测试参数
        private const string Host = "192.168.2.118";   // 可以是 IP 或域名
        private const int Port = 2122;
        private const string UserName = "SDManager";
        private const string Password = "SDM_2016";

        private const string RemoteTestDir = "/SPC";        // 测试用远程目录
        private const string RemoteFileName = "hello.txt";   // 测试用远程文件名

        /// <summary>
        /// 运行所有 FTP 测试
        /// </summary>
        public static async Task RunAllAsync()
        {
            Console.WriteLine("=== FTP 测试开始 ===\n");

            var ftp = new FtpClientEx(Host, UserName, Password, Port);

            // 1. 创建测试目录
            Console.WriteLine("1) 创建远程测试目录...");
            await ftp.CreateDirectoryAsync(RemoteTestDir);
            Console.WriteLine("   已尝试创建目录\n");

            // 2. 列出根目录/测试目录
            Console.WriteLine("2) 列出根目录：");
            var rootList = await ftp.ListAsync();
            foreach (var item in rootList)
                Console.WriteLine("   " + item);
            Console.WriteLine();

            Console.WriteLine($"3) 列出测试目录 {RemoteTestDir} ：");
            var testList = await ftp.ListAsync(RemoteTestDir);
            foreach (var item in testList)
                Console.WriteLine("   " + item);
            Console.WriteLine();

            // 3. 上传文件
            Console.WriteLine("4) 上传测试文件...");
            string tempDir = Path.Combine(Path.GetTempPath(), "FtpManagerTest");
            Directory.CreateDirectory(tempDir);
            string localFile = Path.Combine(tempDir, "hello.txt");
            await File.WriteAllTextAsync(localFile, "Hello FTP from C# at " + DateTime.Now);

            string remotePath = $"{RemoteTestDir}/{RemoteFileName}";
            await ftp.UploadFileAsync(localFile, remotePath);
            Console.WriteLine($"   已上传 {localFile} -> {remotePath}\n");

            // 4. 检查文件是否存在
            Console.WriteLine("5) 检查远程文件是否存在...");
            bool exists = await ftp.FileExistsAsync(remotePath);
            Console.WriteLine($"   远程文件存在: {exists}\n");

            // 5. 下载文件到本地另一个位置
            Console.WriteLine("6) 下载远程文件到本地...");
            string downloadPath = Path.Combine(tempDir, "downloaded.txt");
            await ftp.DownloadFileAsync(remotePath, downloadPath);
            Console.WriteLine($"   已下载到: {downloadPath}");
            Console.WriteLine("   文件内容：");
            Console.WriteLine(await File.ReadAllTextAsync(downloadPath));
            Console.WriteLine();

            // 6. 删除远程文件
            Console.WriteLine("7) 删除远程测试文件...");
            await ftp.DeleteFileAsync(remotePath);
            Console.WriteLine("   已删除远程文件\n");

            Console.WriteLine("=== FTP 测试结束 ===");
        }
    }
}
