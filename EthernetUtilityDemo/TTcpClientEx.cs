using EthernetUtility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TEthernetUtility
{
    public class TTcpClientEx
    {
        public static async Task TestTextMode()
        {
            var host = "127.0.0.1";
            var port = 6025;
            await using var client = new TcpClientEx(host, port)
            {
                PayloadMode = TcpPayloadMode.Text
            };

            // 订阅事件
            client.TextSent += text => Console.WriteLine($"TX TEXT -> {text}");
            client.TextReceived += text => Console.WriteLine($"RX TEXT -> {text}");

            await client.ConnectAsync();

            Console.WriteLine("发送文本查询 *IDN?");
            string idn = await client.QueryAsync("*IDN?");

            Console.WriteLine("发送文本查询 *OPC?");
            string opc = await client.QueryAsync("*OPC?");

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }

        public static async Task TestBinaryMode()
        {
            var host = "127.0.0.1";
            var port = 6025;
            await using var client = new TcpClientEx(host, port)
            {
                PayloadMode = TcpPayloadMode.Binary
            };

            // 订阅事件
            client.BinarySent += data => Console.WriteLine($"TX BIN  -> {BitConverter.ToString(data)}");
            client.BinaryReceived += data => Console.WriteLine($"RX BIN  -> {BitConverter.ToString(data)}");

            await client.ConnectAsync();

            // 示例二进制帧
            byte[] frame = { 0xAA, 0x01, 0x02 };
            Console.WriteLine($"发送二进制查询 {BitConverter.ToString(frame)}");
            byte[] response = await client.QueryAsync(frame);

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }
    }
}
