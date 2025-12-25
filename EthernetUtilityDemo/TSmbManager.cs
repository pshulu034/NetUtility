using NetUtility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EthernetUtilityDemo
{
    class TSmbManager
    {
        public async static void Test()
        {
            string sharePath = @"\\192.168.2.118\sw";      // 共享路径
            string user = "SW_W";              // 或 "User"
            string password = "SW_987654";
            string filePath = Path.Combine(sharePath, "test.txt");

            #region 手动连接/断开
            var mgr = new SmbClientEx();
            mgr.Disconnect(@"\\SERVER\Share", force: false);
            var session = mgr.Connect(sharePath, user, password);

            // ... 执行文件操作 ...

            // 读文件
            if (File.Exists(filePath))
            {
                string content = File.ReadAllText(filePath);
                Console.WriteLine(content);
            }

            // 写文件
            File.WriteAllText(Path.Combine(sharePath, "new.txt"), "Hello from C#");

            session.Disconnect();   // 或 session.Dispose();
            #endregion

            #region 自动断开连接

            using (SmbClientEx.Use(sharePath, user, password))
            {
                // 读文件
                if (File.Exists(filePath))
                {
                    string content = File.ReadAllText(filePath);
                    Console.WriteLine(content);
                }

                // 写文件
                File.WriteAllText(Path.Combine(sharePath, "new.txt"), "Hello from C#");
            } // 离开 using 块时自动断开网络共享会话
            #endregion
        }
    }
}
