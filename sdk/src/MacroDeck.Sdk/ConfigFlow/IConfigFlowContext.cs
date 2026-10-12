using MacroDeck.Sdk.Http;

namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// Context handed to an <see cref="IConfigFlow"/>. Exposes capabilities a flow may need during
/// setup, such as the <see cref="OAuth"/> session for OAuth-based providers.
/// </summary>
public interface IConfigFlowContext
{
	/// <summary>OAuth redirect/callback support for this flow session.</summary>
	IOAuthSession OAuth { get; }

	/// <summary>
	/// What outgoing HTTP and WebSocket requests should carry while the flow runs, the same as
	/// <see cref="IIntegrationContext.Http" />. Available before the integration was ever initialized. A
	/// context from a Macro Deck that does not send it, and any implementation that does not override this
	/// member, answer <see cref="DefaultHostHttp" />.
	/// </summary>
	IHostHttp Http => DefaultHostHttp.Instance;
}
