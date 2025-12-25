using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TcpSimulator.Server;

namespace TcpSimulator;

internal class Program
{
    static async Task Main(string[] args)
    {
        var host = "0.0.0.0";
        var port = 6025;
        var server = new TcpServer(host, port) { PayloadMode = TcpPayloadMode.Binary };

        // 订阅收发事件（文本/二进制）
        server.TextReceived += (remote, text) =>
        {
            Console.WriteLine($"[Text Received] {remote} => {text}");
        };

        server.TextSent += (remote, text) =>
        {
            Console.WriteLine($"[Text Sent] {remote} <= {text}");
        };

        server.BinaryReceived += (remote, data) =>
        {
            Console.WriteLine($"[Binary Received] {remote} => {data.Length} bytes : {BitConverter.ToString(data)}");
        };

        server.BinarySent += (remote, data) =>
        {
            Console.WriteLine($"[Binary Sent] {remote} <= {data.Length} bytes : {BitConverter.ToString(data)}");
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            Console.WriteLine("停止中...");
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"启动服务器 {host}:{port} (Mode={server.PayloadMode}) - 按 Ctrl+C 停止");
        await server.StartAsync(cts.Token);
        Console.WriteLine("服务器已停止。");
    }
}
