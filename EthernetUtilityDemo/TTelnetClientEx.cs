using System;
using System.Threading.Tasks;
using NetUtility;

namespace EthernetUtilityDemo
{
    public static class TTelnetClientEx
    {
        /// <summary>
        /// 测试示例：
        /// </summary>
        public static async Task Test()
        {
            Console.WriteLine("[Telnet] Demo: connecting to 127.0.0.1:23 ... (will read a short stream)");
            var host = "127.0.0.1";
            var port = 23;

            using var tel = new TelnetClientEx();
            try
            {
                await tel.ConnectAsync(host, port, timeoutMilliseconds: 10000);
                Console.WriteLine("[Telnet] Connected. Reading for 5 seconds...");
                var text = await tel.ReadAvailableAsync(5000);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("[Telnet] No data received.");
                }
                else
                {
                    Console.WriteLine("[Telnet] Preview:");
                    Console.WriteLine(text.Length > 2000 ? text.Substring(0, 2000) + "..." : text);
                }

                Console.WriteLine("\n[Telnet] Interactive demo: you can type a line to send (empty to skip).");

               while(true)
                {
                    Console.Write("Send> ");
                    var line = Console.ReadLine();

                    if (string.IsNullOrEmpty(line))
                    {
                        break;
                    }

                    await tel.SendAsync(line);
                    var reply = await tel.ReadAvailableAsync(2000);
                    Console.WriteLine("[Telnet] Reply:");
                    Console.WriteLine(reply);
                }

                // 示例：如果你要登录某个 telnet 设备，可以用 LoginAsync：
                // var loginResult = await tel.LoginAsync("username", "password");
                // Console.WriteLine(loginResult);

                Console.WriteLine("[Telnet] Demo finished. Disconnecting.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Telnet] 测试失败: {ex.Message}");
            }
            finally
            {
                tel.Dispose();
            }

            Console.WriteLine("\n------------------------------------------------------------");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }
}