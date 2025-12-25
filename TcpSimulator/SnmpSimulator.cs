using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TelnetSimulator
{
    /// <summary>
    /// 简易 SNMP v1/v2c 模拟服务（用于 SnmpClient 调试）
    /// - 监听 UDP（默认 161，可指定非特权端口用于调试）
    /// - 只做最小 BER 解析：提取 community、request-id、varbind 中的 OID 列表
    /// - 返回 GetResponse（0xA2），为每个请求的 OID 返回可配置的值（默认 "Simulated"）
    /// 说明：这是一个调试用的轻量模拟器，不尝试实现完整 ASN.1/BER 或完整 SNMP 协议。
    /// </summary>
    public class SnmpSimulator : IDisposable
    {
        private readonly int _port;
        private readonly string _community;
        private readonly UdpClient _udp;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        /// <summary>
        /// 当收到原始请求时触发（用于调试显示原始数据）
        /// </summary>
        public event Action<IPEndPoint, byte[]>? RawRequestReceived;

        /// <summary>
        /// 当发送响应时触发（用于调试显示原始数据）
        /// </summary>
        public event Action<IPEndPoint, byte[]>? RawResponseSent;

        /// <summary>
        /// 可配置的 OID -> value 提供器（函数返回字符串，将作为 OCTET STRING 返回）。
        /// 如果未命中则返回默认值。
        /// </summary>
        public ConcurrentDictionary<string, Func<string>> OidProviders { get; } = new();

        /// <summary>
        /// 默认用于返回的字符串值
        /// </summary>
        public string DefaultValue { get; set; } = "Simulated";

        public SnmpSimulator(int port = 161, string community = "public", bool allowAddressReuse = true)
        {
            _port = port;
            _community = community;

            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
            if (allowAddressReuse)
            {
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }

            // 常见测试 OID 示例
            OidProviders.TryAdd("1.3.6.1.2.1.1.1.0", () => "SNMP Simulator"); // sysDescr.0
            OidProviders.TryAdd("1.3.6.1.2.1.1.5.0", () => "SimulatedHost"); // sysName.0
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _loopTask?.Wait(500); } catch { }
            _cts = null;
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var client = _udp;
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult res;
                try
                {
                    res = await client.ReceiveAsync(token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { continue; }

                _ = Task.Run(() => HandleDatagram(res), token);
            }
        }

        private void HandleDatagram(UdpReceiveResult res)
        {
            var remote = res.RemoteEndPoint;
            var data = res.Buffer;
            RawRequestReceived?.Invoke(remote, data);

            try
            {
                // 简要解析：寻找 community 与 request-id 与 OIDs（仅为支持调试/基本应答）
                int idx = 0;
                if (!TryReadSequence(data, ref idx, out _)) return;
                int ver = ReadInteger(data, ref idx);
                string community = ReadOctetString(data, ref idx) ?? string.Empty;
                if (community != _community)
                {
                    // 非目标 community，忽略（或者可以也回应）
                    return;
                }

                // next should be PDU (tag 0xA0..0xA7). We only care GetRequest/GetNext (0xA0/0xA1)
                if (idx >= data.Length) return;
                byte pduTag = data[idx++];
                int pduLen = ReadLength(data, ref idx);
                int pduEnd = idx + pduLen;

                // request-id
                int requestId = ReadInteger(data, ref idx);
                // error and index (skip)
                int _ = ReadInteger(data, ref idx);
                int __ = ReadInteger(data, ref idx);

                // varbind list
                if (!TryReadSequence(data, ref idx, out _)) { /* empty */ }
                var oids = new List<string>();
                while (idx < pduEnd)
                {
                    // each varbind is a sequence
                    if (!TryReadSequence(data, ref idx, out _)) break;
                    // oid
                    string? oid = ReadOid(data, ref idx);
                    // value: skip (read tag+len+value)
                    if (idx < data.Length)
                    {
                        if (idx < data.Length)
                        {
                            byte valTag = data[idx++];
                            int valLen = ReadLength(data, ref idx);
                            idx += valLen;
                        }
                    }
                    if (oid != null) oids.Add(oid);
                }

                // 准备返回 GetResponse (tag 0xA2)
                var varbindResponses = new List<(string oid, byte[] valueBytes, byte valueTag)>();
                foreach (var oid in oids)
                {
                    string valStr = DefaultValue;
                    if (OidProviders.TryGetValue(oid, out var provider))
                        try { valStr = provider(); } catch { valStr = DefaultValue; }

                    var valBytes = Encoding.UTF8.GetBytes(valStr);
                    varbindResponses.Add((oid, valBytes, 0x04));
                }

                var resp = BuildResponse(version: ver, community: community, requestId: requestId, varbinds: varbindResponses);
                _udp.Send(resp, resp.Length, remote);
                RawResponseSent?.Invoke(remote, resp);
            }
            catch
            {
                // 忽略解析/构造错误（调试模拟）
            }
        }

        #region BER helpers (minimal)

        private static byte[] BuildResponse(int version, string community, int requestId, List<(string oid, byte[] valueBytes, byte valueTag)> varbinds)
        {
            var pduContent = new List<byte>();
            // request-id
            pduContent.AddRange(EncodeInteger(requestId));
            // error
            pduContent.AddRange(EncodeInteger(0));
            // error-index
            pduContent.AddRange(EncodeInteger(0));

            // varbind list
            var vbList = new List<byte>();
            foreach (var vb in varbinds)
            {
                var vbSeq = new List<byte>();
                vbSeq.AddRange(EncodeOid(vb.oid));
                // value
                vbSeq.Add(vb.valueTag);
                vbSeq.AddRange(EncodeLength(vb.valueBytes.Length));
                vbSeq.AddRange(vb.valueBytes);

                // wrap varbind sequence
                var vbEncoded = WrapSequence(vbSeq.ToArray());
                vbList.AddRange(vbEncoded);
            }
            var vbListWrapped = WrapSequence(vbList.ToArray());
            pduContent.AddRange(vbListWrapped);

            // wrap pdu with tag GetResponse (0xA2)
            var pduBytes = pduContent.ToArray();
            var pduWrapped = new List<byte> { 0xA2 };
            pduWrapped.AddRange(EncodeLength(pduBytes.Length));
            pduWrapped.AddRange(pduBytes);

            // message sequence: version, community, pdu
            var msg = new List<byte>();
            msg.AddRange(EncodeInteger(version));
            msg.AddRange(EncodeOctetString(Encoding.UTF8.GetBytes(community)));
            msg.AddRange(pduWrapped);

            var full = WrapSequence(msg.ToArray());
            return full;
        }

        private static byte[] WrapSequence(byte[] content)
        {
            var list = new List<byte> { 0x30 };
            list.AddRange(EncodeLength(content.Length));
            list.AddRange(content);
            return list.ToArray();
        }

        private static byte[] EncodeInteger(int value)
        {
            // encode minimal two's complement big-endian
            if (value == 0) return new byte[] { 0x02, 0x01, 0x00 };
            var tmp = new List<byte>();
            uint v = (uint)value;
            while (v != 0)
            {
                tmp.Insert(0, (byte)(v & 0xFF));
                v >>= 8;
            }
            // ensure highest bit not interpreted as sign bit positive
            if ((tmp[0] & 0x80) != 0) tmp.Insert(0, 0x00);
            var outb = new List<byte> { 0x02 };
            outb.AddRange(EncodeLength(tmp.Count));
            outb.AddRange(tmp);
            return outb.ToArray();
        }

        private static byte[] EncodeOctetString(byte[] data)
        {
            var outb = new List<byte> { 0x04 };
            outb.AddRange(EncodeLength(data.Length));
            outb.AddRange(data);
            return outb.ToArray();
        }

        private static byte[] EncodeOidBytes(byte[] oidBody)
        {
            var outb = new List<byte> { 0x06 };
            outb.AddRange(EncodeLength(oidBody.Length));
            outb.AddRange(oidBody);
            return outb.ToArray();
        }

        private static byte[] EncodeOid(string dotted)
        {
            var parts = dotted.Split('.');
            if (parts.Length < 2) return EncodeOidBytes(Array.Empty<byte>());

            var first = int.Parse(parts[0]);
            var second = int.Parse(parts[1]);
            var body = new List<byte> { (byte)(first * 40 + second) };
            for (int i = 2; i < parts.Length; i++)
            {
                uint val = uint.Parse(parts[i]);
                var enc = new List<byte>();
                do
                {
                    byte b = (byte)(val & 0x7F);
                    enc.Insert(0, b);
                    val >>= 7;
                } while (val != 0);
                for (int j = 0; j < enc.Count - 1; j++) enc[j] |= 0x80;
                body.AddRange(enc);
            }
            return EncodeOidBytes(body.ToArray());
        }

        private static byte[] EncodeLength(int len)
        {
            if (len < 0x80) return new byte[] { (byte)len };
            var tmp = new List<byte>();
            int v = len;
            while (v > 0)
            {
                tmp.Insert(0, (byte)(v & 0xFF));
                v >>= 8;
            }
            var outb = new List<byte> { (byte)(0x80 | tmp.Count) };
            outb.AddRange(tmp);
            return outb.ToArray();
        }

        // Minimal readers (no comprehensive validation)
        private static bool TryReadSequence(byte[] data, ref int idx, out int length)
        {
            length = 0;
            if (idx >= data.Length) return false;
            if (data[idx++] != 0x30) return false;
            length = ReadLength(data, ref idx);
            return true;
        }

        private static int ReadLength(byte[] data, ref int idx)
        {
            if (idx >= data.Length) return 0;
            byte b = data[idx++];
            if ((b & 0x80) == 0) return b;
            int count = b & 0x7F;
            int val = 0;
            for (int i = 0; i < count && idx < data.Length; i++)
            {
                val = (val << 8) | data[idx++];
            }
            return val;
        }

        private static int ReadInteger(byte[] data, ref int idx)
        {
            if (idx >= data.Length || data[idx++] != 0x02) return 0;
            int len = ReadLength(data, ref idx);
            int val = 0;
            for (int i = 0; i < len && idx < data.Length; i++)
            {
                val = (val << 8) | data[idx++];
            }
            // interpret as signed if necessary (not needed for request-id)
            return val;
        }

        private static string? ReadOctetString(byte[] data, ref int idx)
        {
            if (idx >= data.Length || data[idx++] != 0x04) return null;
            int len = ReadLength(data, ref idx);
            if (len <= 0) return string.Empty;
            if (idx + len > data.Length) return null;
            var s = Encoding.UTF8.GetString(data, idx, len);
            idx += len;
            return s;
        }

        private static string? ReadOid(byte[] data, ref int idx)
        {
            if (idx >= data.Length || data[idx++] != 0x06) return null;
            int len = ReadLength(data, ref idx);
            if (len <= 0) return null;
            if (idx + len > data.Length) return null;
            int end = idx + len;
            var bytes = new List<byte>();
            while (idx < end) bytes.Add(data[idx++]);
            // decode
            if (bytes.Count == 0) return null;
            int first = bytes[0] / 40;
            int second = bytes[0] % 40;
            var parts = new List<string> { first.ToString(), second.ToString() };
            ulong value = 0;
            for (int i = 1; i < bytes.Count; i++)
            {
                byte b = bytes[i];
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    parts.Add(value.ToString());
                    value = 0;
                }
            }
            return string.Join('.', parts);
        }

        #endregion

        public void Dispose()
        {
            Stop();
            _udp.Dispose();
        }
    }
}