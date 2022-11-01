using System;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables.Plugin.Models;
using SuchByte.MacroDeck.ViewModels;

namespace SuchByte.MacroDeck.Variables.Plugin.ViewModels;

public class ReadVariableFromFileActionConfigViewModel : ISerializableConfigViewModel
{
    private readonly PluginAction _pluginAction;

    public ReadVariableFromFileActionConfigModel Configuration { get; set; }

    ISerializableConfiguration ISerializableConfigViewModel.SerializableConfiguration => Configuration;

    public string FilePath
    {
        get => Configuration.FilePath;
        set => Configuration.FilePath = value;
    }

    public string Variable
    {
        get => Configuration.Variable;
        set => Configuration.Variable = value;
    }

    public ReadVariableFromFileActionConfigViewModel(PluginAction pluginAction)
    {
        _pluginAction = pluginAction;
        Configuration = ReadVariableFromFileActionConfigModel.Deserialize(pluginAction.Configuration);
    }

    public bool SaveConfig()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || string.IsNullOrWhiteSpace(Variable))
        {
            return false;
        }
        try
        {
            SetConfig();
            MacroDeckLogger.Info(typeof(ReadVariableFromFileActionConfigModel), "config saved");
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(typeof(ReadVariableFromFileActionConfigModel), $"Error while saving config: { ex.Message + Environment.NewLine + ex.StackTrace }");
        }
        return true;
    }

    public void SetConfig()
    {
        _pluginAction.ConfigurationSummary = $"{ Configuration.FilePath }";
        _pluginAction.Configuration = Configuration.Serialize();
    }
}