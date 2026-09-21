using System.Collections.Concurrent;
using System.Security.Claims;

namespace MacroDeckHost.Application.Auth;

// Whether an access token naming a device may still be used. Seeded from the device rows at startup, so
// signing a device out or deleting it takes effect on the next request rather than when the token expires.
public sealed class DeviceSessionGuard
{
	private static readonly DateTime _never = DateTime.MinValue;

	private readonly ConcurrentDictionary<Guid, DateTime> _revokedAt = new();

	public void Seed(IEnumerable<(Guid Id, DateTime? RevokedAt)> devices)
	{
		ArgumentNullException.ThrowIfNull(devices);

		_revokedAt.Clear();
		foreach (var (id, revokedAt) in devices)
		{
			_revokedAt[id] = revokedAt ?? _never;
		}
	}

	public void Track(Guid deviceId, DateTime? revokedAt = null)
		=> _revokedAt[deviceId] = revokedAt ?? _never;

	public void Revoke(Guid deviceId, DateTime revokedAt) => _revokedAt[deviceId] = revokedAt;

	public void Forget(Guid deviceId) => _revokedAt.TryRemove(deviceId, out _);

	// Same one-second problem AccessTokenCutoff has: a token minted in the very second a device was
	// signed out would be refused for its whole life, so its iat is pushed past the revocation.
	public DateTime IssuedAtFor(Guid deviceId, DateTime candidate)
	{
		if (!_revokedAt.TryGetValue(deviceId, out var revokedAt) || revokedAt == _never)
		{
			return candidate;
		}

		var firstAccepted = DateTime.UnixEpoch.AddSeconds(AccessTokenIssuedAt.UnixSeconds(revokedAt) + 1);

		return candidate > firstAccepted ? candidate : firstAccepted;
	}

	public bool Rejects(ClaimsPrincipal principal)
	{
		ArgumentNullException.ThrowIfNull(principal);

		var claim = principal.FindFirst(AuthDefaults.DeviceClaim)?.Value;
		if (string.IsNullOrEmpty(claim))
		{
			return false;
		}

		// A device claim the host cannot resolve is refused rather than ignored: an unparsable id and a
		// device that was deleted both mean this token no longer names a session the host still keeps.
		if (!Guid.TryParse(claim, out var deviceId) || !_revokedAt.TryGetValue(deviceId, out var revokedAt))
		{
			return true;
		}

		if (revokedAt == _never)
		{
			return false;
		}

		// A revoked device plus a token the host cannot date is refused: failing open here would let a
		// token without a readable iat outlive the sign-out it was issued before.
		return !AccessTokenIssuedAt.TryRead(principal, out var issuedAt) ||
			issuedAt <= AccessTokenIssuedAt.UnixSeconds(revokedAt);
	}
}
