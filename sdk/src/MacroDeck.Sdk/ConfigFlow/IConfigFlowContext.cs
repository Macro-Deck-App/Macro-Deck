namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// Context handed to an <see cref="IConfigFlow"/>. Exposes capabilities a flow may need during
/// setup, such as the <see cref="OAuth"/> session for OAuth-based providers.
/// </summary>
public interface IConfigFlowContext
{
	/// <summary>OAuth redirect/callback support for this flow session.</summary>
	IOAuthSession OAuth { get; }
}
