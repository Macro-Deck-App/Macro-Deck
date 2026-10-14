namespace MacroDeckHost.Application.Network.Http;

public sealed class UserAgentHandler : DelegatingHandler
{
	private readonly IHttpUserAgentSource _source;

	public UserAgentHandler(IHttpUserAgentSource source, HttpMessageHandler? inner = null)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
		if (inner is not null)
		{
			InnerHandler = inner;
		}
	}

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		if (request.Headers.UserAgent.Count == 0)
		{
			request.Headers.TryAddWithoutValidation("User-Agent", _source.Current);
		}

		return base.SendAsync(request, cancellationToken);
	}
}
