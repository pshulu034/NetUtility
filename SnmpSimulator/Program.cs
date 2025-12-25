using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;

namespace SnmpSimulator
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            // 默认使用 SNMP 标准端口 161
            // 注意：在大多数系统上监听 161 需要管理员/root 权限
            int port = 161;
            if (args.Length > 0 && int.TryParse(args[0], out var p)) port = p;

            using var server = new SnmpServer(port);

            var oidStore = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            oidStore["1.3.6.1.2.1.1.1.0"] = "SNMP Simulator - sysDescr";
            oidStore["1.3.6.1.2.1.1.5.0"] = Dns.GetHostName();

            server.OidLookup = oid =>
            {
                return oidStore.TryGetValue(oid, out var v) ? v : null;
            };

            server.OidSet = (oid, value) =>
            {
                oidStore[oid] = value ?? string.Empty;
                return true;
            };

            server.TextReceived += (ep, text) => Console.WriteLine($"[TELNET RX] {ep} => {text}");
            server.TextSent += (ep, text) => Console.WriteLine($"[TELNET TX] {ep} <= {text}");

            try
            {
                server.Start();
                Console.WriteLine($"SnmpServer (telnet helper) listening on TCP port {port}.");
                Console.WriteLine("Tip: Listening on port 161 may require elevated privileges and may conflict with real SNMP service.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start server on port {port}: {ex.Message}");
                return;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                Console.WriteLine("Stopping...");
                e.Cancel = true;
                cts.Cancel();
            };

            cts.Token.WaitHandle.WaitOne();

            server.Stop();
            Console.WriteLine("Stopped.");
        }
    }
}
