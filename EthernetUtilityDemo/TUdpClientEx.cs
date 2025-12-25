using EthernetUtility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TEthernetUtility
{
    internal class TUdpClientEx
    {
        public static async Task TestTextMode()
        {
            // Create UDP client
            var client = new UdpClientEx()
            {
                PayloadMode = UdpPayloadMode.Text
            };

            // Subscribe to events
            client.TextSent += (ep, text) =>
                Console.WriteLine($"TX TEXT {ep} -> {text}");
            client.TextReceived += (ep, text) =>
                Console.WriteLine($"RX TEXT {ep} -> {text}");

            // Define server endpoint
            var serverEp = new IPEndPoint(IPAddress.Loopback, 10086);

            // -------------------- Text Query --------------------
            string idn = await client.QueryTextAsync("*IDN?", serverEp);

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }

        public static async Task TestBinaryMode()
        {
            // Create UDP client
            var client = new UdpClientEx()
            {
                PayloadMode = UdpPayloadMode.Binary
            };

            // Subscribe to events
            client.BinarySent += (ep, data) =>
                Console.WriteLine($"TX BIN  {ep} -> {BitConverter.ToString(data)}");
            client.BinaryReceived += (ep, data) =>
                Console.WriteLine($"RX BIN  {ep} -> {BitConverter.ToString(data)}");

            // Define server endpoint
            var serverEp = new IPEndPoint(IPAddress.Loopback, 10086);

            // -------------------- Binary Query --------------------
            byte[] frame = { 0xAA, 0x01 };
            byte[] response = await client.QueryBytesAsync(frame, serverEp);

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }

        public static async Task TestBroadCast()
        {
            // Create UDP client
            var client = new UdpClientEx()
            {
                PayloadMode = UdpPayloadMode.Binary
            };

            // Subscribe to events
            client.BinarySent += (ep, data) =>
                Console.WriteLine($"TX BIN  {ep} -> {BitConverter.ToString(data)}");
            client.BinaryReceived += (ep, data) =>
                Console.WriteLine($"RX BIN  {ep} -> {BitConverter.ToString(data)}");

            var broadcastEp = new IPEndPoint(IPAddress.Broadcast, 10086);

            // query binary broadcast
            byte[] broadcastFrame = { 0xAA, 0x01 };
            await client.QueryBroadcastBinaryAsync(broadcastFrame, broadcastEp);

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }

        public static async Task TestMultiCast()
        {
            // Create UDP client
            var client = new UdpClientEx()
            {
                PayloadMode = UdpPayloadMode.Binary
            };

            // Subscribe to events
            client.BinarySent += (ep, data) =>
                Console.WriteLine($"TX BIN  {ep} -> {BitConverter.ToString(data)}");
            client.BinaryReceived += (ep, data) =>
                Console.WriteLine($"RX BIN  {ep} -> {BitConverter.ToString(data)}");

            var multicastEp = new IPEndPoint(IPAddress.Parse("239.0.0.222"), 10086);

            // Query binary multicast
            byte[] multicastFrame = { 0xBB, 0x02 };
            await client.QueryMulticastBinaryAsync(multicastFrame, multicastEp);

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }
    }
}
