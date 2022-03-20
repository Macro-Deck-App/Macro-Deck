using Newtonsoft.Json;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Server.DeviceMessage;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Device
{
    public class MacroDeckDevice
    {


        public string ClientId { get; set; }
        public string DisplayName { get; set; }

        [JsonIgnore]
        public bool Available { get {
                MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(this.ClientId);
                if (macroDeckClient != null && macroDeckClient.SocketConnection != null && macroDeckClient.SocketConnection.IsAvailable)
                {
                    return true;
                }
                return false;
            }
        }

        public bool Blocked { get; set; } = false;

        public string ProfileId { get; set; }

        public DeviceConfiguration Configuration { get; set; } = new DeviceConfiguration();

        public DeviceType DeviceType { get; set; }

    }
}
