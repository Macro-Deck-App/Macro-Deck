using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Arguments for <c>host.invoke</c> against <see cref="HostApis.Devices" />'s <c>register</c> and
/// <c>update</c> operations. The providing plugin is never named here - the host takes it from the
/// authenticated session, so one plugin cannot register a device in another's name.
/// </summary>
public sealed record DevicesRegisterArguments
{
	public required DeviceDescriptorDto Device { get; init; }
}

/// <summary>Result of the <c>register</c> operation: the identity the host assigned.</summary>
public sealed record DevicesRegisterResult
{
	/// <summary>The host's global device id.</summary>
	public required string DeviceId { get; init; }

	/// <summary>The provider-local id the device was registered under.</summary>
	public required string ProviderDeviceId { get; init; }
}

/// <summary>Arguments for the <c>presence</c> operation.</summary>
public sealed record DevicesPresenceArguments
{
	/// <summary>The provider-local device id.</summary>
	public required string DeviceId { get; init; }

	/// <summary>One of the SDK's <c>DevicePresence</c> member names.</summary>
	public required string Presence { get; init; }
}

/// <summary>Arguments for the <c>unregister</c> operation.</summary>
public sealed record DevicesUnregisterArguments
{
	/// <summary>The provider-local device id.</summary>
	public required string DeviceId { get; init; }
}

/// <summary>Arguments for the <c>interaction</c> operation, reporting a hardware interaction from an
/// open device session.</summary>
public sealed record DevicesInteractionArguments
{
	public required string SessionId { get; init; }

	/// <summary>One of the SDK's <c>DeviceInteractionKind</c> member names.</summary>
	public required string Kind { get; init; }

	/// <summary>Must come from the surface the provider is currently rendering - the host rejects an id
	/// that is not on the device's current surface.</summary>
	public string? WidgetId { get; init; }

	public int? ControlIndex { get; init; }

	public double? Value { get; init; }

	public long? SurfaceRevision { get; init; }

	public IReadOnlyDictionary<string, string> Data { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Result of the <c>interaction</c> operation. An interaction whose kind is part of the contract but
/// has no widget model yet comes back accepted and <see cref="Unsupported" /> rather than as an error:
/// the session stays open and the provider learns that nothing ran.
/// </summary>
public sealed record DevicesInteractionResult
{
	public required bool Accepted { get; init; }

	public required bool Unsupported { get; init; }

	/// <summary>One of the SDK's <c>DeviceSessionReasons</c> codes, present exactly when
	/// <see cref="Accepted" /> is false. Never localized text.</summary>
	public string? ReasonCode { get; init; }
}

/// <summary>Arguments for the <c>close</c> operation, ending an open device session from the
/// provider's side.</summary>
public sealed record DevicesCloseArguments
{
	public required string SessionId { get; init; }
}

/// <summary>Arguments for the <c>icon</c> operation, fetching icon bytes referenced by a device's
/// current surface.</summary>
public sealed record DevicesIconArguments
{
	public required string SessionId { get; init; }

	public required string IconId { get; init; }

	public int? Size { get; init; }

	/// <summary>An ETag the caller already has cached; the host may reply with <c>NotModified</c> instead
	/// of retransmitting the bytes.</summary>
	public string? KnownETag { get; init; }
}

/// <summary>
/// Result of the <c>icon</c> operation - metadata only, never the bytes, exactly as
/// <see cref="MacroDeck.Plugin.Protocol.Capabilities.Icons.IconsDescribePayload" /> documents for the
/// plugin-to-host direction. The bytes travel separately over the host-to-plugin
/// <c>host.asset.*</c> pipeline (<see cref="MacroDeck.Plugin.Protocol.Assets.HostAssetBeginPayload" />
/// and friends), pushed by the host once this result is returned.
/// </summary>
public sealed record DevicesIconResult
{
	public required string ContentType { get; init; }

	/// <summary>Must match the <c>contentHash</c> the host declares in the matching <c>host.asset.begin</c>
	/// - see <see cref="MacroDeck.Plugin.Protocol.Assets.AssetContentHash" />.</summary>
	public required string ETag { get; init; }

	public required int ByteLength { get; init; }

	/// <summary>Absent exactly when <see cref="NotModified" /> is true - there are no bytes to hash.</summary>
	public string? ContentHash { get; init; }

	/// <summary>True when <c>knownETag</c> still matches and no transfer follows.</summary>
	public required bool NotModified { get; init; }

	/// <summary>Correlates this result with the <c>host.asset.*</c> transfer that follows it. Absent when
	/// <see cref="NotModified" /> is true.</summary>
	public string? TransferId { get; init; }
}

/// <summary>Arguments for the <c>widget-icon</c> operation, fetching the bytes behind a widget's
/// currently rendered action-icon-provider icon.</summary>
public sealed record DevicesWidgetIconArguments
{
	public required string SessionId { get; init; }

	/// <summary>Must come from the surface the provider is currently rendering - the host rejects an id
	/// that is not on the device's current surface.</summary>
	public required string WidgetId { get; init; }

	/// <summary>An ETag the caller already has cached; the host may reply with <c>NotModified</c> instead
	/// of retransmitting the bytes.</summary>
	public string? KnownETag { get; init; }
}

/// <summary>
/// Result of the <c>widget-icon</c> operation - metadata only, never the bytes, exactly as
/// <see cref="DevicesIconResult" /> documents for the icon-pack case. The bytes travel separately over
/// the host-to-plugin <c>host.asset.*</c> pipeline, pushed by the host once this result is returned.
/// </summary>
public sealed record DevicesWidgetIconResult
{
	public required string ContentType { get; init; }

	/// <summary>Must match the <c>contentHash</c> the host declares in the matching <c>host.asset.begin</c>
	/// - see <see cref="MacroDeck.Plugin.Protocol.Assets.AssetContentHash" />.</summary>
	public required string ETag { get; init; }

	public required int ByteLength { get; init; }

	/// <summary>Absent exactly when <see cref="NotModified" /> is true - there are no bytes to hash.</summary>
	public string? ContentHash { get; init; }

	/// <summary>True when <c>knownETag</c> still matches and no transfer follows.</summary>
	public required bool NotModified { get; init; }

	/// <summary>Correlates this result with the <c>host.asset.*</c> transfer that follows it. Absent when
	/// <see cref="NotModified" /> is true.</summary>
	public string? TransferId { get; init; }
}
