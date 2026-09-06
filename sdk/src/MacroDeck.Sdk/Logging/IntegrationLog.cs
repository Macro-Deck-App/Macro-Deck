using Serilog;

namespace MacroDeck.Sdk.Logging;

/// <summary>
/// Per-integration logger factory. An integration logs through its own logger, stamped with its
/// id, instead of writing into the host's log stream: sinks (and the log viewer) can then group
/// entries per integration, and a misbehaving integration is attributable without reading the
/// namespace off a source context.
/// </summary>
public static class IntegrationLog
{
	/// <summary>
	/// Serilog property carrying the integration id on every event an integration logger writes.
	/// Log sinks read it to attribute an entry to its integration.
	///
	/// Deliberately not the obvious "Integration": host-side messages use that as an ordinary
	/// template property (<c>"Registered {Count} variable(s) for integration '{Integration}'"</c>),
	/// and a sink cannot tell such a property from this marker - the host line would then be filed
	/// under the integration that did not log it.
	/// </summary>
	public const string IntegrationPropertyName = "MacroDeckIntegrationId";

	/// <summary>The logger of the integration with the given id.</summary>
	public static ILogger For(string integrationId)
		=> Log.ForContext(IntegrationPropertyName, integrationId);

	/// <summary>The logger of the given integration, scoped to the source type that writes to it.</summary>
	public static ILogger For<TSource>(string integrationId)
		=> For(integrationId).ForContext<TSource>();

	/// <summary>
	/// The logger of the given integration, scoped to a source type that is only known at runtime
	/// (a base class logging on behalf of its implementations).
	/// </summary>
	public static ILogger For(string integrationId, Type source)
		=> For(integrationId).ForContext(source);
}
