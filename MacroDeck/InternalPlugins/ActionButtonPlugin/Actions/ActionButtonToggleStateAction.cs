using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.ActionButton; // Don't change because of backwards compatibility!



public class ActionButtonToggleStateAction : PluginAction
{
    public override string Name => LanguageManager.Strings.ActionToggleActionButtonState;
    public override string Description => LanguageManager.Strings.ActionToggleActionButtonStateDescription;
    public override void Trigger(string clientId, ActionButton actionButton)
    {
        MacroDeckServer.SetState(actionButton, !actionButton.State);
    }
}