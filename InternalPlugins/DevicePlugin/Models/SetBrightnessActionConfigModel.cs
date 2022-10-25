using System.Text.Json;
using SuchByte.MacroDeck.Models;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Models
{
    public class SetBrightnessActionConfigModel : ISerializableConfiguration
    {

        public string ClientId { get; set; }

        public float Brightness { get; set; } = 0.3f;

        public string Serialize()
        {
            return JsonSerializer.Serialize(this);
        }
        public static SetBrightnessActionConfigModel Deserialize(string config)
        {
            return ISerializableConfiguration.Deserialize<SetBrightnessActionConfigModel>(config);
        }
    }
}
