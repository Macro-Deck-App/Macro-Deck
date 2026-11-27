namespace MacroDeck.Sdk.Devices;

/// <summary>
/// One finished key picture the host rendered for a device session - see
/// <see cref="IDeviceSession.KeyImageFormat" />. <see cref="Row" /> and <see cref="Column" /> address a
/// cell of the layout's hardware grid, counted from 0 at the top left.
/// </summary>
public sealed record DeviceKeyImage
{
	public required int Row { get; init; }

	public required int Column { get; init; }

	/// <summary>Per cell: starts at 1 and increases with every change to that cell's picture. A provider
	/// drops a picture whose revision is not greater than the last one it applied for the cell.</summary>
	public required long Revision { get; init; }

	/// <summary>One of <see cref="MacroDeck.Sdk.Layouts.KeyImageContentTypes" />, matching the session's
	/// <see cref="IDeviceSession.KeyImageFormat" />.</summary>
	public required string ContentType { get; init; }

	/// <summary>The encoded picture, already mirrored and rotated as the format asks.</summary>
	public required ReadOnlyMemory<byte> Content { get; init; }
}
