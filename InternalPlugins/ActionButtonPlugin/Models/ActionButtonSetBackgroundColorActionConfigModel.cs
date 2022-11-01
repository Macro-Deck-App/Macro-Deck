﻿using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Enums;
using SuchByte.MacroDeck.Models;

namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Models;

public class ActionButtonSetBackgroundColorActionConfigModel : ISerializableConfiguration
{
    public string ColorHex { get; set; } = "#232323";

    [JsonIgnore]
    public Color Color
    {
        get => (Color)new ColorConverter().ConvertFromString(ColorHex);
        set => ColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public SetBackgroundColorMethod Method { get; set; } = SetBackgroundColorMethod.Fixed;


    public string Serialize()
    {
        return JsonSerializer.Serialize(this);
    }

    public static ActionButtonSetBackgroundColorActionConfigModel Deserialize(string config)
    {
        return ISerializableConfiguration.Deserialize<ActionButtonSetBackgroundColorActionConfigModel>(config);
    }
}