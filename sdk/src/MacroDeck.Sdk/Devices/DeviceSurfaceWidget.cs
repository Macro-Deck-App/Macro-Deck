namespace MacroDeck.Sdk.Devices;

/// <summary>One widget placed on a device's current surface.</summary>
public sealed record DeviceSurfaceWidget
{
	public required string Id { get; init; }

	/// <summary>
	/// The widget's type. Never an enum: an unknown widget type is data a provider can ignore or fall
	/// back on, rather than a parse failure that would drop the whole surface.
	/// </summary>
	public required string Type { get; init; }

	public required int PositionX { get; init; }

	public required int PositionY { get; init; }

	public required int Width { get; init; }

	public required int Height { get; init; }

	public bool IsPinned { get; init; }

	/// <summary>The id of the widget's current state, for a state-driven widget.</summary>
	public string? StateId { get; init; }

	/// <summary>The current state's display label.</summary>
	public string? StateLabel { get; init; }

	public DeviceSurfaceAppearance? Appearance { get; init; }

	/// <summary>The interaction kinds the host will accept for this widget.</summary>
	public IReadOnlyList<DeviceInteractionKind> SupportedInteractions { get; init; } = [];
}
