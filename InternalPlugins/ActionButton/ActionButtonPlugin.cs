using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.ActionButton // Don't change because of backwards compatibility!
{
    public class ActionButtonPlugin : MacroDeckPlugin
    {
        internal override string Name => LanguageManager.Strings.PluginActionButton;
        internal override string Author => "Macro Deck";
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
        public override string Name => LanguageManager.Strings.ActionToggleActionButtonState;
        public override string Description => LanguageManager.Strings.ActionToggleActionButtonStateDescription;
        public override void Trigger(string clientId, ActionButton actionButton)
        {
            MacroDeckServer.SetState(actionButton, !actionButton.State);
        }
    }

    public class ActionButtonSetStateOffAction : PluginAction
    {
        public override string Name => LanguageManager.Strings.ActionSetActionButtonStateOff;
        public override string Description => LanguageManager.Strings.ActionSetActionButtonStateOffDescription;
        public override void Trigger(string clientId, ActionButton actionButton)
        {
            MacroDeckServer.SetState(actionButton, false);
        }
    }

    public class ActionButtonSetStateOnAction : PluginAction
    {
        public override string Name => LanguageManager.Strings.ActionSetActionButtonStateOn;
        public override string Description => LanguageManager.Strings.ActionSetActionButtonStateOnDescription;
        public override bool CanConfigure => false;
        public override void Trigger(string clientId, ActionButton actionButton)
        {
            MacroDeckServer.SetState(actionButton, true);
        }
    }
}
