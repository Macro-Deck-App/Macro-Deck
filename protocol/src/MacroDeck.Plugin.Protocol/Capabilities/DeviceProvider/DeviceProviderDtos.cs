namespace MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;

/// <summary>Mirrors the SDK's <c>DeviceCapabilities</c>.</summary>
public sealed record DeviceCapabilitiesDto
{
	public int KeyCount { get; init; }

	public int DialCount { get; init; }

	public int DisplayCount { get; init; }

	public bool SupportsImages { get; init; }

	public bool SupportsText { get; init; }

	public IReadOnlyDictionary<string, string> Extra { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Mirrors the SDK's <c>DeviceDescriptor</c>. <see cref="Presence" /> is a string, not the SDK's
/// <c>DevicePresence</c> enum - see <c>ActionParameterDto</c>'s remarks.
/// </summary>
public sealed record DeviceDescriptorDto
{
	/// <summary>The provider-local, stable device id.</summary>
	public required string Id { get; init; }

	public required string Name { get; init; }

	public string? Model { get; init; }

	public string? Manufacturer { get; init; }

	/// <summary>Opaque to the host, which stores and returns it without interpreting it.</summary>
	public string? LayoutReference { get; init; }

	public DeviceCapabilitiesDto? Capabilities { get; init; }

	/// <summary>One of the SDK's <c>DevicePresence</c> member names.</summary>
	public string Presence { get; init; } = "Online";

	public IReadOnlyDictionary<string, string> Metadata { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>The full result of the <c>device-provider</c> capability's <c>describe</c> operation.</summary>
public sealed record DeviceProviderDescribePayload
{
	public required string ProviderName { get; init; }

	public required IReadOnlyList<DeviceDescriptorDto> Devices { get; init; }
}

/// <summary>
/// Result of the <c>devices</c> operation: the same device list <c>describe</c> carries, exposed as its
/// own narrow round trip - see <c>VirtualProfilesResult</c>'s identical remarks.
/// </summary>
public sealed record DeviceProviderDevicesResult
{
	public required IReadOnlyList<DeviceDescriptorDto> Devices { get; init; }
}
