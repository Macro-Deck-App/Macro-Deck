using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Actions;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System.Collections.Generic;

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
                new ActionButtonSetBackgroundColorAction(),
            };
        }
    }
}
