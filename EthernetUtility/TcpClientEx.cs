using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EthernetUtility
{
    public class TcpClientEx : IAsyncDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _client;
        private NetworkStream? _stream;

        public TcpClientEx(string host, int port)
        {
            _host = host;
            _port = port;
        }

        // 模式（默认文本）
        public TcpPayloadMode PayloadMode { get; set; } = TcpPayloadMode.Text;

        // 事件：文本/二进制 收发
        public event Action<string>? TextReceived;
        public event Action<string>? TextSent;
        public event Action<byte[]>? BinaryReceived;
        public event Action<byte[]>? BinarySent;

        public async Task ConnectAsync()
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port);
            _stream = _client.GetStream();
        }

        // 文本发送（保留原有行为）
        public async Task SendAsync(string command)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");
            if (PayloadMode == TcpPayloadMode.Binary)
                throw new InvalidOperationException("Client is in Binary mode. Use SendAsync(byte[]) for binary data.");

            var data = Encoding.UTF8.GetBytes(command + "\n");
            await _stream.WriteAsync(data.AsMemory(0, data.Length));
            TextSent?.Invoke(command);
        }

        // 二进制发送
        public async Task SendAsync(byte[] data)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");
            if (PayloadMode == TcpPayloadMode.Text)
                throw new InvalidOperationException("Client is in Text mode. Use SendAsync(string) for text data.");

            await _stream.WriteAsync(data.AsMemory(0, data.Length));
            BinarySent?.Invoke(data);
        }

        // 文本查询：发送文本并读取直到换行或超时，返回单行（去除行尾）
        public async Task<string> QueryAsync(string command, int bufferSize = 8192, int timeoutMs = 1000)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");
            if (PayloadMode == TcpPayloadMode.Binary)
                throw new InvalidOperationException("Client is in Binary mode. Use QueryAsync(byte[]) for binary data.");

            await SendAsync(command);

            var buf = new byte[bufferSize];
            var sb = new StringBuilder();
            using var cts = new CancellationTokenSource(timeoutMs);

            try
            {
                while (true)
                {
                    int read = await _stream.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token);
                    if (read <= 0) break;

                    var chunk = Encoding.UTF8.GetString(buf, 0, read);
                    sb.Append(chunk);

                    var text = sb.ToString();
                    var nlIndex = text.IndexOf('\n');
                    if (nlIndex >= 0)
                    {
                        var line = text[..nlIndex].TrimEnd('\r');
                        TextReceived?.Invoke(line);
                        return line;
                    }
                    // 继续读取直到找到换行或超时
                }
            }
            catch (OperationCanceledException)
            {
                // 超时：如果已有内容则返回已有内容，否则抛出超时异常
                var collected = sb.ToString();
                if (!string.IsNullOrEmpty(collected))
                {
                    var line = collected.TrimEnd('\r', '\n');
                    TextReceived?.Invoke(line);
                    return line;
                }
                throw new TimeoutException("Read timeout");
            }

            // 连接关闭但有残余数据
            var remaining = sb.ToString().TrimEnd('\r', '\n');
            TextReceived?.Invoke(remaining);
            return remaining;
        }

        // 二进制查询：发送二进制并读取一次（可读到多少返回多少），超时则根据已读返回或抛出超时
        public async Task<byte[]> QueryAsync(byte[] data, int bufferSize = 8192, int timeoutMs = 1000)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");
            if (PayloadMode == TcpPayloadMode.Text)
                throw new InvalidOperationException("Client is in Text mode. Use QueryAsync(string) for text data.");

            await SendAsync(data);

            var buf = new byte[bufferSize];
            using var ms = new MemoryStream();
            using var cts = new CancellationTokenSource(timeoutMs);

            try
            {
                // 首次读取（会在超时或有数据时完成）
                int read = await _stream.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token);
                if (read > 0)
                {
                    await ms.WriteAsync(buf.AsMemory(0, read));
                    // 尝试短时间内读取更多数据（非阻塞式）；这里不做复杂的可用字节检查，简单再读取一次尝试性地收齐剩余
                    // 如果需要更复杂的行为，可在调用处循环读取直到某种协议边界。
                    try
                    {
                        // 小的额外窗口读取
                        using var shortCts = new CancellationTokenSource(50);
                        int additional = await _stream.ReadAsync(buf.AsMemory(0, buf.Length), shortCts.Token);
                        if (additional > 0)
                            await ms.WriteAsync(buf.AsMemory(0, additional));
                    }
                    catch (OperationCanceledException) { /* 忽略短时间窗口超时 */ }
                }
            }
            catch (OperationCanceledException)
            {
                if (ms.Length == 0) throw new TimeoutException("Read timeout");
                // 否则返回已收集的数据
            }

            var result = ms.ToArray();
            if (result.Length > 0) BinaryReceived?.Invoke(result);
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            try { if (_stream is not null) await _stream.DisposeAsync(); } catch { }
            try { _client?.Close(); } catch { }
            await Task.CompletedTask;
        }
    }

    public enum TcpPayloadMode
    {
        Text,
        Binary,
    }
}


