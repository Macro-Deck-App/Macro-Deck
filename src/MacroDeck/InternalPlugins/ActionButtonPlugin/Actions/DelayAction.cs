using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Actions;
// Don't change because of backwards compatibility!

public class DelayAction : PluginAction
{
    public override string Name => "Delay";
    public override string Description => "";

    public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
    {
        try
        {
            Thread.Sleep(int.Parse(Configuration));
        }
        catch
        {
        }
    }
}
