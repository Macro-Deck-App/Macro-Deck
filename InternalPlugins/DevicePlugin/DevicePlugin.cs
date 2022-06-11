using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Actions;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin
{
    public class DevicePlugin : MacroDeckPlugin
    {
        internal override string Name => "Device";
        internal override string Author => "Macro Deck";

        internal override Image PluginIcon => Properties.Resources.device_manager;

        public override void Enable()
        {
            this.Actions = new List<PluginAction>()
            {
                new SetProfileAction(),
            };
        }
    }
}
