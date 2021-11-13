using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;

namespace SuchByte.MacroDeck.Server
{
    public static class BroadcastServer
    {

        static UdpClient _udpClient;

        public static void Start()
        {
            try
            {
                _udpClient = new UdpClient();
                //_udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, MacroDeck.Configuration.Host_Port));

                Timer broadcastTimer = new Timer(1000 * 10)
                {
                    Enabled = true
                };
                broadcastTimer.Elapsed += BroadcastTimer_Elapsed;
            } catch (Exception e)
            {
                Debug.WriteLine(e.StackTrace);
            }
        }

        private static void BroadcastTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                JObject broacastObject = new JObject
                {
                    ["method"] = "brodcast-ip",
                    ["computer-name"] = Environment.MachineName,
                    ["ip-address"] = MacroDeck.Configuration.Host_Address,
                    ["port"] = MacroDeck.Configuration.Host_Port,
                };
                var data = Encoding.UTF8.GetBytes(broacastObject.ToString());
                _udpClient.Send(data, data.Length, "255.255.255.255", MacroDeck.Configuration.Host_Port);
            } catch { }
        }
    }
}
