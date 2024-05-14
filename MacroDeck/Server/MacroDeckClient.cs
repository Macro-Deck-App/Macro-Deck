using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server.DeviceMessage;

namespace SuchByte.MacroDeck.Server;

public class MacroDeckClient
{
    public MacroDeckClient(string sessionId)
    {
        SessionId = sessionId;
    }

    private DeviceType _deviceType;

    public void SetClientId(string clientId)
    {
        ClientId = clientId;
    }
    
    public MacroDeckFolder Folder { get; set; }

    public MacroDeckProfile Profile { get; set; }
    public string ClientId { get; private set; }
    public string SessionId { get; private set; }

    public DeviceClass DeviceClass { get; set; } = DeviceClass.SoftwareClient;


    public DeviceType DeviceType
    {
        get => _deviceType;
        set
        {
            _deviceType = value;
            switch (_deviceType)
            {
                case DeviceType.Unknown:
                case DeviceType.Web:
                case DeviceType.Android:
                case DeviceType.StreamDeck:
                case DeviceType.iOS:
                    DeviceClass = DeviceClass.SoftwareClient;
                    DeviceMessage = new SoftwareClientMessage();
                    break;
            }
        }
    }

    public IDeviceMessage DeviceMessage { get; set; }
}