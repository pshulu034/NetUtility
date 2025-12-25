using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpSimulator.Server;

public class TcpServer
{
    private readonly string _host;
    private readonly int _port;
    private TcpListener? _listener;
    private volatile bool _running;

    // 新增：工作模式（Text / Binary / Mixed）
    public TcpPayloadMode PayloadMode { get; set; } = TcpPayloadMode.Binary;

    // 事件：文本/二进制 收发
    public event Action<IPEndPoint, string>? TextReceived;
    public event Action<IPEndPoint, string>? TextSent;
    public event Action<IPEndPoint, byte[]>? BinaryReceived;
    public event Action<IPEndPoint, byte[]>? BinarySent;

    public TcpServer(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _listener = new TcpListener(IPAddress.Parse(_host), _port);
        _listener.Start();
        _running = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        while (client.Connected && !cancellationToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            }
            catch
            {
                break;
            }
            if (read <= 0) break;

            var remoteEp = client.Client.RemoteEndPoint as IPEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0);

            // 根据模式处理
            if (PayloadMode == TcpPayloadMode.Binary)
            {
                var data = new byte[read];
                Array.Copy(buffer, 0, data, 0, read);
                BinaryReceived?.Invoke(remoteEp, data);

                var resp = BinaryRequestProcessor.Process(data);
                if (resp != null && resp.Length > 0)
                {
                    await stream.WriteAsync(resp.AsMemory(0, resp.Length), cancellationToken);
                    BinarySent?.Invoke(remoteEp, resp);
                }
            }
            else   //(PayloadMode == TcpPayloadMode.Text)
            {
                // 文本模式：按行处理
                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                var text = sb.ToString();
                var lines = new List<string>();
                int index;
                while ((index = text.IndexOf('\n')) >= 0)
                {
                    var line = text[..index].TrimEnd('\r');
                    lines.Add(line);
                    text = text[(index + 1)..];
                }
                sb.Clear();
                sb.Append(text);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    TextReceived?.Invoke(remoteEp, line);
                    var resp = TextRequestProcessor.Process(line);
                    if (!string.IsNullOrEmpty(resp))
                    {
                        var bytes = Encoding.UTF8.GetBytes(resp + "\n");
                        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
                        TextSent?.Invoke(remoteEp, resp);
                    }
                }
            }
        }
        client.Close();
    }
}

public enum TcpPayloadMode
{
    Text,
    Binary,
}