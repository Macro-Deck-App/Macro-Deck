using System.Globalization;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Rendering;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class FontsOptionsSource : IHostOptionsSource
{
	private readonly IFontCatalog _fonts;

	public FontsOptionsSource(IFontCatalog fonts)
	{
		_fonts = fonts;
	}

	public string Id => WidgetOptionsSources.Fonts;

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var options = _fonts.GetFaces()
			.Where(face => face.RemoteRenderable)
			.Where(face => filter is null || face.Family.Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(face => face.Family, StringComparer.OrdinalIgnoreCase)
			.ThenBy(face => face.Weight)
			.ThenBy(face => face.Slant, StringComparer.Ordinal)
			.Select(face => new ActionParameterOption
			{
				Value = face.FaceId,
				Label = $"{face.Family} {face.StyleName}",
				Metadata = new Dictionary<string, string>
				{
					["family"] = face.Family,
					["weight"] = face.Weight.ToString(CultureInfo.InvariantCulture),
					["slant"] = face.Slant
				}
			})
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = false,
			CacheSeconds = 60
		});
	}
}
