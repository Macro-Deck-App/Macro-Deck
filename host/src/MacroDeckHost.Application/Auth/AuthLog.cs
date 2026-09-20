using Microsoft.Extensions.Logging;

namespace MacroDeckHost.Application.Auth;

internal static partial class AuthLog
{
	[LoggerMessage(EventId = 5600,
		Level = LogLevel.Information,
		Message = "Accepted a retry of refresh token {TokenId} rotated {SecondsAgo:0.#}s ago (user {UserId}, " +
			"device {DeviceId}) instead of treating it as reuse.")]
	public static partial void RotationRetryAccepted(ILogger logger,
		Guid tokenId,
		double secondsAgo,
		Guid userId,
		Guid? deviceId);

	[LoggerMessage(EventId = 5601,
		Level = LogLevel.Warning,
		Message = "Refresh token {TokenId} was presented after it had been rotated out: revoked the {Revoked} " +
			"live token(s) of family {FamilyId} (user {UserId}, device {DeviceId}). Other sessions of the " +
			"account are untouched.")]
	public static partial void FamilyRevokedAsReuse(ILogger logger,
		Guid tokenId,
		Guid familyId,
		int revoked,
		Guid userId,
		Guid? deviceId);
}
