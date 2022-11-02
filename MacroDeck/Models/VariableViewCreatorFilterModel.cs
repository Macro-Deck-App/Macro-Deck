using System.Collections.Generic;
using System.Text.Json;

namespace SuchByte.MacroDeck.Models;

public class VariableViewCreatorFilterModel : ISerializableConfiguration
{
    public List<string> HiddenCreators { get; set; } = new();

    public string Serialize()
    {
        return JsonSerializer.Serialize(this);
    }

    public static VariableViewCreatorFilterModel Deserialize(string config)
    {
        return ISerializableConfiguration.Deserialize<VariableViewCreatorFilterModel>(config);
    }
}