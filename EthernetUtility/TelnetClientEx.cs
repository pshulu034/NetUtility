using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EthernetUtility
{
    /// <summary>
    /// 简易 Telnet 客户端封装，支持：
    /// - ConnectAsync / Disconnect
    /// - Send / SendCommand（可等待期望提示）
    /// - ReadAvailable / ReadUntil（带超时）
    /// - 简单 IAC（Telnet 命令）过滤
    /// 设计目标：覆盖常见交互场景（登录、发送命令、读取响应），便于在控制台测试或自动化脚本中使用。
    /// </summary>
    public sealed class TelnetClientEx : IDisposable
    {
        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private readonly Encoding _encoding;
        private readonly int _bufferSize;
        private bool _disposed;

        public bool IsConnected => _tcp?.Connected == true;

        public TelnetClientEx(Encoding? encoding = null, int bufferSize = 4096)
        {
            _encoding = encoding ?? Encoding.ASCII;
            _bufferSize = bufferSize;
        }

        public async Task ConnectAsync(string host, int port = 23, int timeoutMilliseconds = 10000, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentNullException(nameof(host));
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));

            _tcp = new TcpClient();
            var connectTask = _tcp.ConnectAsync(host, port);
            var delay = Task.Delay(timeoutMilliseconds, cancellationToken);

            var completed = await Task.WhenAny(connectTask, delay).ConfigureAwait(false);
            if (completed != connectTask)
            {
                _tcp.Dispose();
                _tcp = null;
                throw new TimeoutException($"连接到 {host}:{port} 超时。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _stream = _tcp.GetStream();
            _stream.ReadTimeout = timeoutMilliseconds;
        }

        public void Disconnect()
        {
            _stream?.Dispose();
            _stream = null;
            _tcp?.Close();
            _tcp?.Dispose();
            _tcp = null;
        }

        public async Task SendAsync(string text, bool appendNewLine = true, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            if (appendNewLine) text += "\r\n";
            var bytes = _encoding.GetBytes(text);
            await _stream!.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> ReadAvailableAsync(int timeoutMilliseconds = 2000, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var sw = Stopwatch.StartNew();
            var result = new StringBuilder();
            var buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
            try
            {
                while (sw.ElapsedMilliseconds < timeoutMilliseconds)
                {
                    if (_stream!.DataAvailable)
                    {
                        int read = await _stream.ReadAsync(buffer, 0, _bufferSize, cancellationToken).ConfigureAwait(false);
                        if (read == 0) break;
                        result.Append(ProcessIac(buffer, read));
                        // 重置计时以继续读取连续流
                        sw.Restart();
                    }
                    else
                    {
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return result.ToString();
        }

        public async Task<string> ReadUntilAsync(string expect, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (expect is null) throw new ArgumentNullException(nameof(expect));
            return (await ReadUntilAnyAsync(new[] { expect }, timeout, cancellationToken).ConfigureAwait(false)).Text;
        }

        public async Task<(string Text, string Matched)> ReadUntilAnyAsync(string[] expects, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (expects == null || expects.Length == 0) throw new ArgumentNullException(nameof(expects));
            EnsureConnected();

            var sw = Stopwatch.StartNew();
            var buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
            var sb = new StringBuilder();
            try
            {
                while (sw.Elapsed < timeout)
                {
                    if (_stream!.DataAvailable)
                    {
                        int read = await _stream.ReadAsync(buffer, 0, _bufferSize, cancellationToken).ConfigureAwait(false);
                        if (read == 0) break;
                        sb.Append(ProcessIac(buffer, read));
                        var text = sb.ToString();
                        foreach (var e in expects)
                        {
                            if (!string.IsNullOrEmpty(e) && text.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return (text, e);
                            }
                        }
                        // 继续读取
                    }
                    else
                    {
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return (sb.ToString(), string.Empty);
        }

        /// <summary>
        /// 发送命令并等待期望提示（可用于登录或命令交互）。
        /// </summary>
        public async Task<string> SendCommandAsync(string command, string[] expectPrompts, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            await SendAsync(command, true, cancellationToken).ConfigureAwait(false);
            var (text, matched) = await ReadUntilAnyAsync(expectPrompts, timeout, cancellationToken).ConfigureAwait(false);
            return text;
        }

        /// <summary>
        /// 简单登录帮助：按用户名/密码提示发送凭证（提示可自定义）。
        /// 返回登录后接收到的文本。
        /// </summary>
        public async Task<string> LoginAsync(string username, string password, string userPrompt = "login", string passwordPrompt = "password", TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (username is null) throw new ArgumentNullException(nameof(username));
            if (password is null) throw new ArgumentNullException(nameof(password));
            EnsureConnected();

            var t = timeout ?? TimeSpan.FromSeconds(10);
            // 先等待用户名或密码提示，如果出现用户名提示则发送用户名；否则如果直接出现密码提示发送密码。
            var (text, matched) = await ReadUntilAnyAsync(new[] { userPrompt, passwordPrompt }, t, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(matched) && matched.IndexOf(userPrompt, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                await SendAsync(username, true, cancellationToken).ConfigureAwait(false);
                (text, matched) = await ReadUntilAnyAsync(new[] { passwordPrompt }, t, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(matched) && matched.IndexOf(passwordPrompt, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                await SendAsync(password, true, cancellationToken).ConfigureAwait(false);
                // 等待登录结果或 shell 提示
                var post = await ReadAvailableAsync((int)t.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
                return text + post;
            }

            return text;
        }

        private string ProcessIac(byte[] buffer, int length)
        {
            // 简单过滤 IAC 序列（255 开头）。遇到双 255 表示字节 255 转义。
            var outBytes = new List<byte>(length);
            for (int i = 0; i < length; i++)
            {
                byte b = buffer[i];
                if (b == 255) // IAC
                {
                    if (i + 1 < length)
                    {
                        byte next = buffer[i + 1];
                        if (next == 255)
                        {
                            outBytes.Add(255);
                            i++; // 跳过第二个 255
                            continue;
                        }
                        // 常见结构 IAC CMD OPT -> 跳过 CMD 和可选的 OPT（若存在则跳两位）
                        // 为简单起见：跳过下 2 字节（如果存在）
                        if (i + 2 < length) i += 2;
                        else i++;
                        continue;
                    }
                }
                else
                {
                    outBytes.Add(b);
                }
            }
            return _encoding.GetString(outBytes.ToArray());
        }

        private void EnsureConnected()
        {
            if (!IsConnected || _stream == null) throw new InvalidOperationException("Telnet 未连接。请先调用 ConnectAsync。");
        }

        public void Dispose()
        {
            if (_disposed) return;
            Disconnect();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}