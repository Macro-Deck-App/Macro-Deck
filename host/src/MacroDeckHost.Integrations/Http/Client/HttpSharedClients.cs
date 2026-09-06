using System.Collections.Concurrent;
using System.Net;

namespace MacroDeckHost.Integrations.Http.Client;

internal static class HttpSharedClients
{
	private const string UserAgent = "Macro-Deck-Http/1.0";

	private static readonly ConcurrentDictionary<(bool FollowRedirects, bool ValidateTls), HttpClient> _clients = new();

	public static HttpClient Get(bool followRedirects, bool validateTls)
		=> _clients.GetOrAdd((followRedirects, validateTls), CreateClient);

	private static HttpClient CreateClient((bool FollowRedirects, bool ValidateTls) key)
	{
		var handler = new SocketsHttpHandler
		{
			AllowAutoRedirect = key.FollowRedirects,
			MaxAutomaticRedirections = 10,
			AutomaticDecompression = DecompressionMethods.All,
			ConnectTimeout = TimeSpan.FromSeconds(10),
			PooledConnectionLifetime = TimeSpan.FromMinutes(5)
		};

		if (!key.ValidateTls)
		{
#pragma warning disable CA5359
			handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
		}

		var client = new HttpClient(handler)
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
		return client;
	}
}
