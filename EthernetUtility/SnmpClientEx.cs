using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EthernetUtility
{
    /// <summary>
    /// Minimal SNMP v2c client wrapper for simple Get requests.
    /// - Designed for debugging and demo use (works with SnmpSimulator in this workspace).
    /// - Not a full SNMP implementation; supports basic BER for GET and basic response parsing.
    /// </summary>
    public sealed class SnmpClientEx : IDisposable
    {
        private readonly UdpClient _udp;
        private readonly IPEndPoint _remote;
        private readonly string _community;
        private readonly int _timeoutMs;

        public SnmpClientEx(string host, int port = 161, string community = "public", int timeoutMs = 2000)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentNullException(nameof(host));
            _remote = new IPEndPoint(Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork), port);
            _udp = new UdpClient();
            _community = community ?? "public";
            _timeoutMs = timeoutMs;
        }

        public void Dispose()
        {
            _udp.Dispose();
        }

        public async Task<SnmpVar?> GetAsync(string oid, CancellationToken cancellation = default)
        {
            var dict = await GetAsync(new[] { oid }, cancellation).ConfigureAwait(false);
            return dict.TryGetValue(oid, out var v) ? v : null;
        }

        public async Task<Dictionary<string, SnmpVar>> GetAsync(IEnumerable<string> oids, CancellationToken cancellation = default)
        {
            var list = oids?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? Array.Empty<string>();
            if (list.Length == 0) return new Dictionary<string, SnmpVar>();

            int requestId = Environment.TickCount & int.MaxValue;
            var req = BuildGetRequest(requestId, _community, list);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            cts.CancelAfter(_timeoutMs);

            await _udp.SendAsync(req, req.Length, _remote).ConfigureAwait(false);

            try
            {
                var receiveTask = _udp.ReceiveAsync();
                using (cts.Token.Register(() => { try { _udp.Close(); } catch { } }))
                {
                    var res = await receiveTask.ConfigureAwait(false);
                    return ParseGetResponse(res.Buffer, requestId);
                }
            }
            catch (SocketException)
            {
                return new Dictionary<string, SnmpVar>();
            }
            catch (ObjectDisposedException)
            {
                return new Dictionary<string, SnmpVar>();
            }
            catch (OperationCanceledException)
            {
                return new Dictionary<string, SnmpVar>();
            }
        }

        public async Task<SnmpVar?> SetAsync(string oid, string value, CancellationToken cancellation = default)
        {
            var dict = await SetAsync(new Dictionary<string, string> { { oid, value } }, cancellation).ConfigureAwait(false);
            return dict.TryGetValue(oid, out var v) ? v : null;
        }

        public async Task<Dictionary<string, SnmpVar>> SetAsync(IDictionary<string, string> oidValues, CancellationToken cancellation = default)
        {
            var pairs = (oidValues ?? new Dictionary<string, string>())
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .ToArray();
            if (pairs.Length == 0) return new Dictionary<string, SnmpVar>();

            int requestId = Environment.TickCount & int.MaxValue;
            var req = BuildSetRequest(requestId, _community, pairs);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            cts.CancelAfter(_timeoutMs);

            await _udp.SendAsync(req, req.Length, _remote).ConfigureAwait(false);

            try
            {
                var receiveTask = _udp.ReceiveAsync();
                using (cts.Token.Register(() => { try { _udp.Close(); } catch { } }))
                {
                    var res = await receiveTask.ConfigureAwait(false);
                    return ParseGetResponse(res.Buffer, requestId);
                }
            }
            catch
            {
                return new Dictionary<string, SnmpVar>();
            }
        }

        #region BER Encoding (minimal)

        private static byte[] BuildGetRequest(int requestId, string community, string[] oids)
        {
            var pduContent = new List<byte>();
            pduContent.AddRange(EncodeInteger(requestId)); // request-id
            pduContent.AddRange(EncodeInteger(0)); // error-status
            pduContent.AddRange(EncodeInteger(0)); // error-index

            // varbind list
            var vbList = new List<byte>();
            foreach (var oid in oids)
            {
                var vbSeq = new List<byte>();
                vbSeq.AddRange(EncodeOid(oid));
                // value = NULL for GET request
                vbSeq.Add(0x05); // NULL tag
                vbSeq.AddRange(EncodeLength(0));
                var vbWrapped = WrapSequence(vbSeq.ToArray());
                vbList.AddRange(vbWrapped);
            }

            var vbListWrapped = WrapSequence(vbList.ToArray());
            pduContent.AddRange(vbListWrapped);

            // wrap pdu with GetRequest tag (0xA0)
            var pduBytes = pduContent.ToArray();
            var pduWrapped = new List<byte> { 0xA0 };
            pduWrapped.AddRange(EncodeLength(pduBytes.Length));
            pduWrapped.AddRange(pduBytes);

            // message: version(1) + community + pdu
            var msg = new List<byte>();
            msg.AddRange(EncodeInteger(1)); // SNMPv2c -> version = 1
            msg.AddRange(EncodeOctetString(Encoding.ASCII.GetBytes(community)));
            msg.AddRange(pduWrapped);

            return WrapSequence(msg.ToArray());
        }

        private static byte[] BuildSetRequest(int requestId, string community, KeyValuePair<string, string>[] oidValues)
        {
            var pduContent = new List<byte>();
            pduContent.AddRange(EncodeInteger(requestId));
            pduContent.AddRange(EncodeInteger(0));
            pduContent.AddRange(EncodeInteger(0));

            var vbList = new List<byte>();
            foreach (var kv in oidValues)
            {
                var vbSeq = new List<byte>();
                vbSeq.AddRange(EncodeOid(kv.Key));
                var valBytes = Encoding.UTF8.GetBytes(kv.Value ?? string.Empty);
                vbSeq.AddRange(EncodeOctetString(valBytes));
                var vbWrapped = WrapSequence(vbSeq.ToArray());
                vbList.AddRange(vbWrapped);
            }

            var vbListWrapped = WrapSequence(vbList.ToArray());
            pduContent.AddRange(vbListWrapped);

            var pduBytes = pduContent.ToArray();
            var pduWrapped = new List<byte> { 0xA3 }; // SetRequest
            pduWrapped.AddRange(EncodeLength(pduBytes.Length));
            pduWrapped.AddRange(pduBytes);

            var msg = new List<byte>();
            msg.AddRange(EncodeInteger(1)); // SNMPv2c -> version = 1
            msg.AddRange(EncodeOctetString(Encoding.ASCII.GetBytes(community)));
            msg.AddRange(pduWrapped);

            return WrapSequence(msg.ToArray());
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
            if (value == 0) return new byte[] { 0x02, 0x01, 0x00 };
            var tmp = new List<byte>();
            uint v = (uint)value;
            while (v != 0)
            {
                tmp.Insert(0, (byte)(v & 0xFF));
                v >>= 8;
            }
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

        private static byte[] EncodeOidBytes(byte[] oidBody)
        {
            var outb = new List<byte> { 0x06 };
            outb.AddRange(EncodeLength(oidBody.Length));
            outb.AddRange(oidBody);
            return outb.ToArray();
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

        #endregion

        #region BER Parsing (minimal)

        private static Dictionary<string, SnmpVar> ParseGetResponse(byte[] data, int expectedRequestId)
        {
            var result = new Dictionary<string, SnmpVar>();
            try
            {
                int idx = 0;
                if (!TryReadSequence(data, ref idx, out _)) return result;
                int version = ReadInteger(data, ref idx);
                string community = ReadOctetString(data, ref idx) ?? string.Empty;
                if (idx >= data.Length) return result;
                byte pduTag = data[idx++]; // expect 0xA2 (GetResponse)
                int pduLen = ReadLength(data, ref idx);
                int pduEnd = idx + pduLen;

                int requestId = ReadInteger(data, ref idx);
                int errorStatus = ReadInteger(data, ref idx);
                int errorIndex = ReadInteger(data, ref idx);

                // varbind list
                if (!TryReadSequence(data, ref idx, out _)) return result;
                while (idx < data.Length && idx < pduEnd)
                {
                    if (!TryReadSequence(data, ref idx, out _)) break;
                    string? oid = ReadOid(data, ref idx);
                    if (idx >= data.Length) break;
                    byte valTag = data[idx++];
                    int valLen = ReadLength(data, ref idx);
                    SnmpVar? value = null;
                    switch (valTag)
                    {
                        case 0x02: // INTEGER
                            {
                                int intVal = 0;
                                for (int i = 0; i < valLen && idx < data.Length; i++)
                                {
                                    intVal = (intVal << 8) | data[idx++];
                                }
                                value = new SnmpVar(SnmpVarType.Integer, intVal);
                                break;
                            }
                        case 0x04: // OCTET STRING
                            {
                                var s = Encoding.UTF8.GetString(data, idx, valLen);
                                idx += valLen;
                                value = new SnmpVar(SnmpVarType.OctetString, s);
                                break;
                            }
                        case 0x05: // NULL
                            {
                                idx += valLen;
                                value = new SnmpVar(SnmpVarType.Null, null);
                                break;
                            }
                        case 0x06: // OID
                            {
                                string? v = ReadOidFromRaw(data, idx, valLen);
                                idx += valLen;
                                value = new SnmpVar(SnmpVarType.ObjectId, v);
                                break;
                            }
                        default:
                            {
                                // unknown: return raw bytes
                                var raw = new byte[valLen];
                                Array.Copy(data, idx, raw, 0, valLen);
                                idx += valLen;
                                value = new SnmpVar(SnmpVarType.Opaque, raw);
                                break;
                            }
                    }
                    if (oid != null && value != null)
                    {
                        result[oid] = value;
                    }
                }
            }
            catch
            {
                // ignore parsing errors for demo
            }
            return result;
        }

        // helpers for parsing
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
            string? v = ReadOidFromRaw(data, idx, len);
            idx += len;
            return v;
        }

        private static string? ReadOidFromRaw(byte[] data, int start, int len)
        {
            if (len <= 0) return null;
            int end = start + len;
            var bytes = new List<byte>();
            for (int i = start; i < end; i++) bytes.Add(data[i]);
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
    }

    public enum SnmpVarType
    {
        Integer,
        OctetString,
        ObjectId,
        Null,
        Opaque
    }

    public sealed class SnmpVar
    {
        public SnmpVarType Type { get; }
        public object? Value { get; }

        public SnmpVar(SnmpVarType type, object? value)
        {
            Type = type;
            Value = value;
        }

        public override string ToString()
        {
            return Type switch
            {
                SnmpVarType.Integer => $"Integer: {Value}",
                SnmpVarType.OctetString => $"String: {Value}",
                SnmpVarType.ObjectId => $"OID: {Value}",
                SnmpVarType.Null => "Null",
                SnmpVarType.Opaque => $"Raw: {BitConverter.ToString((byte[]?)Value ?? Array.Empty<byte>())}",
                _ => Value?.ToString() ?? string.Empty
            };
        }
    }
}
