using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace NetUtility
{
    /// <summary>
    /// ICMP 协议常用功能封装：Ping、连通性检查、简单路由跟踪（Traceroute 风格）
    /// 基于 System.Net.NetworkInformation.Ping 实现
    /// </summary>
    public class IcmpManager
    {
        /// <summary>
        /// Ping 结果模型
        /// </summary>
        public class PingResult
        {
            public string Host { get; set; } = string.Empty;
            public IPStatus Status { get; set; }
            public long RoundtripTimeMs { get; set; }
            public string? Address { get; set; }
            public int Ttl { get; set; }
            public int BufferSize { get; set; }
            public bool Success => Status == IPStatus.Success;

            public override string ToString()
            {
                if (!Success)
                    return $"{Host} - {Status}";

                return $"{Host} [{Address}] time={RoundtripTimeMs}ms TTL={Ttl} bytes={BufferSize}";
            }
        }

        /// <summary>
        /// 连续多次 Ping 的统计结果
        /// </summary>
        public class PingStatistics
        {
            public string Host { get; set; } = string.Empty;
            public int Sent { get; set; }
            public int Received { get; set; }
            public int Lost => Sent - Received;
            public double LossPercent => Sent == 0 ? 0 : (Lost * 100.0 / Sent);
            public long MinMs { get; set; } = long.MaxValue;
            public long MaxMs { get; set; } = 0;
            public double AvgMs { get; set; }
        }

        /// <summary>
        /// 简单 Ping 一次
        /// </summary>
        public async Task<PingResult> PingOnceAsync(
            string host,
            int timeoutMs = 4000,
            int ttl = 64,
            int bufferSize = 32,
            bool dontFragment = true,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host 不能为空", nameof(host));

            byte[] buffer = new byte[bufferSize];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = 0x61; // 'a'

            var options = new PingOptions(ttl, dontFragment);

            using var ping = new Ping();

            PingReply reply = await ping.SendPingAsync(
                host,
                timeoutMs,
                buffer,
                options);

            return new PingResult
            {
                Host = host,
                Status = reply.Status,
                RoundtripTimeMs = reply.RoundtripTime,
                Address = reply.Address?.ToString(),
                Ttl = reply.Options?.Ttl ?? ttl,
                BufferSize = bufferSize
            };
        }

        /// <summary>
        /// 连续 Ping 多次并统计延时/丢包（类似 ping 命令）
        /// </summary>
        public async Task<(List<PingResult> Results, PingStatistics Stats)> PingManyAsync(
            string host,
            int count = 4,
            int intervalMs = 1000,
            int timeoutMs = 4000,
            CancellationToken cancellationToken = default)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            var results = new List<PingResult>(count);
            var stats = new PingStatistics { Host = host, Sent = count };

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var r = await PingOnceAsync(host, timeoutMs, cancellationToken: cancellationToken);
                results.Add(r);

                if (r.Success)
                {
                    stats.Received++;
                    stats.MinMs = Math.Min(stats.MinMs, r.RoundtripTimeMs);
                    stats.MaxMs = Math.Max(stats.MaxMs, r.RoundtripTimeMs);
                    stats.AvgMs += r.RoundtripTimeMs;
                }

                if (i < count - 1)
                {
                    try
                    {
                        await Task.Delay(intervalMs, cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }

            if (stats.Received > 0)
                stats.AvgMs /= stats.Received;
            else
                stats.MinMs = 0;

            return (results, stats);
        }

        /// <summary>
        /// 简单双向路由跟踪（Traceroute 风格）：
        /// 通过逐步增加 TTL 的 Ping 来发现每一跳的地址
        /// </summary>
        public async Task<List<PingResult>> TraceRouteAsync(
            string host,
            int maxHops = 30,
            int timeoutMs = 4000,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host 不能为空", nameof(host));

            var results = new List<PingResult>();

            using var ping = new Ping();
            byte[] buffer = new byte[32];

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var options = new PingOptions(ttl, true);

                PingReply reply = await ping.SendPingAsync(
                    host,
                    timeoutMs,
                    buffer,
                    options);

                var r = new PingResult
                {
                    Host = host,
                    Status = reply.Status,
                    RoundtripTimeMs = reply.RoundtripTime,
                    Address = reply.Address?.ToString(),
                    Ttl = ttl,
                    BufferSize = buffer.Length
                };

                results.Add(r);

                // 到达目标或显式成功时结束
                if (reply.Status == IPStatus.Success)
                    break;
            }

            return results;
        }
    }
}
