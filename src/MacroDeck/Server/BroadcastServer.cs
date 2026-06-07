using System.Net.Sockets;
using System.Timers;
using Newtonsoft.Json.Linq;
using Timer = System.Timers.Timer;

namespace SuchByte.MacroDeck.Server;

public static class BroadcastServer
{
    private static UdpClient _udpClient;
    private static Timer _broadcastTimer;

    public static void Start()
    {
        try
        {
            _udpClient = new UdpClient();

            _broadcastTimer = new Timer(1000 * 5)
            {
                Enabled = true
            };
            _broadcastTimer.Elapsed += BroadcastTimer_Elapsed;

            Application.ApplicationExit += OnApplicationExit;
        }
        catch
        {
        }
    }

    private static void OnApplicationExit(object sender, EventArgs e)
    {
        Application.ApplicationExit -= OnApplicationExit;
        Stop();
    }

    public static void Stop()
    {
        try
        {
            if (_broadcastTimer != null)
            {
                _broadcastTimer.Stop();
                _broadcastTimer.Elapsed -= BroadcastTimer_Elapsed;
                _broadcastTimer.Dispose();
                _broadcastTimer = null;
            }

            _udpClient?.Dispose();
            _udpClient = null;
        }
        catch
        {
        }
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
                    ["port"] = MacroDeck.Configuration.HostPort
                };
                var data = Encoding.UTF8.GetBytes(broacastObject.ToString());
                _udpClient.Send(data, data.Length, "255.255.255.255", MacroDeck.Configuration.HostPort);
            });
        }
        catch
        {
        }
    }
}
