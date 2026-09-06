namespace MacroDeck.Sdk.Devices;

/// <summary>One hardware interaction reported by a device through <see cref="IDeviceSession.SendInteractionAsync" />.</summary>
public sealed record DeviceInteraction
{
	public required DeviceInteractionKind Kind { get; init; }

	public required DeviceInteractionTarget Target { get; init; }

	/// <summary>An analog value, for kinds that carry one - an encoder delta or an analog control's level.</summary>
	public double? Value { get; init; }

	/// <summary>
	/// The <see cref="DeviceSurface.Revision" /> the provider was rendering when the interaction occurred.
	/// Lets the host discard an interaction that targeted a surface it has since superseded.
	/// </summary>
	public long? SurfaceRevision { get; init; }

	/// <summary>Provider-defined extras. Opaque to the host.</summary>
	public IReadOnlyDictionary<string, string> Data { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}
