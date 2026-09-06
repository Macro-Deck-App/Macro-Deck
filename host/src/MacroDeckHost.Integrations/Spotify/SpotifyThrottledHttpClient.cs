using SpotifyAPI.Web.Http;
using MacroDeck.Sdk.Logging;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Integrations.Spotify;

internal sealed class SpotifyThrottledHttpClient : IHTTPClient
{
	private readonly IHTTPClient _inner;
	private readonly SpotifyRequestLimiter _limiter;
	private readonly ILogger _logger;

	internal SpotifyThrottledHttpClient(IHTTPClient inner, SpotifyRequestLimiter limiter, ILogger? logger = null)
	{
		_inner = inner;
		_limiter = limiter;
		_logger = logger ?? IntegrationLog.For<SpotifyThrottledHttpClient>(SpotifyIntegration.IntegrationId);
	}

	public async Task<IResponse> DoRequest(IRequest request, CancellationToken cancel)
	{
		var context = SpotifyRequestScope.Current;
		var endpoint = SafeEndpoint(request.Endpoint.ToString());
		if (_limiter.Reserve(context.Droppable) is not { } wait)
		{
			_logger.Debug(
				"Spotify request {Method} {Endpoint}: reason={Reason}, source={Source}, category={Category}, limiter=rejected",
				request.Method,
				endpoint,
				context.Reason,
				context.Source,
				context.Category);
			throw new SpotifyThrottledException(
				"Macro Deck skipped a Spotify request to stay under its own rate limit.");
		}

		if (wait > TimeSpan.Zero)
		{
			_logger.Debug(
				"Spotify request {Method} {Endpoint}: reason={Reason}, source={Source}, category={Category}, limiter=delayed, waitMs={WaitMs}",
				request.Method,
				endpoint,
				context.Reason,
				context.Source,
				context.Category,
				(long)wait.TotalMilliseconds);
			await Task.Delay(wait, cancel);
		}

		IResponse response;
		try
		{
			response = await _inner.DoRequest(request, cancel);
		}
		catch (Exception ex) when (MusicPlayerTransientFailure.IsNetworkLevel(ex))
		{
			_logger.Debug(
				"Spotify request {Method} {Endpoint}: reason={Reason}, source={Source}, category={Category}, limiter=passed, networkFailure={Failure}",
				request.Method,
				endpoint,
				context.Reason,
				context.Source,
				context.Category,
				ex.GetType().Name);
			throw;
		}

		var status = (int)response.StatusCode;
		if (status == 429)
		{
			_limiter.NoteRefused();
		}
		else if (status < 500)
		{
			_limiter.NoteAccepted();
		}

		_logger.Write(LevelFor(status),
			"Spotify request {Method} {Endpoint}: reason={Reason}, source={Source}, category={Category}, limiter=passed, status={Status}, retryAfter={RetryAfter}",
			request.Method,
			endpoint,
			context.Reason,
			context.Source,
			context.Category,
			status,
			status == 429 && response.Headers.TryGetValue("Retry-After", out var retryAfter)
				? retryAfter
				: null);

		return response;
	}

	private static LogEventLevel LevelFor(int status)
		=> status switch
		{
			>= 500 => LogEventLevel.Warning,
			>= 400 => LogEventLevel.Information,
			_ => LogEventLevel.Debug
		};

	private static string SafeEndpoint(string endpoint)
	{
		var withoutQuery = endpoint.Split('?', 2)[0];
		var path = Uri.TryCreate(withoutQuery, UriKind.Absolute, out var absolute)
			? absolute.AbsolutePath
			: withoutQuery;
		path = $"/{path.TrimStart('/')}";

		if (path.Contains("/api/token", StringComparison.OrdinalIgnoreCase))
		{
			return "oauth/token";
		}

		if (path.Contains("/me/player/devices", StringComparison.OrdinalIgnoreCase))
		{
			return "player/devices";
		}

		if (path.Contains("/me/player", StringComparison.OrdinalIgnoreCase))
		{
			return "player";
		}

		if (path.Contains("/search", StringComparison.OrdinalIgnoreCase))
		{
			return "catalog/search";
		}

		if (path.Contains("/playlists/", StringComparison.OrdinalIgnoreCase))
		{
			return "library/playlist";
		}

		if (path.Contains("/me/tracks", StringComparison.OrdinalIgnoreCase))
		{
			return "library/tracks";
		}

		if (path.Contains("/me/top/", StringComparison.OrdinalIgnoreCase))
		{
			return "library/top-items";
		}

		if (path.EndsWith("/me", StringComparison.OrdinalIgnoreCase))
		{
			return "profile";
		}

		return "spotify-api";
	}

	public void SetRequestTimeout(TimeSpan timeout) => _inner.SetRequestTimeout(timeout);

	public void Dispose()
	{
	}
}
