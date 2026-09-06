namespace MacroDeck.Sdk.Devices;

/// <summary>
/// A device a provider offers to Macro Deck. <paramref name="Id" /> is the provider's own stable
/// identifier for the hardware - a serial number or equivalent - and is what makes a device the same
/// device across disconnects, provider restarts and host restarts. The host derives its own global id
/// from it and owns everything else about the device's lifecycle.
/// </summary>
/// <param name="Id">Stable provider-local id. Must be non-empty and unique within the provider.</param>
/// <param name="Name">Name proposed for the device. A name the user has since set wins over it.</param>
/// <param name="Model">Model or product name, e.g. "Stream Deck XL".</param>
/// <param name="Manufacturer">Vendor name.</param>
/// <param name="LayoutReference">
/// Reference to the layout this device uses. Opaque to the host, which stores and passes it through
/// without interpreting it; the layout abstraction that gives it meaning is a separate contract.
/// </param>
/// <param name="Capabilities">Generic input/output capabilities.</param>
/// <param name="Presence">Whether the device is reachable at the moment of registration.</param>
/// <param name="Metadata">Provider-defined metadata. Opaque to the host.</param>
public sealed record DeviceDescriptor(
	string Id,
	string Name,
	string? Model = null,
	string? Manufacturer = null,
	string? LayoutReference = null,
	DeviceCapabilities? Capabilities = null,
	DevicePresence Presence = DevicePresence.Online,
	IReadOnlyDictionary<string, string>? Metadata = null);
