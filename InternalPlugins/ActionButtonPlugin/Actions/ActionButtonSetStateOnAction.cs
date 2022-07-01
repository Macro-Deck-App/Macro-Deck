using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.ActionButton // Don't change because of backwards compatibility!
{
    public class ActionButtonSetStateOnAction : PluginAction
    {
        public override string Name => LanguageManager.Strings.ActionSetActionButtonStateOn;
        public override string Description => LanguageManager.Strings.ActionSetActionButtonStateOnDescription;
        public override bool CanConfigure => false;
        public override void Trigger(string clientId, ActionButton actionButton)
        {
            if (actionButton.State == true) return;
            MacroDeckServer.SetState(actionButton, true);
        }
    }
}
