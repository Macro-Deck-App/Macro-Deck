using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

internal sealed class StubSpotifyHttpClient : IHTTPClient
{
	public required Func<IRequest, IResponse> Handler { get; init; }

	public List<IRequest> Requests { get; } = [];

	public Task<IResponse> DoRequest(IRequest request, CancellationToken cancel)
	{
		Requests.Add(request);
		return Task.FromResult(Handler(request));
	}

	public void SetRequestTimeout(TimeSpan timeout)
	{
	}

	public void Dispose()
	{
	}
}
