namespace MacroDeck.Plugin.Protocol.Errors;

/// <summary>
/// Every error code v1 speaks. <c>public const string</c> with a PascalCase name and a SCREAMING_SNAKE
/// value - naming the field after the value would trip CA1707, and this project carries no
/// <c>NoWarn</c>. Append-only within a major: removing or renaming a code requires the current
/// protocol version to advance.
/// </summary>
public static class ProtocolErrorCodes
{
	public const string ProtocolVersionUnsupported = "PROTOCOL_VERSION_UNSUPPORTED";

	public const string UnknownMessageType = "UNKNOWN_MESSAGE_TYPE";

	public const string MalformedEnvelope = "MALFORMED_ENVELOPE";

	public const string InvalidPayload = "INVALID_PAYLOAD";

	public const string Unauthenticated = "UNAUTHENTICATED";

	public const string PluginAlreadyRegistered = "PLUGIN_ALREADY_REGISTERED";

	public const string SessionExpired = "SESSION_EXPIRED";

	public const string SessionNotResumable = "SESSION_NOT_RESUMABLE";

	public const string SessionReplaced = "SESSION_REPLACED";

	// Added for the ui host api: a provider pushing a tree, patch or fault for a session the host is not
	// brokering for it needs to be told which of its own sessions is gone, and CAPABILITY_UNAVAILABLE
	// would say the whole capability went away instead.
	public const string SessionNotFound = "SESSION_NOT_FOUND";

	public const string CapabilityUnsupported = "CAPABILITY_UNSUPPORTED";

	public const string CapabilityUnavailable = "CAPABILITY_UNAVAILABLE";

	public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

	public const string AssetTooLarge = "ASSET_TOO_LARGE";

	public const string QueueOverflow = "QUEUE_OVERFLOW";

	public const string RateLimited = "RATE_LIMITED";

	public const string Timeout = "TIMEOUT";

	public const string Cancelled = "CANCELLED";

	public const string CorrelationUnknown = "CORRELATION_UNKNOWN";

	public const string DuplicateIdempotencyKey = "DUPLICATE_IDEMPOTENCY_KEY";

	public const string InternalError = "INTERNAL_ERROR";

	/// <summary>An <c>adb</c> call refused because ADB is switched off in Macro Deck.</summary>
	public const string AdbNotEnabled = "ADB_NOT_ENABLED";

	/// <summary>An <c>adb</c> call refused because ADB is on but this plugin may not use it.</summary>
	public const string AdbNotAllowed = "ADB_NOT_ALLOWED";

	/// <summary>An <c>adb</c> call that adb or the device could not carry out. The details'
	/// <c>reason</c> names why, from the <c>adb_</c> values of <see cref="ProtocolErrorReasons" />.</summary>
	public const string AdbFailed = "ADB_FAILED";

	/// <summary>A <c>ui</c>/<c>register-resource</c> refused because it would take the plugin past
	/// <see cref="Limits.ProtocolLimits.MaxUiResourceBytesPerPlugin" /> or
	/// <see cref="Limits.ProtocolLimits.MaxUiResourcesPerPlugin" />. The resource previously registered
	/// under that name, if any, is left as it was.</summary>
	public const string UiResourceQuotaExceeded = "UI_RESOURCE_QUOTA_EXCEEDED";

	/// <summary>An <c>icon-packs</c>/<c>get-icon-resource</c> naming a key or icon name the calling plugin's
	/// bundled packs do not contain.</summary>
	public const string PluginIconNotFound = "PLUGIN_ICON_NOT_FOUND";

	/// <summary>An <c>icon-packs</c>/<c>sync-bundled</c> whose uploaded archive is not a usable icon pack. The
	/// message names the key; packs already installed under that key are left as they were.</summary>
	public const string IconPackInvalid = "ICON_PACK_INVALID";

	/// <summary>An <c>icon-packs</c>/<c>sync-bundled</c> from a session that is not self-registered. Installed
	/// plugins get their bundled packs from their artifact.</summary>
	public const string IconPackSyncNotAllowed = "ICON_PACK_SYNC_NOT_ALLOWED";

	public static readonly IReadOnlyList<string> All =
	[
		ProtocolVersionUnsupported, UnknownMessageType, MalformedEnvelope, InvalidPayload, Unauthenticated,
		PluginAlreadyRegistered, SessionExpired, SessionNotResumable, SessionReplaced, SessionNotFound,
		CapabilityUnsupported,
		CapabilityUnavailable, PayloadTooLarge, AssetTooLarge, QueueOverflow, RateLimited,
		Timeout, Cancelled, CorrelationUnknown, DuplicateIdempotencyKey, InternalError,
		AdbNotEnabled, AdbNotAllowed, AdbFailed, UiResourceQuotaExceeded,
		PluginIconNotFound, IconPackInvalid, IconPackSyncNotAllowed,
	];
}
