namespace MacroDeck.Sdk.Events;

/// <summary>
/// Implemented by integrations that raise events. The host enumerates providers across all enabled
/// integrations, namespaces every definition id as <c>integrationId::eventId</c>, and serves the
/// merged catalogue to the trigger editor - the same capability-interface-by-registry shape as
/// <c>IMusicPlayerProvider</c> and <c>IWeatherProvider</c>.
///
/// A provider only declares what it can raise; publishing goes through the
/// <see cref="IEventPublisher" /> the host hands the integration in its context.
/// </summary>
public interface IEventProvider
{
	/// <summary>Human-readable provider name shown in the event picker, e.g. "OBS Studio".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name - so the one place a name is stated stays
	/// the integration. Stating a name here still wins, which is what an integration exposing one or
	/// more distinctly-branded providers needs.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>
	/// The events this integration can raise. Read whenever the catalogue is built, so a provider
	/// whose event set depends on its configuration may return a different list after a reconfigure.
	/// </summary>
	IReadOnlyList<EventDefinition> EventDefinitions { get; }
}
