using MacroDeck.Server;
using Newtonsoft.Json;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.Device;

public class MacroDeckDevice
{


    public string ClientId { get; set; }
    public string DisplayName { get; set; }

    [JsonIgnore]
    public bool Available { get {
            var macroDeckClient = MacroDeckServer.GetMacroDeckClient(ClientId);
            if (macroDeckClient != null && WebSocketHandler.IsAvailable(macroDeckClient.SessionId))
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