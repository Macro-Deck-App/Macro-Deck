using System.Text.Json;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Weather;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeckHost.Integrations.Weather.Widgets;

internal interface IWeatherStations
{
	event EventHandler? StationsChanged;

	string? FirstStationId { get; }

	WeatherSnapshot? Snapshot(string stationId);
}

internal sealed class WeatherDetailsUiProvider : IUiProvider
{
	public const string DetailsViewId = "weather-details";

	private const string InstanceIdKey = "instanceId";

	private readonly IWeatherStations _stations;
	private readonly Func<IUiResourceRegistry?> _resources;

	public WeatherDetailsUiProvider(IWeatherStations stations, Func<IUiResourceRegistry?> resources)
	{
		_stations = stations;
		_resources = resources;
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive }
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;
		if (surface.Kind is not UiSurfaceKinds.Dialog
			|| ReadString(surface.Attributes, UiDialogSurfaceAttributes.ViewId) != DetailsViewId)
		{
			return null;
		}

		var registry = _resources();
		var icons = await WeatherDetailsIcons.ResolveAsync(registry, cancellationToken).ConfigureAwait(false);

		return new WeatherDetailsSession(surface,
			_stations,
			RequestedStation(surface),
			icons,
			registry is null ? _resources : null,
			WeatherDetailsTimeFormat.From(surface));
	}

	private static WeatherDetailsStation RequestedStation(UiSurface surface)
	{
		if (!surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var data)
			|| data.ValueKind != JsonValueKind.Object
			|| ReadString(data, InstanceIdKey) is not { } requested)
		{
			return WeatherDetailsStation.Default;
		}

		if (QualifiedId.TryParse(requested, out var qualified))
		{
			return string.Equals(qualified.OwnerId, WeatherIntegration.IntegrationId, StringComparison.Ordinal)
				? WeatherDetailsStation.Of(qualified.LocalId)
				: WeatherDetailsStation.Unknown;
		}

		return WeatherDetailsStation.Of(requested);
	}

	private static string? ReadString(IReadOnlyDictionary<string, JsonElement> attributes, string key)
	{
		return attributes.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	private static string? ReadString(JsonElement data, string key)
	{
		return data.TryGetProperty(key, out var value)
			&& value.ValueKind == JsonValueKind.String
			&& value.GetString() is { Length: > 0 } text
				? text
				: null;
	}
}

internal sealed record WeatherDetailsStation(string? StationId, bool IsForeign)
{
	public static readonly WeatherDetailsStation Default = new(null, false);

	public static readonly WeatherDetailsStation Unknown = new(null, true);

	public static WeatherDetailsStation Of(string stationId)
	{
		return new WeatherDetailsStation(stationId, false);
	}
}
