namespace MacroDeckHost.Integrations.Http.Client;

internal interface IHttpRequestClient
{
	Task<HttpSendOutcome> SendAsync(HttpRequestSpec spec, CancellationToken cancellationToken);
}
