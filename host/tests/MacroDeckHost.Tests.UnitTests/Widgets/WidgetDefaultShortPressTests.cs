using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Integrations.Weather;
using MacroDeckHost.Tests.UnitTests.Weather;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetDefaultShortPressTests
{
	private const string WeatherIntegrationId = WeatherIntegration.IntegrationId;
	private const string Station = "app.macro-deck.weather::home";
	private const string PluginId = "com.example.lights";

	private WidgetTypeRegistry _widgetTypes = null!;
	private FakeIntegrationRegistry _integrations = null!;
	private WidgetDefaultShortPress _defaults = null!;
	private WeatherIntegration _weather = null!;

	[SetUp]
	public void SetUp()
	{
		_widgetTypes = new WidgetTypeRegistry(new RecordingMediator());
		_integrations = new FakeIntegrationRegistry();
		_weather = new WeatherIntegration(new FakeOpenMeteoClient());
		_integrations.Add(_weather);
		_defaults = new WidgetDefaultShortPress(() => _widgetTypes, _integrations, TestLocalization.Resolver);
	}

	[TearDown]
	public void TearDown() => _weather.Dispose();

	private static WidgetEntity Widget(string type, string data)
		=> new() { Id = Guid.NewGuid(), FolderId = Guid.NewGuid(), Type = type, Data = data };

	private static JsonElement SingleBlock(string? source)
	{
		Assert.That(source, Is.Not.Null, "a default should run");
		using var document = JsonDocument.Parse(source!);
		var flows = document.RootElement.GetProperty("flows");
		Assert.That(flows.GetArrayLength(), Is.EqualTo(1));
		var flow = flows[0];
		Assert.That(flow.GetProperty("triggerType").GetString(), Is.EqualTo("onShortPress"));
		return flow.GetProperty("children")[0].Clone();
	}

	private static string? ParameterValue(JsonElement block, string name)
		=> block.GetProperty("parameters")
			.EnumerateArray()
			.First(parameter => parameter.GetProperty("name").GetString() == name)
			.GetProperty("value")
			.GetString();

	[Test]
	public void A_weather_widget_without_a_short_press_action_opens_the_details_of_its_own_station()
	{
		var block = SingleBlock(_defaults.FlowsSourceFor(Widget(WidgetTypeIds.Weather,
			$$"""{"instanceId":"{{Station}}"}"""), fromDevice: false));

		Assert.Multiple(() =>
		{
			Assert.That(block.GetProperty("integrationId").GetString(), Is.EqualTo(WeatherIntegrationId));
			Assert.That(block.GetProperty("actionId").GetString(), Is.EqualTo("show-details"));
			Assert.That(ParameterValue(block, "instanceId"), Is.EqualTo(Station));
		});
	}

	[Test]
	public void A_weather_widget_without_a_station_leaves_the_station_unset()
	{
		var block = SingleBlock(_defaults.FlowsSourceFor(Widget(WidgetTypeIds.Weather, "{}"), fromDevice: false));

		Assert.That(ParameterValue(block, "instanceId"), Is.Empty);
	}

	[Test]
	public void A_short_press_action_the_user_configured_wins_over_the_default()
	{
		var data = """{"flows":[{"triggerType":"onShortPress","children":[{"type":"action"}]}]}""";

		Assert.That(_defaults.FlowsSourceFor(Widget(WidgetTypeIds.Weather, data), fromDevice: false), Is.Null);
	}

	[TestCase("""{"flows":[{"triggerType":"onShortPress","children":[]}]}""")]
	[TestCase("""{"flows":[{"triggerType":"onShortPress","children":[{"type":"action","disabled":true}]}]}""")]
	[TestCase("""{"flows":"[{\"triggerType\":\"onLongPress\",\"children\":[{\"type\":\"action\"}]}]"}""")]
	[TestCase("""{"flows":[{"triggerType":"onShortPress","children":[]},{"triggerType":"onShortPress","children":[{"type":"action"}]}]}""")]
	public void A_short_press_flow_that_would_run_nothing_leaves_the_default_in_place(string data)
		=> Assert.That(_defaults.FlowsSourceFor(Widget(WidgetTypeIds.Weather, data), fromDevice: false), Is.Not.Null);

	[Test]
	public void Nothing_runs_while_the_integration_behind_the_default_is_disabled()
	{
		_integrations.SetEnabled(WeatherIntegrationId, false);

		Assert.That(_defaults.FlowsSourceFor(Widget(WidgetTypeIds.Weather, "{}"), fromDevice: false), Is.Null);
	}

	[Test]
	public void A_hardware_deck_press_does_not_open_the_weather_details_it_has_no_screen_for()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_defaults.FlowsSourceFor(Widget(WidgetTypeIds.Weather, "{}"), fromDevice: true), Is.Null);
			Assert.That(_defaults.RunsOnDevices(WidgetTypeIds.Weather), Is.False);
		});
	}

	[TestCase(WidgetTypeIds.ActionButton)]
	[TestCase(WidgetTypeIds.Clock)]
	[TestCase(WidgetTypeIds.MusicPlayer)]
	[TestCase(WidgetTypeIds.Countdown)]
	[TestCase(WidgetTypeIds.Stopwatch)]
	[TestCase(WidgetTypeIds.Slider)]
	public void A_built_in_type_without_a_default_runs_nothing_extra(string type)
		=> Assert.That(_defaults.FlowsSourceFor(Widget(type, "{}"), fromDevice: false), Is.Null);

	[Test]
	public async Task A_provider_type_runs_its_own_declared_action_with_its_parameters()
	{
		_integrations.Add(new FakeIntegration
		{
			Id = PluginId,
			Actions = [new CapturingActionDefinition { Id = "toggle", Parameters = [ActionParameter.Text("room")] }]
		});
		var registration = await _widgetTypes.Register(PluginId,
			new WidgetTypeDescriptor("lamp", LocalizedText.FromLiteral("Lamp"))
			{
				DefaultShortPressAction =
					new WidgetDefaultAction("toggle", new Dictionary<string, string> { ["room"] = "kitchen" })
			});

		var block = SingleBlock(_defaults.FlowsSourceFor(Widget(registration.WidgetTypeId,
			"""{"flows":[{"triggerType":"onShortPress","children":[{"type":"action"}]}]}"""), fromDevice: true));

		Assert.Multiple(() =>
		{
			Assert.That(_defaults.RunsOnDevices(registration.WidgetTypeId), Is.True);
			Assert.That(block.GetProperty("integrationId").GetString(), Is.EqualTo(PluginId),
				"a type without SupportsFlows has no user flow to win over its default");
			Assert.That(ParameterValue(block, "room"), Is.EqualTo("kitchen"));
		});
	}

	[Test]
	public async Task A_provider_type_naming_an_action_it_does_not_declare_runs_nothing()
	{
		_integrations.Add(new FakeIntegration { Id = PluginId });
		var registration = await _widgetTypes.Register(PluginId,
			new WidgetTypeDescriptor("lamp", LocalizedText.FromLiteral("Lamp"))
			{
				DefaultShortPressAction = new WidgetDefaultAction("missing")
			});

		Assert.That(_defaults.FlowsSourceFor(Widget(registration.WidgetTypeId, "{}"), fromDevice: false), Is.Null);
	}
}
