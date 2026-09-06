using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public class DeviceEntity : BaseEntity
{
	// Empty for a provider-registered device: it authenticates through its provider, not against the
	// host, so there is no secret it could present. Every credential check must treat empty as "cannot
	// authenticate" rather than as a hash that merely fails to match.
	public required string SecretHash { get; set; }

	public required string Name { get; set; }

	// Once set, later client-proposed names must not overwrite the user's choice.
	public bool NameIsCustom { get; set; }

	public string? ProposedName { get; set; }

	public DeviceClientType ClientType { get; set; }

	public DeviceFormFactor FormFactor { get; set; }

	public string? Platform { get; set; }

	public string? Browser { get; set; }

	public string? AppVersion { get; set; }

	public DateTime LastSeenAt { get; set; }

	// Virtual profile IDs use integrationId::localId, so this cannot be a Guid.
	public string? StartupProfileId { get; set; }

	/// <summary>The integration or plugin id that registered this device; null for a client device.</summary>
	public string? ProviderId { get; set; }

	/// <summary>The provider's own stable id for the device. Unique within <see cref="ProviderId" />.</summary>
	public string? ProviderDeviceId { get; set; }

	public string? Model { get; set; }

	public string? Manufacturer { get; set; }

	/// <summary>Opaque layout reference supplied by the provider; the host does not interpret it.</summary>
	public string? LayoutReference { get; set; }

	/// <summary>Serialized device capability metadata supplied by the provider.</summary>
	public string? Capabilities { get; set; }

	/// <summary>
	/// Serialized <c>LayoutDescriptor</c> the device's <see cref="LayoutReference" /> last resolved to.
	/// Written on a resolution hit and left untouched on a miss, so a device keeps constraining a
	/// profile by its last known layout even while the provider that would resolve it is stopped.
	/// </summary>
	public string? LayoutSnapshot { get; set; }

	public bool IsProviderDevice => !string.IsNullOrEmpty(ProviderId);
}
