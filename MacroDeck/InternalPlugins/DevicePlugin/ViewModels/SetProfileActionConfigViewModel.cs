using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Models;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.ViewModels;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.ViewModels;

public class SetProfileActionConfigViewModel : ISerializableConfigViewModel
{
    private readonly PluginAction _action;

    public SetProfileActionConfigModel Configuration { get; set; }

    ISerializableConfiguration ISerializableConfigViewModel.SerializableConfiguration => Configuration;

    public string ClientId
    {
        get => Configuration.ClientId;
        set => Configuration.ClientId = value;
    }

    public string ProfileId
    {
        get => Configuration.ProfileId;
        set => Configuration.ProfileId = value;
    }

    public SetProfileActionConfigViewModel(PluginAction action)
    {
        Configuration = SetProfileActionConfigModel.Deserialize(action.Configuration);
        _action = action;
    }

    public bool SaveConfig()
    {
        try
        {
            SetConfig();
            MacroDeckLogger.Info($"{GetType().Name}: config saved");
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error( $"{GetType().Name}: Error while saving config: { ex.Message + Environment.NewLine + ex.StackTrace }");
            return false;
        }
        return true;
    }

    public void SetConfig()
    {
        _action.ConfigurationSummary = $"{(string.IsNullOrWhiteSpace(ClientId) ? LanguageManager.Strings.WhereExecuted : DeviceManager.GetKnownDevices().Find(x => x.ClientId.Equals(ClientId)).DisplayName)} -> {ProfileManager.FindProfileById(ProfileId).DisplayName}";
        _action.Configuration = Configuration.Serialize();
    }

}