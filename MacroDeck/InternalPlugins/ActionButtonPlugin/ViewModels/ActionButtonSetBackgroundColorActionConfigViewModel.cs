using System;
using System.Drawing;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Enums;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Models;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.ViewModels;

namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.ViewModels;

public class ActionButtonSetBackgroundColorActionConfigViewModel : ISerializableConfigViewModel
{
    private readonly PluginAction _pluginAction;

    public ActionButtonSetBackgroundColorActionConfigModel Configuration { get; set; }

    ISerializableConfiguration ISerializableConfigViewModel.SerializableConfiguration => Configuration;

    public Color Color
    {
        get => Configuration.Color;
        set => Configuration.Color = value;
    }

    public SetBackgroundColorMethod Method
    {
        get => Configuration.Method;
        set => Configuration.Method = value;
    }


    public ActionButtonSetBackgroundColorActionConfigViewModel(PluginAction pluginAction)
    {
        _pluginAction = pluginAction;
        Configuration = ActionButtonSetBackgroundColorActionConfigModel.Deserialize(pluginAction.Configuration);
    }

    public bool SaveConfig()
    {
        try
        {
            SetConfig();
            MacroDeckLogger.Info(typeof(ActionButtonSetBackgroundColorActionConfigViewModel), "config saved");
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(typeof(ActionButtonSetBackgroundColorActionConfigViewModel), $"Error while saving config: { ex.Message + Environment.NewLine + ex.StackTrace }");
        }
        return true;
    }

    public void SetConfig()
    {
        _pluginAction.ConfigurationSummary = Method switch
        {
            SetBackgroundColorMethod.Fixed => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}",
            SetBackgroundColorMethod.Random => LanguageManager.Strings.Random,
            _ => "",
        };
        _pluginAction.Configuration = Configuration.Serialize();
    }
}