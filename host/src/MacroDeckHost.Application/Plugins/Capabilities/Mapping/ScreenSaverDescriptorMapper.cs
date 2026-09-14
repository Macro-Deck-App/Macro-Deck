using MacroDeck.Plugin.Protocol.Capabilities.ScreenSaverProvider;
using MacroDeck.Sdk.ScreenSavers;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class ScreenSaverDescriptorMapper
{
	public static ScreenSaverDescriptor ToDescriptor(ScreenSaverDescriptorDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		return new ScreenSaverDescriptor(dto.Id,
			dto.Name,
			dto.Description,
			dto.HasConfiguration,
			dto.Interactive,
			dto.Metadata);
	}
}
