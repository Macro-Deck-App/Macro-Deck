namespace MacroDeck.Sdk.Devices;

/// <summary>What a <see cref="DeviceInteraction" /> is about.</summary>
public sealed record DeviceInteractionTarget
{
	/// <summary>
	/// The id of the widget on the device's current surface the interaction applies to. Must come from
	/// the surface the provider is currently rendering - the host rejects an id that is not on the
	/// device's current surface.
	/// </summary>
	public string? WidgetId { get; init; }

	/// <summary>The index of the physical control (key, dial, touch region) that produced the interaction,
	/// for a provider that reports by hardware position rather than by widget id.</summary>
	public int? ControlIndex { get; init; }
}
