namespace TelnetSimulator;

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class TelnetServer
{
    private readonly int _port;
    private TcpListener _listener;
    private CancellationTokenSource _cts;

    public TelnetServer(int port = 23)
    {
        _port = port;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();

        Console.WriteLine($"[TelnetServer] Listening on port {_port}");

        Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        Console.WriteLine("[TelnetServer] Stopped");
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync();
            Console.WriteLine("[Client] Connected");

            _ = Task.Run(() => HandleClientAsync(client, token));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            var buffer = new byte[1024];

            await SendAsync(stream, "Telnet Simulator Ready\r\n");

            while (!token.IsCancellationRequested && client.Connected)
            {
                int len = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (len <= 0) break;

                string cmd = Encoding.ASCII.GetString(buffer, 0, len).Trim();
                Console.WriteLine($"[RX] {cmd}");

                string response = HandleCommand(cmd);

                if (!string.IsNullOrEmpty(response))
                {
                    await SendAsync(stream, response + "\r\n");
                    Console.WriteLine($"[TX] {response}");
                }
            }
        }

        Console.WriteLine("[Client] Disconnected");
    }

    private Task SendAsync(NetworkStream stream, string text)
    {
        byte[] data = Encoding.ASCII.GetBytes(text);
        return stream.WriteAsync(data, 0, data.Length);
    }

    // 👉 在这里写你的 SCPI 逻辑
    protected virtual string HandleCommand(string cmd)
    {
        switch (cmd.ToUpper())
        {
            case "*IDN?":
                return "RF,SCPI-SIM,MODEL-1,1.0";

            case "MEAS:POW?":
                return "10.23";

            case "SYST:ERR?":
                return "0,No error";

            default:
                return "ERROR:UNKNOWN COMMAND";
        }
    }
}
