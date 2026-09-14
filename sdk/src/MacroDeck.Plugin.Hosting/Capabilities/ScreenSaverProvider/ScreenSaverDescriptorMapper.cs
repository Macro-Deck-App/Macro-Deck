using MacroDeck.Plugin.Protocol.Capabilities.ScreenSaverProvider;
using MacroDeck.Sdk.ScreenSavers;

namespace MacroDeck.Plugin.Hosting.Capabilities.ScreenSaverProvider;

internal static class ScreenSaverDescriptorMapper
{
	public static ScreenSaverDescriptorDto ToDto(ScreenSaverDescriptor screenSaver)
	{
		ArgumentNullException.ThrowIfNull(screenSaver);

		return new ScreenSaverDescriptorDto
		{
			Id = screenSaver.Id,
			Name = screenSaver.Name,
			Description = screenSaver.Description,
			HasConfiguration = screenSaver.HasConfiguration,
			Interactive = screenSaver.Interactive,
			Metadata = screenSaver.Metadata
		};
	}
}
