using SuchByte.MacroDeck.InternalPlugins.Variables.Enums;
using SuchByte.MacroDeck.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuchByte.MacroDeck.InternalPlugins.Variables.Models
{
    public class ChangeVariableValueActionConfigModel : ISerializableConfiguration
    {

        [JsonPropertyName("method"), JsonConverter(typeof(JsonStringEnumConverter))]
        public ChangeVariableMethod Method { get; set; } = ChangeVariableMethod.countUp;


        [JsonPropertyName("variable")]
        public string Variable { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";


        public string Serialize()
        {
            return JsonSerializer.Serialize(this);
        }

        public static ChangeVariableValueActionConfigModel Deserialize(string config)
        {
            return ISerializableConfiguration.Deserialize<ChangeVariableValueActionConfigModel>(config);
        }
    }
}
