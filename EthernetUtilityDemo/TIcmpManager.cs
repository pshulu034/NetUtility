using EthernetUtility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.NetworkInformation;

namespace EthernetUtilityDemo
{
    /// <summary>
    /// IcmpManager 使用示例
    /// </summary>
    public static class TIcmpManager
    {
        /// <summary>
        /// 示例1：Ping 一次
        /// </summary>
        public static async Task PingOnceExample()
        {
            var icmp = new IcmpManager();

            Console.WriteLine("=== ICMP 示例1：Ping 一次 ===");

            var host = "8.8.8.8";
            var r = await icmp.PingOnceAsync(host);

            if (r.Status == IPStatus.Success)
            {
                Console.WriteLine("Sucess");
            }

            Console.WriteLine(r.ToString());
            Console.WriteLine();
        }

        /// <summary>
        /// 示例2：连续多次 Ping 并统计
        /// </summary>
        public static async Task PingManyExample()
        {
            var icmp = new IcmpManager();
            var host = "8.8.8.8";

            Console.WriteLine("=== ICMP 示例2：连续 Ping 并统计 ===");
            Console.WriteLine($"目标: {host}\n");

            var (results, stats) = await icmp.PingManyAsync(host, count: 4, intervalMs: 1000);

            foreach (var r in results)
            {
                Console.WriteLine(r.ToString());
            }

            Console.WriteLine();
            Console.WriteLine($"统计: 发送={stats.Sent}, 接收={stats.Received}, 丢包={stats.Lost} ({stats.LossPercent:F1}%), " +
                              $"最小={stats.MinMs}ms 最大={stats.MaxMs}ms 平均={stats.AvgMs:F1}ms");
            Console.WriteLine();
        }

        /// <summary>
        /// 示例3：简单路由跟踪（Traceroute 风格）
        /// </summary>
        public static async Task TraceRouteExample()
        {
            var icmp = new IcmpManager();
            var host = "8.8.8.8";

            Console.WriteLine("=== ICMP 示例3：路由跟踪 (Traceroute) ===");
            Console.WriteLine($"目标: {host}\n");

            var hops = await icmp.TraceRouteAsync(host, maxHops: 20);

            int i = 1;
            foreach (var h in hops)
            {
                Console.WriteLine($"{i,2}  {h.Address,-20}  {h.Status,-15}  {h.RoundtripTimeMs,4}ms");
                i++;
            }

            Console.WriteLine();
        }

        /// <summary>
        /// 运行所有 ICMP 示例
        /// </summary>
        public static async Task RunAllAsync()
        {
            await PingOnceExample();
            await PingManyExample();
            await TraceRouteExample();
        }
    }
}
