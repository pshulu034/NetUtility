using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace UdpSimulator.Server
{
    public class UdpServerEx : IDisposable
    {
        private readonly int _port;
        private readonly IPAddress _multicast;
        private UdpClient _udp;
        private CancellationTokenSource _cts;

        public bool IsRunning { get; private set; }

        public UdpPayloadMode PayloadMode { get; set; } = UdpPayloadMode.Binary;

        /// <summary>
        /// 文本发送事件
        /// </summary>
        public event Action<IPEndPoint, string> TextSent;

        /// <summary>
        /// 二进制发送事件
        /// </summary>
        public event Action<IPEndPoint, byte[]> BinarySent;

        /// <summary>
        /// 文本接收事件（SCPI / ASCII）
        /// </summary>
        public event Action<IPEndPoint, string> TextReceived;

        /// <summary>
        /// 原始二进制接收事件
        /// </summary>
        public event Action<IPEndPoint, byte[]> BinaryReceived;

        public UdpServerEx(int port, IPAddress multicast = null)
        {
            _port = port;
            _multicast = multicast;
        }

        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _udp = new UdpClient(_port)
            {
                EnableBroadcast = true
            };

            if (_multicast != null)
            {
                _udp.JoinMulticastGroup(_multicast);
            }

            IsRunning = true;
            Task.Run(() => ReceiveLoop(_cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _cts.Cancel();
            _udp.Close();
            _udp.Dispose();

            IsRunning = false;
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var result = await _udp.ReceiveAsync();

                    switch (PayloadMode)
                    {
                        case UdpPayloadMode.Binary:
                            HandleBinary(result.RemoteEndPoint, result.Buffer);
                            break;

                        case UdpPayloadMode.Text:
                            HandleText(result.RemoteEndPoint, result.Buffer);
                            break;

                        default:
                            break;
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public void SendString(string text, IPEndPoint remote)
        {
            var buf = Encoding.ASCII.GetBytes(text + "\n");
            _udp.Send(buf, buf.Length, remote);

            // 触发事件
            TextSent?.Invoke(remote, text);
        }

        public void SendBytes(byte[] data, IPEndPoint remote)
        {
            _udp.Send(data, data.Length, remote);

            // 触发事件
            BinarySent?.Invoke(remote, data);
        }

        private void HandleBinary(IPEndPoint remote, byte[] data)
        {
            // 1️⃣ 抛原始事件（日志 / UI）
            BinaryReceived?.Invoke(remote, data);

            // 2️⃣ 处理二进制请求
            var response = BinaryRequestProcessor.Process(data);

            // 3️⃣ 自动回包
            if (response != null && response.Length > 0)
            {
                SendBytes(response, remote);
            }
        }

        private void HandleText(IPEndPoint remote, byte[] data)
        {
            string text;
            try
            {
                text = Encoding.ASCII.GetString(data).Trim();
            }
            catch
            {
                return;
            }

            if (string.IsNullOrEmpty(text))
                return;

            // 文本接收事件
            TextReceived?.Invoke(remote, text);

            // 处理文本请求
            string resp = TextRequetProcessor.Process(text);

            // 响应请求
            if (!string.IsNullOrEmpty(resp))
            {
                SendString(resp, remote);
            }
        }
    }

    public enum UdpPayloadMode
    {
        Text,      // 只当文本处理
        Binary,    // 只当二进制处理
    }
}
