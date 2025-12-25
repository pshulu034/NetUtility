using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace EthernetUtility
{

    public class UdpClientEx : IDisposable
    {
        private readonly UdpClient _client;

        /// <summary>
        /// 客户端工作模式
        /// </summary>
        public UdpPayloadMode PayloadMode { get; set; } = UdpPayloadMode.Binary;

        /// <summary>
        /// 文本发送 / 接收事件
        /// </summary>
        public event Action<IPEndPoint, string> TextSent;
        public event Action<IPEndPoint, string> TextReceived;

        /// <summary>
        /// 二进制发送 / 接收事件
        /// </summary>
        public event Action<IPEndPoint, byte[]> BinarySent;
        public event Action<IPEndPoint, byte[]> BinaryReceived;

        public UdpClientEx(int localPort = 0)
        {
            _client = new UdpClient(localPort)
            {
                EnableBroadcast = true
            };
        }

        public void JoinMulticast(IPAddress group)
        {
            _client.JoinMulticastGroup(group);
        }

        // ===================== 文本方法 =====================
        public async Task SendTextAsync(string text, IPEndPoint remote)
        {
            if (PayloadMode == UdpPayloadMode.Binary)
                throw new InvalidOperationException("Client is in BinaryOnly mode");

            var buf = Encoding.ASCII.GetBytes(text);
            await _client.SendAsync(buf, buf.Length, remote);

            TextSent?.Invoke(remote, text);
        }

        public async Task<string> ReceiveTextAsync(int timeoutMs = 2000)
        {
            if (PayloadMode == UdpPayloadMode.Binary)
                throw new InvalidOperationException("Client is in BinaryOnly mode");

            var buf = await ReceiveBytesAsync(timeoutMs);
            string text = Encoding.ASCII.GetString(buf).Trim();

            return text;
        }

        public async Task<string> QueryTextAsync(string text, IPEndPoint remote, int timeoutMs = 2000)
        {
            await SendTextAsync(text, remote);
            return await ReceiveTextAsync(timeoutMs);
        }

        // ===================== 二进制方法 =====================

        public async Task SendBytesAsync(byte[] data, IPEndPoint remote)
        {
            if (PayloadMode == UdpPayloadMode.Text)
                throw new InvalidOperationException("Client is in TextOnly mode");

            await _client.SendAsync(data, data.Length, remote);
            BinarySent?.Invoke(remote, data);
        }

        public async Task<byte[]> ReceiveBytesAsync(int timeoutMs = 2000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var task = _client.ReceiveAsync();

            var result = await task.WaitAsync(cts.Token);

            if (PayloadMode != UdpPayloadMode.Text)
                BinaryReceived?.Invoke(result.RemoteEndPoint, result.Buffer);

            if (PayloadMode != UdpPayloadMode.Binary)
            {
                try
                {
                    string text = Encoding.ASCII.GetString(result.Buffer).Trim();
                    TextReceived?.Invoke(result.RemoteEndPoint, text);
                }
                catch { }
            }

            return result.Buffer;
        }

        public async Task<byte[]> QueryBytesAsync(byte[] data, IPEndPoint remote, int timeoutMs = 2000)
        {
            await SendBytesAsync(data, remote);
            return await ReceiveBytesAsync(timeoutMs);
        }

        #region 广播和组播
        /// <summary>
        /// Join a multicast group to receive multicast packets.
        /// </summary>
        public void JoinMulticastGroup(IPAddress multicastAddress)
        {
            _client.JoinMulticastGroup(multicastAddress);
        }

        /// <summary>
        /// Leave a multicast group.
        /// </summary>
        public void LeaveMulticastGroup(IPAddress multicastAddress)
        {
            _client.DropMulticastGroup(multicastAddress);
        }

        /// <summary>
        /// Send a broadcast binary packet using an IPEndPoint.
        /// </summary>
        public async Task SendBroadcastBinaryAsync(byte[] data, IPEndPoint broadcastEp)
        {
            if (broadcastEp.Address.Equals(IPAddress.Broadcast) == false)
                throw new ArgumentException("IPEndPoint must have broadcast address 255.255.255.255", nameof(broadcastEp));

            await SendBytesAsync(data, broadcastEp);
        }

        /// <summary>
        /// Send a broadcast text message using an IPEndPoint.
        /// </summary>
        public async Task SendBroadcastTextAsync(string text, IPEndPoint broadcastEp)
        {
            if (broadcastEp.Address.Equals(IPAddress.Broadcast) == false)
                throw new ArgumentException("IPEndPoint must have broadcast address 255.255.255.255", nameof(broadcastEp));

            await SendTextAsync(text, broadcastEp);
        }

        /// <summary>
        /// Send a multicast binary packet using an IPEndPoint.
        /// </summary>
        public async Task SendMulticastBinaryAsync(byte[] data, IPEndPoint multicastEp)
        {
            if (!multicastEp.Address.IsMulticast())
                throw new ArgumentException("IPEndPoint must be a valid multicast address", nameof(multicastEp));

            await SendBytesAsync(data, multicastEp);
        }

        /// <summary>
        /// Send a multicast text message using an IPEndPoint.
        /// </summary>
        public async Task SendMulticastTextAsync(string text, IPEndPoint multicastEp)
        {
            if (!multicastEp.Address.IsMulticast())
                throw new ArgumentException("IPEndPoint must be a valid multicast address", nameof(multicastEp));

            await SendTextAsync(text, multicastEp);
        }

        /// <summary>
        /// Receive a broadcast binary packet (optional helper, wraps ReceiveBytesAsync).
        /// </summary>
        public async Task<byte[]> ReceiveBroadcastBinaryAsync(int timeoutMs = 2000)
        {
            return await ReceiveBytesAsync(timeoutMs);
        }

        /// <summary>
        /// Receive a multicast binary packet (optional helper, wraps ReceiveBytesAsync).
        /// </summary>
        public async Task<byte[]> ReceiveMulticastBinaryAsync(int timeoutMs = 2000)
        {
            return await ReceiveBytesAsync(timeoutMs);
        }

        /// <summary>
        /// Send a broadcast binary packet and wait for a response.
        /// </summary>
        public async Task<byte[]> QueryBroadcastBinaryAsync(byte[] data, IPEndPoint broadcastEp, int timeoutMs = 2000)
        {
            await SendBroadcastBinaryAsync(data, broadcastEp);
            return await ReceiveBroadcastBinaryAsync(timeoutMs);
        }

        /// <summary>
        /// Send a multicast binary packet and wait for a response.
        /// </summary>
        public async Task<byte[]> QueryMulticastBinaryAsync(byte[] data, IPEndPoint multicastEp, int timeoutMs = 2000)
        {
            await SendMulticastBinaryAsync(data, multicastEp);
            return await ReceiveMulticastBinaryAsync(timeoutMs);
        }
        #endregion

        public void Dispose()
        {
            _client.Close();
            _client.Dispose();
        }
    }

    public enum UdpPayloadMode
    {
        Text,
        Binary,
    }

    public static class IPAddressExtensions
    {
        public static bool IsMulticast(this IPAddress ip)
        {
            byte first = ip.GetAddressBytes()[0];
            return first >= 224 && first <= 239;
        }
    }
}