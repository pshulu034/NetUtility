using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SnmpSimulator
{
    public sealed class SnmpServer : IDisposable
    {
        private readonly int _port;
        private UdpClient? _udp;
        private CancellationTokenSource? _cts;
        private Task? _loop;

        public Func<string, string?>? OidLookup { get; set; }
        public Func<string, string, bool>? OidSet { get; set; }

        public event Action<EndPoint, string>? TextReceived;
        public event Action<EndPoint, string>? TextSent;

        public SnmpServer(int port = 161)
        {
            _port = port;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _udp = new UdpClient(_port);
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _udp?.Close(); } catch { }
            try { _udp?.Dispose(); } catch { }
            _udp = null;
            _cts = null;
            _loop = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task LoopAsync(CancellationToken token)
        {
            if (_udp == null) return;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var res = await _udp.ReceiveAsync(token).ConfigureAwait(false);
                    var remote = res.RemoteEndPoint;
                    var data = res.Buffer;
                    var setReq = ParseSetRequest(data);
                    if (setReq != null)
                    {
                        TextReceived?.Invoke(remote, $"SET {string.Join(", ", setReq.Select(x => x.oid))}");
                        var vars = new List<(string oid, byte[] valueTagAndValue)>();
                        foreach (var (oid, valTag, valBytes) in setReq)
                        {
                            bool ok = false;
                            if (valTag == 0x04)
                            {
                                var s = Encoding.UTF8.GetString(valBytes);
                                ok = OidSet?.Invoke(oid, s) ?? false;
                            }
                            vars.Add((oid, ok ? EncodeOctetString(valBytes) : BuildNullValue()));
                        }
                        var resp = BuildGetResponse(Environment.TickCount & int.MaxValue, "public", vars);
                        await _udp.SendAsync(resp, resp.Length, remote).ConfigureAwait(false);
                        TextSent?.Invoke(remote, $"RESP {string.Join(", ", setReq.Select(x => x.oid))}");
                        continue;
                    }

                    var req = ParseGetRequest(data);
                    if (req == null) continue;
                    TextReceived?.Invoke(remote, $"GET {string.Join(", ", req.Oids)}");
                    var vars2 = new List<(string oid, byte[] valueTagAndValue)>();
                    foreach (var oid in req.Oids)
                    {
                        string? s = OidLookup?.Invoke(oid);
                        if (s == null)
                        {
                            vars2.Add((oid, BuildNullValue()));
                        }
                        else
                        {
                            vars2.Add((oid, EncodeOctetString(Encoding.UTF8.GetBytes(s))));
                        }
                    }
                    var resp2 = BuildGetResponse(req.RequestId, req.Community, vars2);
                    await _udp.SendAsync(resp2, resp2.Length, remote).ConfigureAwait(false);
                    TextSent?.Invoke(remote, $"RESP {string.Join(", ", req.Oids)}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                }
            }
        }

        private sealed class GetRequest
        {
            public int RequestId { get; init; }
            public string Community { get; init; } = "public";
            public List<string> Oids { get; init; } = new List<string>();
        }

        private GetRequest? ParseGetRequest(byte[] data)
        {
            try
            {
                int idx = 0;
                if (!TryReadSequence(data, ref idx, out _)) return null;
                int version = ReadInteger(data, ref idx);
                string community = ReadOctetString(data, ref idx) ?? "public";
                if (idx >= data.Length) return null;
                byte pduTag = data[idx++];
                if (pduTag != 0xA0) return null;
                int pduLen = ReadLength(data, ref idx);
                int pduEnd = idx + pduLen;
                int requestId = ReadInteger(data, ref idx);
                int errorStatus = ReadInteger(data, ref idx);
                int errorIndex = ReadInteger(data, ref idx);
                if (!TryReadSequence(data, ref idx, out _)) return null;
                var oids = new List<string>();
                while (idx < data.Length && idx < pduEnd)
                {
                    if (!TryReadSequence(data, ref idx, out _)) break;
                    string? oid = ReadOid(data, ref idx);
                    if (oid == null) break;
                    if (idx >= data.Length) break;
                    byte valTag = data[idx++];
                    int valLen = ReadLength(data, ref idx);
                    idx += valLen;
                    oids.Add(oid);
                }
                return new GetRequest { RequestId = requestId, Community = community, Oids = oids };
            }
            catch
            {
                return null;
            }
        }

        private List<(string oid, byte valTag, byte[] valBytes)>? ParseSetRequest(byte[] data)
        {
            try
            {
                int idx = 0;
                if (!TryReadSequence(data, ref idx, out _)) return null;
                int version = ReadInteger(data, ref idx);
                string community = ReadOctetString(data, ref idx) ?? "public";
                if (idx >= data.Length) return null;
                byte pduTag = data[idx++];
                if (pduTag != 0xA3) return null;
                int pduLen = ReadLength(data, ref idx);
                int pduEnd = idx + pduLen;
                int requestId = ReadInteger(data, ref idx);
                int errorStatus = ReadInteger(data, ref idx);
                int errorIndex = ReadInteger(data, ref idx);
                if (!TryReadSequence(data, ref idx, out _)) return null;
                var list = new List<(string oid, byte valTag, byte[] valBytes)>();
                while (idx < data.Length && idx < pduEnd)
                {
                    if (!TryReadSequence(data, ref idx, out _)) break;
                    string? oid = ReadOid(data, ref idx);
                    if (oid == null) break;
                    if (idx >= data.Length) break;
                    byte valTag = data[idx++];
                    int valLen = ReadLength(data, ref idx);
                    if (idx + valLen > data.Length) break;
                    var valBytes = new byte[valLen];
                    Array.Copy(data, idx, valBytes, 0, valLen);
                    idx += valLen;
                    list.Add((oid, valTag, valBytes));
                }
                return list;
            }
            catch
            {
                return null;
            }
        }

        private byte[] BuildGetResponse(int requestId, string community, List<(string oid, byte[] valueTagAndValue)> vars)
        {
            var pduContent = new List<byte>();
            pduContent.AddRange(EncodeInteger(requestId));
            pduContent.AddRange(EncodeInteger(0));
            pduContent.AddRange(EncodeInteger(0));
            var vbList = new List<byte>();
            foreach (var v in vars)
            {
                var vbSeq = new List<byte>();
                vbSeq.AddRange(EncodeOid(v.oid));
                vbSeq.AddRange(v.valueTagAndValue);
                var vbWrapped = WrapSequence(vbSeq.ToArray());
                vbList.AddRange(vbWrapped);
            }
            var vbListWrapped = WrapSequence(vbList.ToArray());
            pduContent.AddRange(vbListWrapped);
            var pduBytes = pduContent.ToArray();
            var pduWrapped = new List<byte> { 0xA2 };
            pduWrapped.AddRange(EncodeLength(pduBytes.Length));
            pduWrapped.AddRange(pduBytes);
            var msg = new List<byte>();
            msg.AddRange(EncodeInteger(1));
            msg.AddRange(EncodeOctetString(Encoding.ASCII.GetBytes(community)));
            msg.AddRange(pduWrapped);
            return WrapSequence(msg.ToArray());
        }

        private byte[] BuildNullValue()
        {
            return new byte[] { 0x05, 0x00 };
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
            if (tmp.Count == 0 || (tmp[0] & 0x80) != 0) tmp.Insert(0, 0x00);
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
    }
}
