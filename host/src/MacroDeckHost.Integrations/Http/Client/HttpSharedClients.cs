using System.Collections.Concurrent;
using System.Net;

namespace MacroDeckHost.Integrations.Http.Client;

internal static class HttpSharedClients
{
	private static readonly ConcurrentDictionary<(bool FollowRedirects, bool ValidateTls), HttpClient> _clients = new();

	public static HttpClient Get(bool followRedirects, bool validateTls)
		=> _clients.GetOrAdd((followRedirects, validateTls), CreateClient);

	private static HttpClient CreateClient((bool FollowRedirects, bool ValidateTls) key)
	{
		return IntegrationHttp.CreateClient(Timeout.InfiniteTimeSpan, handler =>
		{
			handler.AllowAutoRedirect = key.FollowRedirects;
			handler.MaxAutomaticRedirections = 10;
			handler.AutomaticDecompression = DecompressionMethods.All;
			handler.ConnectTimeout = TimeSpan.FromSeconds(10);
			handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);

			if (!key.ValidateTls)
			{
#pragma warning disable CA5359
				handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
			}
		});
	}
}
