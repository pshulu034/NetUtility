using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TcpSimulator
{
    public static class BinaryRequestProcessor
    {
        public static byte[]? Process(byte[] req)
        {
            // 示例协议（与 UdpSimulator 保持一致）：
            // [0] = 0xAA
            // [1] = CMD
            // [2] = LEN
            if (req == null || req.Length < 3 || req[0] != 0xAA)
                return null;

            byte cmd = req[1];

            switch (cmd)
            {
                case 0x01: // 读状态
                    return new byte[] { 0xAA, 0x01, 0x01, 0x00 };

                case 0x02: // 版本
                    return new byte[] { 0xAA, 0x02, 0x02, 0x01, 0x10 };

                default:
                    return new byte[] { 0xAA, 0xFF };
            }
        }
    }
}
