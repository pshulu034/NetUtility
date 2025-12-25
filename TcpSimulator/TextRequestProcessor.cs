using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TcpSimulator
{
    public static class TextRequestProcessor
    {
        public static string Process(string cmd)
        {
            if (cmd == null) return string.Empty;
            cmd = cmd.Trim().ToUpperInvariant();

            switch (cmd)
            {
                case "*IDN?":
                    return "Simulator,TCP-MOCK,MODEL-1,1.0";

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
