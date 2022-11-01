using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json.Linq;

namespace SuchByte.MacroDeck.Server;

public static class BroadcastServer
{

    static UdpClient _udpClient;

    public static void Start()
    {
        try
        {
            _udpClient = new UdpClient();

            var broadcastTimer = new Timer(1000 * 5)
            {
                Enabled = true
            };
            broadcastTimer.Elapsed += BroadcastTimer_Elapsed;
        } catch {}
    }

    private static void BroadcastTimer_Elapsed(object sender, ElapsedEventArgs e)
    {
        try
        {
            Task.Run(() =>
            {
                var broacastObject = new JObject
                {
                    ["computer-name"] = Environment.MachineName,
                    ["ip-address"] = MacroDeck.Configuration.HostAddress,
                    ["port"] = MacroDeck.Configuration.HostPort,
                };
                var data = Encoding.UTF8.GetBytes(broacastObject.ToString());
                _udpClient.Send(data, data.Length, "255.255.255.255", MacroDeck.Configuration.HostPort);
            });
        } catch { }
    }
}