using Newtonsoft.Json;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.Device;

public class MacroDeckDevice
{


    public string ClientId { get; set; }
    public string DisplayName { get; set; }

    [JsonIgnore]
    public bool Available { get {
            var macroDeckClient = MacroDeckServer.Instance.GetMacroDeckClient(ClientId);
            if (macroDeckClient != null && macroDeckClient.SocketConnection != null && macroDeckClient.SocketConnection.IsAvailable)
            {
                return true;
            }
            return false;
        }
    }

    public bool Blocked { get; set; } = false;

    public string ProfileId { get; set; }

    public DeviceConfiguration Configuration { get; set; } = new();

    public DeviceType DeviceType { get; set; }

}