using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpSimulator
{
    public static class TextRequetProcessor
    {
        public static string Process(string cmd)
        {
            cmd = cmd.ToUpperInvariant();

            switch (cmd)
            {
                case "*IDN?":
                    return "ACME,UDP-MOCK,MODEL-1,1.0";

                case "*OPC?":
                    return "1";

                case "*RST":
                    return "OK";

                case "SYST:ERR?":
                    return "0,\"No error\"";

                default:
                    if (cmd.EndsWith("?"))
                        return "ERROR:UNKNOWN QUERY";
                    return "OK";
            }
        }
    }
}
