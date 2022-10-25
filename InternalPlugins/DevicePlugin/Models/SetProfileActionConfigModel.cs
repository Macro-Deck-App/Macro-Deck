using System.Text.Json;
using SuchByte.MacroDeck.Models;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Models
{
    public class SetProfileActionConfigModel : ISerializableConfiguration
    {

        public string ClientId { get; set; }

        public string ProfileId { get; set; }

        public string Serialize()
        {
            return JsonSerializer.Serialize(this);
        }
        public static SetProfileActionConfigModel Deserialize(string config)
        {
            return ISerializableConfiguration.Deserialize<SetProfileActionConfigModel>(config);
        }
    }
}
