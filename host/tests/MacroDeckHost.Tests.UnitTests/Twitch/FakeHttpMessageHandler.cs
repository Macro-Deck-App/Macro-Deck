using System.Net;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
	private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

	public List<Uri> RequestedUris { get; } = [];

	public List<string> RequestBodies { get; } = [];

	public List<string?> AuthorizationHeaders { get; } = [];

	public FakeHttpMessageHandler Enqueue(HttpStatusCode status, string body)
	{
		_responses.Enqueue((status, body));
		return this;
	}

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		RequestedUris.Add(request.RequestUri!);
		AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
		RequestBodies.Add(request.Content is null
			? string.Empty
			: await request.Content.ReadAsStringAsync(cancellationToken));

		var (status, body) = _responses.Count > 0
			? _responses.Dequeue()
			: (HttpStatusCode.OK, "{}");

		return new HttpResponseMessage(status) { Content = new StringContent(body) };
	}
}
