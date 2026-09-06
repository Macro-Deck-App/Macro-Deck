namespace MacroDeck.Sdk.Devices;

/// <summary>The bytes and metadata for one icon, as returned by <see cref="IDeviceSession.GetIconAsync" />.</summary>
public sealed record DeviceIconImage
{
	public required string IconId { get; init; }

	public required string ContentType { get; init; }

	public required string ETag { get; init; }

	/// <summary>Empty when <see cref="NotModified" /> is true.</summary>
	public required ReadOnlyMemory<byte> Content { get; init; }

	/// <summary>True when the caller's <c>knownETag</c> still matches and the host skipped the transfer.</summary>
	public required bool NotModified { get; init; }
}
