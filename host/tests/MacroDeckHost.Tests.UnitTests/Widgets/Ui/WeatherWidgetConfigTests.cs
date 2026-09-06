using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Weather;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Preview;
using MacroDeckHost.Widgets.Weather;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The Weather widget's <c>widget-config</c> tree (issue #837): every key the shipped schema declares for
/// it, the two conditional fields' dependencies, and the provider's decline of a foreign widget type.
/// </summary>
[TestFixture]
public class WeatherWidgetConfigTests
{
	private static readonly object _stored = new
	{
		instanceId = "owm.berlin",
		showIcon = true,
		showTemperature = true,
		showCondition = true,
		showForecast = true,
		animateIcon = true,
		forecastDays = 5,
		border = new { style = "static", color = "#ff0000" },
	};

	// A closed range of seven, each reading as a phrase rather than a digit, is picked from a list - and
	// stays a number while doing so, which is what the schema declares the field as.
	[Test]
	public void Forecast_days_is_picked_from_the_seven_day_counts_and_stays_a_number()
	{
		var host = Render(_stored);

		var options = host.ById("forecastDays").Property(UiConfigProperties.Options)!.Value;

		Assert.Multiple(() =>
		{
			Assert.That(options.EnumerateArray().Select(o => o.GetProperty("value").GetString()),
				Is.EqualTo(new[] { "1", "2", "3", "4", "5", "6", "7" }));
			Assert.That(host.ById("forecastDays").Property(UiConfigProperties.Value)!.Value.ValueKind,
				Is.EqualTo(JsonValueKind.Number));
		});
	}

	[Test]
	public void Editing_every_control_still_validates_against_the_weather_schema()
	{
		var host = Render(_stored);

		host.ById("instanceId").Change("owm.hamburg");
		host.ById("showIcon").Change(false);
		host.ById("showTemperature").Change(false);
		host.ById("showCondition").Change(false);
		host.ById("showForecast").Change(false);
		host.ById("forecastDays").Change(3);
		host.ById("border.style").Change("comet");
		host.ById("border.color").Change("#00ff00");

		var composed = Compose(host);
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));

		Assert.That(provider.TryGet(WidgetTypeIds.Weather, out var schema), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(schema!, composed), Is.Empty);
			Assert.That(composed.GetProperty("instanceId").GetString(), Is.EqualTo("owm.hamburg"));
			Assert.That(composed.GetProperty("showIcon").GetBoolean(), Is.False);
			Assert.That(composed.GetProperty("forecastDays").GetDouble(), Is.EqualTo(3));
			Assert.That(composed.GetProperty("border").GetProperty("style").GetString(), Is.EqualTo("comet"));
		});
	}

	[Test]
	public void AnimateIcon_is_visible_exactly_while_showIcon_is_set()
	{
		var host = Render(new { showIcon = true });

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "animateIcon"), Is.True);

		host.ById("showIcon").Change(false);

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "animateIcon"), Is.False);
	}

	[Test]
	public void ForecastDays_is_visible_exactly_while_showForecast_is_set()
	{
		var host = Render(new { showForecast = true });

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "forecastDays"), Is.True);

		host.ById("showForecast").Change(false);

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "forecastDays"), Is.False);
	}

	[Test]
	public async Task A_config_surface_naming_a_different_widget_type_is_declined()
	{
		var provider = new WeatherWidgetUiProvider(new StubWeatherRegistry([]),
			new NullResourceStore(),
			new WeatherStateNotifier(),
			new PassThroughSampleText(),
			Serilog.Core.Logger.None);

		var surface = ConfigSurface(WidgetTypeIds.Clock, "{}");

		var session = await provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	private static UiTestHost Render(object data)
		=> UiTestHost.Render(WeatherWidgetConfigView.Build(JsonSerializer.SerializeToElement(data),
			new StubWeatherRegistry([
				new WeatherStationDescriptor("owm.berlin", "openweathermap", default, "Berlin", false),
				new WeatherStationDescriptor("owm.hamburg", "openweathermap", default, "Hamburg", false),
			])));

	private static UiSurface ConfigSurface(string widgetType, string widgetData)
		=> new()
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiConfigSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.Deserialize<JsonElement>(widgetData),
			},
		};

	private static JsonElement Compose(UiTestHost host)
	{
		var border = new Dictionary<string, object?>
		{
			["style"] = host.ById("border.style").Text(UiConfigProperties.Value),
			["color"] = host.ById("border.color").Text(UiConfigProperties.Value),
		};

		var data = new Dictionary<string, object?>
		{
			["instanceId"] = host.ById("instanceId").Text(UiConfigProperties.Value),
			["showIcon"] = host.ById("showIcon").Flag(UiConfigProperties.Value),
			["showTemperature"] = host.ById("showTemperature").Flag(UiConfigProperties.Value),
			["showCondition"] = host.ById("showCondition").Flag(UiConfigProperties.Value),
			["showForecast"] = host.ById("showForecast").Flag(UiConfigProperties.Value),
			["animateIcon"] = host.ById("animateIcon").Flag(UiConfigProperties.Value),
			["forecastDays"] = host.ById("forecastDays").Number(UiConfigProperties.Value),
			["border"] = border,
		};

		return JsonSerializer.SerializeToElement(data);
	}

	private sealed class StubWeatherRegistry(IReadOnlyList<WeatherStationDescriptor> instances) : IWeatherRegistry
	{
		public IReadOnlyList<WeatherStationDescriptor> GetInstances() => instances;

		public IWeatherStation? GetStation(string instanceId) => null;

		public IWeatherStation? DefaultStation => null;
	}

	private sealed class NullResourceStore : IUiResourceStore
	{
		public UiResource Register(UiResourceRegistration registration)
			=> new() { ResourceId = $"{registration.OwnerId}.{registration.Name}", ContentHash = "hash" };

		public bool TryGet(string resourceId, out UiResourceContent content)
		{
			content = default!;
			return false;
		}
	}

	private sealed class PassThroughSampleText : IWidgetSampleTextResolver
	{
		public ValueTask<string> ResolveAsync(LocalizedString value) =>
			ValueTask.FromResult(value.ToString() ?? string.Empty);
	}
}
