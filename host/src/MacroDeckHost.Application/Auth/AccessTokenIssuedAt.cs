using System.Globalization;
using System.Security.Claims;

namespace MacroDeckHost.Application.Auth;

public static class AccessTokenIssuedAt
{
	public const string Claim = "iat";

	public static bool TryRead(ClaimsPrincipal principal, out long unixSeconds)
	{
		ArgumentNullException.ThrowIfNull(principal);

		return long.TryParse(principal.FindFirst(Claim)?.Value,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out unixSeconds);
	}

	public static long UnixSeconds(DateTime utc) => (long)Math.Floor((utc - DateTime.UnixEpoch).TotalSeconds);
}
