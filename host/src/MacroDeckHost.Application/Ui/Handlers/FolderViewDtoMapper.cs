using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Transport.Messages.FolderViews;

namespace MacroDeckHost.Application.Ui.Handlers;

public static class FolderViewDtoMapper
{
	public static FolderView MapToDto(FolderViewCatalogEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);

		return new FolderView
		{
			Id = entry.FolderViewId,
			ProviderId = entry.ProviderId,
			Name = entry.Descriptor.Name,
			Description = entry.Descriptor.Description ?? default,
			Navigation = entry.Descriptor.ResolvedNavigation,
			HasConfiguration = entry.Descriptor.HasConfiguration,
			IsBuiltIn = BuiltInFolderViews.IsWidgetGrid(entry.FolderViewId)
		};
	}

	public static IReadOnlyList<FolderView> MapToDto(IReadOnlyList<FolderViewCatalogEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);

		return [.. entries.Select(MapToDto)];
	}
}
