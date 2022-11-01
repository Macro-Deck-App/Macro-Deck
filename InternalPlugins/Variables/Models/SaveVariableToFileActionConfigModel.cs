using System.Text.Json;
using SuchByte.MacroDeck.Models;

namespace SuchByte.MacroDeck.Variables.Plugin.Models;

public class SaveVariableToFileActionConfigModel : ISerializableConfiguration
{
    public string FilePath { get; set; }

    public string Variable { get; set; }


    public string Serialize()
    {
        return JsonSerializer.Serialize(this);
    }

    public static SaveVariableToFileActionConfigModel Deserialize(string config)
    {
        return ISerializableConfiguration.Deserialize<SaveVariableToFileActionConfigModel>(config);
    }
}