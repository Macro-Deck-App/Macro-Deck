using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeckHost.Application.Ui.Resources;

public static class HostUiResourceLimits
{
	// Host-rendered icons reach Macro Deck's own clients over HTTP, never a plugin's tree or registration.
	public const int MaxHostIconResourceBytes = ProtocolLimits.MaxAssetBytes;

	public const int MaxHostWidgetTreeBytes = 768 * 1024;
}
