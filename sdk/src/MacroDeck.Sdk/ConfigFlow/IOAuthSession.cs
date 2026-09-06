namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// OAuth capability handed to a config flow via <see cref="IConfigFlowContext.OAuth"/>. The host
/// owns the redirect endpoint and correlates the browser callback back to this flow, so providers
/// only build the provider-specific authorize URL and read the resulting authorization code.
/// </summary>
public interface IOAuthSession
{
	/// <summary>The host-owned redirect URI to register with the provider and pass in the authorize URL.</summary>
	string RedirectUri { get; }

	/// <summary>An opaque CSRF/correlation token to include as the <c>state</c> parameter.</summary>
	string State { get; }

	/// <summary>
	/// The authorization code captured from the redirect, or <c>null</c> if the callback has not
	/// arrived yet. Populated by the host before the resume step is submitted.
	/// </summary>
	string? AuthorizationCode { get; }
}
