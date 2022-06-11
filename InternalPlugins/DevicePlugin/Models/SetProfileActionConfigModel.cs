using SuchByte.MacroDeck.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

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
