using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Actions;
// Don't change because of backwards compatibility!

public class ActionButtonSetStateOffAction : PluginAction
{
    public override string Name => LanguageManager.Strings.ActionSetActionButtonStateOff;
    public override string Description => LanguageManager.Strings.ActionSetActionButtonStateOffDescription;

    public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
    {
        if (actionButton.State == false)
        {
            return;
        }

        MacroDeckServer.SetState(actionButton, false);
    }
}
