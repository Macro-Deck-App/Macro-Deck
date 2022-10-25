using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Models;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Actions
{
    public class SetBrightnessAction : PluginAction
    {
        public override string Name => LanguageManager.Strings.ActionSetBrightness;

        public override string Description => LanguageManager.Strings.ActionSetBrightnessDescription;

        public override bool CanConfigure => true;

        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {
            var configModel = SetBrightnessActionConfigModel.Deserialize(Configuration);
            if (clientId == "" || clientId == "-1") return;
            MacroDeckDevice macroDeckDevice;
            if (string.IsNullOrWhiteSpace(configModel.ClientId))
            {
                macroDeckDevice = DeviceManager.GetMacroDeckDevice(clientId);
            }
            else
            {
                macroDeckDevice = DeviceManager.GetMacroDeckDevice(configModel.ClientId);
            }
            if (macroDeckDevice == null || !macroDeckDevice.Available || (macroDeckDevice.DeviceType != DeviceType.Android && macroDeckDevice.DeviceType != DeviceType.iOS)) return;
            macroDeckDevice.Configuration.Brightness = configModel.Brightness;

            DeviceManager.SaveKnownDevices();
            var macroDeckClient = MacroDeckServer.GetMacroDeckClient(macroDeckDevice.ClientId);
            macroDeckClient?.DeviceMessage.SendConfiguration(macroDeckClient);
        }

        public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
        {
            return new SetBrightnessActionConfigView(this);
        }
    }
}
