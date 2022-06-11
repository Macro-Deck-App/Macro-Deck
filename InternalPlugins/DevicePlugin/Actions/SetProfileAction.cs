using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Actions
{
    public class SetProfileAction : PluginAction
    {
        public override string Name => "Set profile";

        public override string Description => "Sets the profile on the device";

        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {

        }
    }
}
