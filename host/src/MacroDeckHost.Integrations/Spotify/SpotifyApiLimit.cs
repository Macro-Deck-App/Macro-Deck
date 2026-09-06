using SpotifyAPI.Web;

namespace MacroDeckHost.Integrations.Spotify;

internal enum SpotifyApiLimitKind
{
	RateLimit,

	Quota
}

internal sealed record SpotifyApiLimitStatus(
	SpotifyApiLimitKind Kind,
	DateTimeOffset Since,
	DateTimeOffset PausedUntil);

internal static class SpotifyApiLimits
{
	private static readonly string[] _quotaMarkers =
	[
		"developer.spotify.com",
		"not registered",
		"quota"
	];

	internal static bool IsRateLimited(Exception ex)
		=> ex is APITooManyRequestsException ||
			(ex is APIException { Response: { } response } && (int)response.StatusCode == 429);

	internal static TimeSpan PauseFor(Exception ex, TimeSpan fallback)
	{
		for (var current = ex; current is not null; current = current.InnerException)
		{
			if (current is APITooManyRequestsException { RetryAfter: var retryAfter } && retryAfter > TimeSpan.Zero)
			{
				return retryAfter + TimeSpan.FromSeconds(1);
			}
		}

		return fallback;
	}

	internal static bool IsQuotaExceeded(Exception ex)
	{
		for (var current = ex; current is not null; current = current.InnerException)
		{
			if (current is APIException { Response: { } response } api &&
				(int)response.StatusCode == 403 &&
				MentionsQuota(response.Body as string, api.Message))
			{
				return true;
			}
		}

		return false;
	}

	private static bool MentionsQuota(string? body, string message)
		=> _quotaMarkers.Any(marker =>
			body?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true ||
			message.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
