using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Transport.Messages.ScreenSavers;

namespace MacroDeckHost.Application.Ui.Handlers;

public static class ScreenSaverDtoMapper
{
	public static ScreenSaver MapToDto(ScreenSaverCatalogEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);

		return new ScreenSaver
		{
			Id = entry.ScreenSaverId,
			ProviderId = entry.ProviderId,
			Name = entry.Descriptor.Name,
			Description = entry.Descriptor.Description ?? default,
			HasConfiguration = entry.Descriptor.HasConfiguration,
			Interactive = entry.Descriptor.Interactive,
			IsBuiltIn = BuiltInScreenSavers.IsBuiltIn(entry.ScreenSaverId)
		};
	}

	public static IReadOnlyList<ScreenSaver> MapToDto(IReadOnlyList<ScreenSaverCatalogEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);

		return [.. entries.Select(MapToDto)];
	}
}
