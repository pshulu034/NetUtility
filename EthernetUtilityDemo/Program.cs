// See https://aka.ms/new-console-template for more information
using EthernetUtilityDemo;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TEthernetUtility;
using System;
using System.Threading.Tasks;
using EthernetUtility;

namespace EthernetUtilityDemo
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            // demo: query local SNMP simulator (adjust host/port as needed)
            var host = "127.0.0.1";
            var port = 161; // default SNMP port; may require elevated privileges if simulator binds to 161
            if (args.Length >= 1) host = args[0];
            if (args.Length >= 2 && int.TryParse(args[1], out var p)) port = p;

            using var client = new SnmpClientEx(host, port, community: "public", timeoutMs: 3000);

            #region GetParam
            
            Console.WriteLine($"Querying {host}:{port} ...");
            // common SNMP examples
            var oid1 = "1.3.6.1.2.1.1.1.0"; // sysDescr.0
            var oid2 = "1.3.6.1.2.1.1.5.0"; // sysName.0

            var r1 = await client.GetAsync(oid1);
            Console.WriteLine($"{oid1} => {(r1 != null ? r1.ToString() : "<no response>")}");

            var r2 = await client.GetAsync(oid2);
            Console.WriteLine($"{oid2} => {(r2 != null ? r2.ToString() : "<no response>")}");

            // query multiple OIDs
            var multi = await client.GetAsync(new[] { oid1, oid2 });
            foreach (var kv in multi)
            {
                Console.WriteLine($"{kv.Key} => {kv.Value}");
            }
            #endregion

            #region SetParam
            // 直接设置指定 OID 为 "abc"
            var sr1 = await client.SetAsync(oid1, "abc");
            Console.WriteLine($"{oid1} <= {(sr1 != null ? sr1.ToString() : "<failed>")}");

            var sr2 = await client.SetAsync(oid2, "abc");
            Console.WriteLine($"{oid1} <= {(sr2 != null ? sr2.ToString() : "<failed>")}");

            IDictionary<string, string> dict = new  Dictionary<string, string>()
            {
                { oid1, "def1" },
                { oid2, "def2" }
            };
            var smulti = await client.SetAsync(dict);

            //回读
            multi = await client.GetAsync(new[] { oid1, oid2 });
            foreach (var kv in multi)
            {
                Console.WriteLine($"{kv.Key} => {kv.Value}");
            }
            #endregion



            Console.WriteLine("Demo complete. Press Enter to exit.");
            Console.ReadLine();
        }
    }
}

