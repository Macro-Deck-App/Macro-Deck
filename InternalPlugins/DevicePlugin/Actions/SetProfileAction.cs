using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Models;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Actions
{
    public class SetProfileAction : PluginAction
    {
        public override string Name => LanguageManager.Strings.ActionSetProfile;

        public override string Description => LanguageManager.Strings.ActionSetProfileDescription;

        public override bool CanConfigure => true;

        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {
            var configModel = SetProfileActionConfigModel.Deserialize(this.Configuration);
            MacroDeckDevice macroDeckDevice = DeviceManager.GetMacroDeckDevice(configModel.ClientId);
            MacroDeckProfile profile = ProfileManager.FindProfileById(configModel.ProfileId);
            if (macroDeckDevice == null || profile == null) return;
            DeviceManager.SetProfile(macroDeckDevice, profile);            
        }

        public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
        {
            return new SetProfileActionConfigView(this);
        }
    }
}
