using System.Reflection;
using System.Runtime.CompilerServices;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Widgets.Weather;

internal static class WeatherWidgetIcons
{
	internal const string OwnerId = "app.macro-deck.weather";

	private static readonly string[] _iconNames =
	[
		"clear-day", "clear-night", "cloudy", "cloudy-1-day", "cloudy-1-night", "cloudy-2-day",
		"cloudy-2-night", "fog-day", "fog-night", "rain-and-sleet-mix", "rainy-1-day", "rainy-1-night",
		"rainy-2-day", "rainy-2-night", "rainy-3-day", "rainy-3-night", "snowy-1-day", "snowy-1-night",
		"snowy-2-day", "snowy-2-night", "snowy-3-day", "snowy-3-night", "thunderstorms",
	];

	private static readonly string[] _staticIconNames = _iconNames;

	private static readonly ConditionalWeakTable<IUiResourceStore, Lazy<WeatherIconResources>> _registrations =
		new();

	internal static WeatherIconResources EnsureRegistered(IUiResourceStore store)
	{
		ArgumentNullException.ThrowIfNull(store);

		var lazy = _registrations.GetValue(store,
			static registryStore => new Lazy<WeatherIconResources>(() => RegisterAll(registryStore)));

		return lazy.Value;
	}

	internal static string IconName(string conditionSlug, bool isDay)
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
			_ => "cloudy",
		};
	}

	private static WeatherIconResources RegisterAll(IUiResourceStore store)
	{
		var assembly = typeof(WeatherWidgetIcons).Assembly;

		var animated = new Dictionary<string, UiResource>(_iconNames.Length, StringComparer.Ordinal);
		var still = new Dictionary<string, UiResource>(_staticIconNames.Length, StringComparer.Ordinal);

		foreach (var name in _iconNames)
		{
			animated[name] = store.Register(new UiResourceRegistration
			{
				OwnerId = OwnerId,
				Name = name,
				MediaType = "image/svg+xml",
				Content = ReadEmbeddedSvg(assembly, $".Icons.{name}.svg"),
			});
		}

		foreach (var name in _staticIconNames)
		{
			still[name] = store.Register(new UiResourceRegistration
			{
				OwnerId = OwnerId,
				Name = $"static.{name}",
				MediaType = "image/svg+xml",
				Content = ReadEmbeddedSvg(assembly, $".Icons.static.{name}.svg"),
			});
		}

		return new WeatherIconResources(animated, still);
	}

	private static byte[] ReadEmbeddedSvg(Assembly assembly, string suffix)
	{
		var resourceName = Array.Find(assembly.GetManifestResourceNames(),
			candidate => candidate.EndsWith(suffix, StringComparison.Ordinal));

		if (resourceName is null)
		{
			throw new InvalidOperationException($"No embedded resource ending with '{suffix}' was found in " +
				$"'{assembly.GetName().Name}'.");
		}

		using var stream = assembly.GetManifestResourceStream(resourceName)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);

		return memory.ToArray();
	}
}

internal sealed record WeatherIconResources(
	IReadOnlyDictionary<string, UiResource> Animated,
	IReadOnlyDictionary<string, UiResource> Still);
