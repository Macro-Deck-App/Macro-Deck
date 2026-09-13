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
}
