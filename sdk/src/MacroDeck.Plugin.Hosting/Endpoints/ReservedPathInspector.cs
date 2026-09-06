using Microsoft.AspNetCore.Routing;

namespace MacroDeck.Plugin.Hosting.Endpoints;

/// <summary>
/// Finds routes an author mapped under the reserved prefix, so <c>Build()</c> can refuse them.
///
/// <para>
/// The runtime middleware would already stop such a route from being served, but silently: the author
/// would see a 404 on a route they can see in their own source. Failing the build says what is wrong
/// once, at the moment it can be fixed.
/// </para>
/// </summary>
internal static class ReservedPathInspector
{
	public static IReadOnlyList<string> FindConflicts(IEnumerable<EndpointDataSource> dataSources)
		=>
		[
			.. dataSources
				.SelectMany(source => source.Endpoints)
				.OfType<RouteEndpoint>()
				.Select(endpoint => endpoint.RoutePattern.RawText)
				.Where(pattern => ReservedPaths.IsReserved(pattern))
				.Where(pattern => !ReservedPaths.All.Any(route
					=> string.Equals(route, Normalize(pattern), StringComparison.OrdinalIgnoreCase)))
				.Select(pattern => $"The route '{pattern}' is under '{ReservedPaths.Prefix}', which the SDK " +
					"reserves for its own endpoints. Map it somewhere else.")
				.Distinct(StringComparer.Ordinal)
		];

	private static string? Normalize(string? pattern)
		=> pattern is null || pattern.StartsWith('/') ? pattern : "/" + pattern;
}
