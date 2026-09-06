using MacroDeck.Plugin.Testing.Conformance;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>Kebab-case command-line spellings for <see cref="ConformanceCategory" />, e.g.
/// <c>disconnect-and-reconnect</c> for <see cref="ConformanceCategory.DisconnectAndReconnect" /> - the
/// PascalCase member names themselves are not friendly command-line tokens.</summary>
internal static class ConformanceCategoryTokens
{
	private static readonly IReadOnlyDictionary<string, ConformanceCategory> _byToken =
		new Dictionary<string, ConformanceCategory>(StringComparer.OrdinalIgnoreCase)
		{
			["manifest-and-identifiers"] = ConformanceCategory.ManifestAndIdentifiers,
			["registration-and-negotiation"] = ConformanceCategory.RegistrationAndNegotiation,
			["capability-serialization"] = ConformanceCategory.CapabilitySerialization,
			["duplicate-ids"] = ConformanceCategory.DuplicateIds,
			["timeout-and-cancellation"] = ConformanceCategory.TimeoutAndCancellation,
			["disconnect-and-reconnect"] = ConformanceCategory.DisconnectAndReconnect,
			["health-endpoint"] = ConformanceCategory.HealthEndpoint,
			["bounded-queues"] = ConformanceCategory.BoundedQueues
		};

	public static IReadOnlyCollection<string> Tokens => (IReadOnlyCollection<string>)_byToken.Keys;

	public static bool TryParse(string token, out ConformanceCategory category)
		=> _byToken.TryGetValue(token, out category);
}
