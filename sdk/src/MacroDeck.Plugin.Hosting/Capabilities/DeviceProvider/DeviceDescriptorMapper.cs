using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Sdk.Devices;

namespace MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;

/// <summary>Turns an SDK device descriptor into its wire shape.</summary>
internal static class DeviceDescriptorMapper
{
	public static DeviceDescriptorDto ToDto(DeviceDescriptor device)
		=> new()
		{
			Id = device.Id,
			Name = device.Name,
			Model = device.Model,
			Manufacturer = device.Manufacturer,
			LayoutReference = device.LayoutReference,
			Capabilities = device.Capabilities is { } capabilities
				? new DeviceCapabilitiesDto
				{
					KeyCount = capabilities.KeyCount,
					DialCount = capabilities.DialCount,
					DisplayCount = capabilities.DisplayCount,
					SupportsImages = capabilities.SupportsImages,
					SupportsText = capabilities.SupportsText,
					Extra = capabilities.Extra
				}
				: null,
			Presence = device.Presence.ToString(),
			Metadata = device.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)
		};
}
