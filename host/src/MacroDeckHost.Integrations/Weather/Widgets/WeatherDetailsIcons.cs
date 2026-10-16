using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Integrations.Weather.Widgets;

internal sealed record WeatherDetailsIcons(
	IReadOnlyDictionary<string, UiResource> Animated,
	IReadOnlyDictionary<string, UiResource> Still)
{
	public const string AnimatedFolder = "animated";

	public const string StillFolder = "static";

	public static readonly WeatherDetailsIcons None =
		new(new Dictionary<string, UiResource>(), new Dictionary<string, UiResource>());

	private static readonly string[] _animatedNames =
	[
		"clear-day", "clear-night", "cloudy", "cloudy-1-day", "cloudy-1-night", "cloudy-2-day",
		"cloudy-2-night", "fog-day", "fog-night", "rain-and-sleet-mix", "rainy-1-day", "rainy-1-night",
		"rainy-2-day", "rainy-2-night", "rainy-3-day", "rainy-3-night", "snowy-1-day", "snowy-1-night",
		"snowy-2-day", "snowy-2-night", "snowy-3-day", "snowy-3-night", "thunderstorms"
	];

	private static readonly string[] _stillNames =
	[
		"clear-day", "cloudy", "cloudy-1-day", "cloudy-2-day", "fog-day", "rain-and-sleet-mix", "rainy-1-day",
		"rainy-2-day", "rainy-3-day", "snowy-1-day", "snowy-2-day", "snowy-3-day", "thunderstorms"
	];

	public UiResource? AnimatedFor(string condition, bool isDay)
	{
		return Animated.GetValueOrDefault(IconName(condition, isDay));
	}

	public UiResource? StillFor(string condition)
	{
		return Still.GetValueOrDefault(IconName(condition, true));
	}

	public static async Task<WeatherDetailsIcons> ResolveAsync(
		IUiResourceRegistry? resources,
		CancellationToken cancellationToken)
	{
		if (resources is null)
		{
			return None;
		}

		var animated = await ResolveFolderAsync(resources, AnimatedFolder, _animatedNames, cancellationToken)
			.ConfigureAwait(false);
		var still = await ResolveFolderAsync(resources, StillFolder, _stillNames, cancellationToken)
			.ConfigureAwait(false);

		return new WeatherDetailsIcons(animated, still);
	}

	public static string IconName(string conditionSlug, bool isDay)
	{
		var suffix = isDay ? "day" : "night";

		return conditionSlug switch
		{
			"clear" => $"clear-{suffix}",
			"mainly-clear" => $"cloudy-1-{suffix}",
			"partly-cloudy" => $"cloudy-2-{suffix}",
			"overcast" => "cloudy",
			"fog" => $"fog-{suffix}",
			"drizzle" => $"rainy-1-{suffix}",
			"rain" => $"rainy-2-{suffix}",
			"rain-showers" => $"rainy-3-{suffix}",
			"freezing-rain" => "rain-and-sleet-mix",
			"snow-grains" => $"snowy-1-{suffix}",
			"snow" => $"snowy-2-{suffix}",
			"snow-showers" => $"snowy-3-{suffix}",
			"thunderstorm" => "thunderstorms",
			_ => "cloudy"
		};
	}

	private static async Task<IReadOnlyDictionary<string, UiResource>> ResolveFolderAsync(
		IUiResourceRegistry resources,
		string folder,
		IEnumerable<string> names,
		CancellationToken cancellationToken)
	{
		var icons = new Dictionary<string, UiResource>(StringComparer.Ordinal);
		foreach (var name in names)
		{
			try
			{
				icons[name] = await resources.GetPluginIconAsync(folder, name, cancellationToken).ConfigureAwait(false);
			}
			catch (UiResourceException)
			{
			}
		}

		return icons;
	}
}
