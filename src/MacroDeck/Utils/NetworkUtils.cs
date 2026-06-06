
using SuchByte.MacroDeck.Logging;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SuchByte.MacroDeck.Utils;

internal class NetworkUtils
{
    public static string[] GetNetworkInterfaces()
    {
        var networkInterfaces = new List<string>();
        try
        {
            networkInterfaces.AddRange(NetworkInterface.GetAllNetworkInterfaces()
                .Select(adapter => adapter.GetIPProperties()
                    .UnicastAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address.ToString())
                .Where(address => !string.IsNullOrWhiteSpace(address) && address != "127.0.0.1"));
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Warning($"Error while searching for network interfaces\n{ex.Message}");
        }

        return networkInterfaces.ToArray();
    }
}
