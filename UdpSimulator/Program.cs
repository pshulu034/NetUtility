using System.Net;
using System.Text;
using System.Threading.Tasks;
using UdpSimulator.Server;

namespace UdpSimulator;

internal class Program
{
    static async Task Main(string[] args)
    {
        // 组播地址（可选，不需要就传 null）
        var multicast = IPAddress.Parse("239.0.0.222");
        var server = new UdpServerEx(10086, multicast)
        {
            PayloadMode = UdpPayloadMode.Text                         // TextOnly / BinaryOnly
        };

        server.TextReceived += (ep, text) => Console.WriteLine($"RX TEXT {ep} -> {text}");
        server.BinaryReceived += (ep, data) => Console.WriteLine($"RX BIN  {ep} -> {BitConverter.ToString(data)}");
        server.TextSent += (ep, text) => Console.WriteLine($"TX TEXT {ep} -> {text}");
        server.BinarySent += (ep, data) => Console.WriteLine($"TX BIN  {ep} -> {BitConverter.ToString(data)}");

        server.Start();

        Console.WriteLine("UDP Server started on port 10086");
        Console.WriteLine("Press ENTER to stop...");
        Console.ReadLine();

        server.Stop();
    }
}
