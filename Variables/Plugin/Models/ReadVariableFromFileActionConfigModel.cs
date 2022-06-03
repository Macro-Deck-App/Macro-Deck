using SuchByte.MacroDeck.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace SuchByte.MacroDeck.Variables.Plugin.Models
{
    public class ReadVariableFromFileActionConfigModel : ISerializableConfiguration
    {
        public string FilePath { get; set; }

        public string Variable { get; set; }


        public string Serialize()
        {
            return JsonSerializer.Serialize(this);
        }

        public static ReadVariableFromFileActionConfigModel Deserialize(string config)
        {
            return ISerializableConfiguration.Deserialize<ReadVariableFromFileActionConfigModel>(config);
        }
    }
}
