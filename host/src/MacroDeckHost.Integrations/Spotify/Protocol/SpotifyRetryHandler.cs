using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Integrations.Spotify.Protocol;

internal sealed class SpotifyRetryHandler : IRetryHandler
{
	public Task<IResponse> HandleRetry(
		IRequest request,
		IResponse response,
		IRetryHandler.RetryFunc retry,
		CancellationToken cancel = default)
	{
		return Task.FromResult(response);
	}
}
