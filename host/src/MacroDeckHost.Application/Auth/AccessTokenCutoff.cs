using System.Globalization;
using System.Security.Claims;

namespace MacroDeckHost.Application.Auth;

public sealed class AccessTokenCutoff
{
	private const string IssuedAtClaim = "iat";
	private const long Unset = long.MinValue;

	private long _cutoffSeconds = Unset;

	public void Set(DateTime utcNow) => Interlocked.Exchange(ref _cutoffSeconds, UnixSeconds(utcNow));

	public DateTime IssuedAtFor(DateTime utcNow)
	{
		var cutoff = Interlocked.Read(ref _cutoffSeconds);
		if (cutoff == Unset)
		{
			return utcNow;
		}

		var firstAccepted = DateTime.UnixEpoch.AddSeconds(cutoff + 1);
		return utcNow > firstAccepted ? utcNow : firstAccepted;
	}

	public bool Rejects(ClaimsPrincipal principal)
	{
		var cutoff = Interlocked.Read(ref _cutoffSeconds);
		return cutoff != Unset &&
			long.TryParse(principal.FindFirst(IssuedAtClaim)?.Value,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var issuedAt) &&
			issuedAt <= cutoff;
	}

	private static long UnixSeconds(DateTime utc) => (long)Math.Floor((utc - DateTime.UnixEpoch).TotalSeconds);
}
