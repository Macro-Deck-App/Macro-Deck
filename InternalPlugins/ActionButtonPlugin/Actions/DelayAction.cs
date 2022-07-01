using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SuchByte.MacroDeck.ActionButton.Plugin // Don't change because of backwards compatibility!
{
    public class DelayAction : PluginAction
    {
        public override string Name => "Delay";
        public override string Description => "";
        public override void Trigger(string clientId, ActionButton actionButton)
        {
            try
            {
                Thread.Sleep(int.Parse(this.Configuration));
            } catch { }
        }
    }
}
