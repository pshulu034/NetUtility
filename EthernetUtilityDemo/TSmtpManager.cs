using EthernetUtility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EthernetUtilityDemo
{
    public class TSmtpManager
    {
        public async static Task Test()
        {
            Console.WriteLine("\n[QQ SMTP] Send email via QQ now...");
            try
            {
                var host = "smtp.qq.com";
                var port = 587;
                var enableSsl = true;
                var username = "1429837208@qq.com";
                var from = "1429837208@qq.com";
                var toList = new List<string> { "snetwork@163.com" };
                var password = "ofqwhcuezozefhfb";

                using var smtp = new SmtpClientEx(host, port, enableSsl, username, password);
                await smtp.SendAsync(from, toList, "Trae SMTP Test", "This is a test email sent from HttpManagerConsole using QQ SMTP.", true);
                Console.WriteLine("QQ SMTP send succeeded.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"QQ SMTP send failed: {ex.Message}");
            }

            Console.WriteLine("\n------------------------------------------------------------");
            Console.WriteLine("Demo completed. Press any key to exit.");
            Console.ReadKey();
        }
    }
}
