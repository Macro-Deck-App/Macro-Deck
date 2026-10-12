namespace MacroDeck.Sdk.Http;

/// <summary>What Macro Deck wants outgoing HTTP and WebSocket requests to carry.</summary>
public interface IHostHttp
{
	/// <summary>
	/// The User-Agent the user configured in Macro Deck's network settings, or Macro Deck's default. It can
	/// change while the integration runs, so read it for every request instead of caching it.
	/// </summary>
	string UserAgent { get; }
}
