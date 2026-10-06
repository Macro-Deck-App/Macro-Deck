using System.Net;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

internal sealed class YouTubeFakeHttpHandler : HttpMessageHandler
{
	private readonly Queue<Func<HttpResponseMessage>> _responses = new();

	public List<HttpMethod> Methods { get; } = [];

	public List<Uri> RequestedUris { get; } = [];

	public List<string> RequestBodies { get; } = [];

	public List<string?> AuthorizationHeaders { get; } = [];

	public YouTubeFakeHttpHandler Enqueue(HttpStatusCode status, string body)
	{
		_responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body) });
		return this;
	}

	public YouTubeFakeHttpHandler EnqueueFailure(Exception failure)
	{
		_responses.Enqueue(() => throw failure);
		return this;
	}

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		Methods.Add(request.Method);
		RequestedUris.Add(request.RequestUri!);
		AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
		RequestBodies.Add(request.Content is null
			? string.Empty
			: await request.Content.ReadAsStringAsync(cancellationToken));

		return _responses.Count > 0
			? _responses.Dequeue()()
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
	}
}
