using MacroDeckHost.Integrations.Http;

namespace MacroDeckHost.Integrations.YtmDesktop.Protocol;

internal static class YtmDesktopHttpClients
{
	private static readonly TimeSpan _fastTimeout = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _slowTimeout = TimeSpan.FromSeconds(40);

	internal static HttpClient Fast { get; } = CreateHttpClient(_fastTimeout);

	internal static HttpClient Slow { get; } = CreateHttpClient(_slowTimeout);

	private static HttpClient CreateHttpClient(TimeSpan timeout)
	{
		var client = IntegrationHttp.CreateClient(timeout);
		client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		return client;
	}
}
