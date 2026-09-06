using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Integrations.Spotify;

internal static class SpotifyHttpClients
{
	internal static readonly TimeSpan TokenEndpointTimeout = TimeSpan.FromSeconds(5);

	// The library default is 100s. This only has to stop a genuinely black-holed socket - the poll's own
	// linked token already cuts a slow read - so it deliberately sits well above the 10s poll boundary
	// instead of capping a user-initiated search/library/profile read.
	internal static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(25);

	private static readonly TimeSpan _pooledConnectionLifetime = TimeSpan.FromMinutes(2);

	internal static IHTTPClient Api { get; } = CreateBounded(ApiTimeout);

	internal static IHTTPClient CreateBounded(TimeSpan timeout)
		=> new NetHttpClient(CreatePooled(timeout));

	internal static HttpClient CreatePooled(TimeSpan timeout)
		=> new(new SocketsHttpHandler { PooledConnectionLifetime = _pooledConnectionLifetime })
		{
			Timeout = timeout
		};
}
