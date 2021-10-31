using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.ActionButton
{
    public class ActionButtonPlugin : MacroDeckPlugin
    {
        public override string Name => "ActionButton";
        public override string Author => "Macro Deck";
        public override string Description => "";
        public override void Enable()
        {
            this.Actions = new List<PluginAction>()
            {
                new ActionButtonToggleStateAction(),
                new ActionButtonSetStateOffAction(),
                new ActionButtonSetStateOnAction(),
            };
        }
    }


    public class ActionButtonToggleStateAction : PluginAction
    {
        public override string Name => "Toggle state";
        public override string Description => "Toggles the state of this button";
        public override string DisplayName { get; set; } = "Toggle state";
        public override void Trigger(string clientId, ActionButton actionButton)
        {
            MacroDeckServer.SetState(actionButton, !actionButton.State);
        }
    }

    public class ActionButtonSetStateOffAction : PluginAction
    {
        public override string Name => "Set button state off";
        public override string Description => "Set the state of the button to off";
        public override string DisplayName { get; set; } = "Set button state off";
        public override void Trigger(string clientId, ActionButton actionButton)
        {
            MacroDeckServer.SetState(actionButton, false);
        }
    }

    public class ActionButtonSetStateOnAction : PluginAction
    {
        public override string Name => "Set button state on";
        public override string Description => "Set the state of the button to on";
        public override string DisplayName { get; set; } = "Set button state on";
        public override bool CanConfigure => false;
        public override void Trigger(string clientId, ActionButton actionButton)
        {
            MacroDeckServer.SetState(actionButton, true);
        }
    }
}
