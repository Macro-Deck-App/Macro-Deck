using Serilog;
using SuchByte.MacroDeck.InternalPlugins.Variables.Models;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.ViewModels;

namespace SuchByte.MacroDeck.InternalPlugins.Variables.ViewModels;

public class SaveVariableToFileActionConfigViewModel : ISerializableConfigViewModel
{
    private static readonly ILogger logger = Log.ForContext(typeof(SaveVariableToFileActionConfigViewModel));

    private readonly PluginAction _pluginAction;

    public SaveVariableToFileActionConfigModel Configuration { get; set; }

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

    public SaveVariableToFileActionConfigViewModel(PluginAction pluginAction)
    {
        _pluginAction = pluginAction;
        Configuration = SaveVariableToFileActionConfigModel.Deserialize(pluginAction.Configuration);
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
            logger.Information("config saved");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error while saving config");
        }

        return true;
    }

    public void SetConfig()
    {
        _pluginAction.ConfigurationSummary = $"{Configuration.FilePath}";
        _pluginAction.Configuration = Configuration.Serialize();
    }
}
