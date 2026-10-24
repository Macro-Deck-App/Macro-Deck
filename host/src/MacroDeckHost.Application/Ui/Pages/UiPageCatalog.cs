using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeckHost.Application.Ui.Pages;

public static class UiPageCatalog
{
	public static IReadOnlyList<UiPageDescriptor> Accept(
		IEnumerable<string> surfaceKinds,
		IEnumerable<UiPageDescriptor> declared)
	{
		if (!surfaceKinds.Contains(UiSurfaceKinds.Page, StringComparer.Ordinal))
		{
			return [];
		}

		var ids = new HashSet<string>(StringComparer.Ordinal);

		return [.. declared.Where(page => UiPageDescriptorDto.IsValidId(page.Id) && ids.Add(page.Id))];
	}

	public static IReadOnlyList<UiPageDescriptor> Of(IUiProvider provider)
	{
		ArgumentNullException.ThrowIfNull(provider);

		return Accept(provider.Surfaces.Select(surface => surface.Kind),
			provider.Pages.Select(page => new UiPageDescriptor
			{
				Id = page.Id,
				Title = page.Title,
				IconName = page.IconName
			}));
	}
}
