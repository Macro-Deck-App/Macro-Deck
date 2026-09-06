namespace MacroDeck.Sdk.Devices;

/// <summary>
/// The layout a device's current surface renders under. These are the folder's <em>effective</em>
/// values, already resolved through folder → parent folder → profile default - a provider never has to
/// walk that chain itself.
/// </summary>
public sealed record DeviceSurfaceLayout
{
	public required int Rows { get; init; }

	public required int Columns { get; init; }

	public int WidgetSpacing { get; init; }

	public int WidgetBorderRadius { get; init; }

	public string? BackgroundColor { get; init; }

	/// <summary>
	/// The device's own opaque reference, as declared on <see cref="DeviceDescriptor.LayoutReference" />
	/// or <see cref="DeviceRegistration" />, echoed back verbatim. Never interpreted by the host - the
	/// host performs no reflow or clipping of <see cref="Rows" />, <see cref="Columns" /> or the widget
	/// positions against the device's physical capabilities; a provider is responsible for fitting what
	/// it receives to its own hardware.
	/// </summary>
	public string? LayoutReference { get; init; }
}
