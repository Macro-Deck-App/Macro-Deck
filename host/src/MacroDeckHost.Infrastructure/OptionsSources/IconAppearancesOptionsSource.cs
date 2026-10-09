using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class IconAppearancesOptionsSource : IHostOptionsSource
{
	private readonly IIconPackCache _icons;

	public IconAppearancesOptionsSource(IIconPackCache icons)
	{
		_icons = icons;
	}

	public string Id => IconOptionsSourceIds.Appearances;

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var options = IconAppearanceOptions.BuiltIn().ToList();

		var variants = new SortedDictionary<string, string>(StringComparer.Ordinal);
		foreach (var pack in _icons.GetAllPacks())
		{
			foreach (var icon in _icons.GetIconsByPackId(pack.Id).Where(icon => icon.AppearanceOfId is null))
			{
				foreach (var appearance in _icons.GetAppearances(icon.Id))
				{
					var key = IconAppearanceTraits.ToKey(appearance.AppearanceTraits ?? new Dictionary<string, string>());
					if (IconAppearanceTraits.TryGetVariantName(key, out var name))
					{
						variants[key] = name;
					}
				}
			}
		}

		options.AddRange(variants.Select(variant => new ActionParameterOption
		{
			Value = variant.Key,
			Label = AppStrings.IconPacks.Appearances.Kind.Named(variant.Value)
		}));

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = 0
		});
	}
}
