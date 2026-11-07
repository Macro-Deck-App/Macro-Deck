using MacroDeck.Sdk.Decks;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class DeckNavigatorAccessor
{
	public IDeckNavigator? Current { get; set; }
}
