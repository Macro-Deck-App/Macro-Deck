using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Sdk.Devices;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

/// <summary>Turns the wire shape of a device registration into the SDK descriptor the host works with.</summary>
public static class DeviceDescriptorMapper
{
	public static DeviceDescriptor ToDescriptor(DeviceDescriptorDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		return new DeviceDescriptor(dto.Id,
			dto.Name,
			dto.Model,
			dto.Manufacturer,
			dto.LayoutReference,
			ToCapabilities(dto.Capabilities),
			ToPresence(dto.Presence),
			dto.Metadata);
	}

	/// <summary>An unrecognised presence name is <see cref="DevicePresence.Unknown" />, never a failure -
	/// a plugin built against a later SDK must not break registration by naming a state this host has
	/// not heard of.</summary>
	public static DevicePresence ToPresence(string? presence)
		=> Enum.TryParse<DevicePresence>(presence, ignoreCase: true, out var parsed)
			? parsed
			: DevicePresence.Unknown;

	private static DeviceCapabilities? ToCapabilities(DeviceCapabilitiesDto? dto)
		=> dto is null
			? null
			: new DeviceCapabilities
			{
				KeyCount = dto.KeyCount,
				DialCount = dto.DialCount,
				DisplayCount = dto.DisplayCount,
				SupportsImages = dto.SupportsImages,
				SupportsText = dto.SupportsText,
				Extra = dto.Extra
			};
}
