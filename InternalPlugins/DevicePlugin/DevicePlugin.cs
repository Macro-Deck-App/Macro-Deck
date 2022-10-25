using System.Collections.Generic;
using System.Drawing;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Actions;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin
{
    public class DevicePlugin : MacroDeckPlugin
    {
        internal override string Name => "Device";
        internal override string Author => "Macro Deck";

        internal override Image PluginIcon => Resources.device_manager;

        public override void Enable()
        {
            Actions = new List<PluginAction>
            {
                new SetProfileAction(),
                new SetBrightnessAction()
            };
        }
    }
}
