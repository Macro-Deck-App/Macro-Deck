using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Device
{
    public class MacroDeckDevice
    {

        public string ClientId { get; set; }
        public string DisplayName { get; set; }
        public bool Available { get {
                MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(this.ClientId);
                if (macroDeckClient != null && macroDeckClient.SocketConnection != null && macroDeckClient.SocketConnection.IsAvailable)
                {
                    return true;
                }
                return false;
            } }
        public bool Blocked { get; set; } = false;
        public string ProfileId { get; set; }
        public DeviceType DeviceType { get; set; }
        public DeviceConfiguration Configuration { get; set; } = new DeviceConfiguration();
    }
}
