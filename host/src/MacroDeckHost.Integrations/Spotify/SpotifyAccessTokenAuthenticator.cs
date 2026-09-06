using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Integrations.Spotify;

internal sealed class SpotifyAccessTokenAuthenticator : IAuthenticator
{
	private readonly ISpotifyAccessTokenSource _tokens;

	public SpotifyAccessTokenAuthenticator(ISpotifyAccessTokenSource tokens)
	{
		_tokens = tokens;
	}

	public Task Apply(IRequest request, IAPIConnector apiConnector)
	{
		request.Headers["Authorization"] = $"Bearer {_tokens.AccessToken}";
		return Task.CompletedTask;
	}
}
