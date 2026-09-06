using System.Globalization;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeckHost.Application.Plugins;

public static class PluginErrors
{
	public static ProtocolError Unauthenticated() => new()
	{
		Code = ProtocolErrorCodes.Unauthenticated,
		Message = ProtocolErrorMessages.For(ProtocolErrorCodes.Unauthenticated),
		Retryable = false
	};

	public static ProtocolError Forbidden() => new()
	{
		Code = ProtocolErrorCodes.Unauthenticated,
		Message = ProtocolErrorMessages.For(ProtocolErrorCodes.Unauthenticated),
		Retryable = false
	};

	/// <summary>
	/// Refusal because Developer Mode is switched off. Reuses the generic unauthenticated code so a
	/// client compiled against an older SDK still handles it; the <c>reason</c> detail is what lets a
	/// newer client name the setting instead of reporting a bare 403.
	/// </summary>
	public static ProtocolError DeveloperModeDisabled() => new()
	{
		Code = ProtocolErrorCodes.Unauthenticated,
		Message = ProtocolErrorMessages.For(ProtocolErrorCodes.Unauthenticated),
		Details = ProtocolDiagnostics.Redact(new Dictionary<string, string>
		{
			["reason"] = ProtocolErrorReasons.DeveloperModeDisabled
		}),
		Retryable = false
	};

	public static ProtocolError RateLimited(TimeSpan retryAfter) => new()
	{
		Code = ProtocolErrorCodes.RateLimited,
		Message = ProtocolErrorMessages.For(ProtocolErrorCodes.RateLimited),
		Details = ProtocolDiagnostics.Redact(new Dictionary<string, string>
		{
			["retryAfterSeconds"] = Math.Max(0, (int)Math.Ceiling(retryAfter.TotalSeconds))
				.ToString(CultureInfo.InvariantCulture)
		}),
		Retryable = true
	};

	public static ProtocolError InvalidPayload(IReadOnlyDictionary<string, string>? details = null) => new()
	{
		Code = ProtocolErrorCodes.InvalidPayload,
		Message = ProtocolErrorMessages.For(ProtocolErrorCodes.InvalidPayload),
		Details = ProtocolDiagnostics.Redact(details),
		Retryable = false
	};

	public static ProtocolError AlreadyRegistered() => new()
	{
		Code = ProtocolErrorCodes.PluginAlreadyRegistered,
		Message = ProtocolErrorMessages.For(ProtocolErrorCodes.PluginAlreadyRegistered),
		Retryable = false
	};

	public static ProtocolError VersionUnsupported(ProtocolVersionRange hostRange) => new()
	{
		Code = ProtocolErrorCodes.ProtocolVersionUnsupported,
		Message = ProtocolErrorMessages.For(ProtocolErrorCodes.ProtocolVersionUnsupported),
		Details = ProtocolDiagnostics.Redact(new Dictionary<string, string>
		{
			["supportedMinimum"] = hostRange.Minimum.ToString(CultureInfo.InvariantCulture),
			["supportedMaximum"] = hostRange.Maximum.ToString(CultureInfo.InvariantCulture)
		}),
		Retryable = false
	};
}
