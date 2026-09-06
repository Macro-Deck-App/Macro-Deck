namespace MacroDeck.Sdk.Devices;

/// <summary>
/// The bytes and metadata for one widget's provider-owned icon, as returned by
/// <see cref="IDeviceSession.GetWidgetIconAsync" />. A sibling of <see cref="DeviceIconImage" /> rather
/// than a reuse of it: that type is addressed by an icon-pack id, which a provider-owned icon does not
/// have - see <see cref="DeviceSurfaceAppearance.HasProviderIcon" />.
/// </summary>
public sealed record DeviceWidgetIconImage
{
	public required string WidgetId { get; init; }

	public required string ContentType { get; init; }

	public required string ETag { get; init; }

	/// <summary>Empty when <see cref="NotModified" /> is true.</summary>
	public required ReadOnlyMemory<byte> Content { get; init; }

	/// <summary>True when the caller's <c>knownETag</c> still matches and the host skipped the transfer.</summary>
	public required bool NotModified { get; init; }
}
