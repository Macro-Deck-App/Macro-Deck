namespace MacroDeckHost.Integrations.Http.Protocol;

internal interface IHttpRequestClient
{
	Task<HttpSendOutcome> SendAsync(HttpRequestSpec spec, CancellationToken cancellationToken);
}
