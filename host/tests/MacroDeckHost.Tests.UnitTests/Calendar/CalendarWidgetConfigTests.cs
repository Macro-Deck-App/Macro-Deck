using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations.Calendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Calendar;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarWidgetConfigTests
{
	private CalendarWidgetHarness _harness = null!;

	[SetUp]
	public async Task SetUp()
	{
		_harness = new CalendarWidgetHarness();
		_harness.Google.WithAccount("bob", "work");
		await _harness.SyncAsync();
	}

	[Test]
	public async Task The_configuration_starts_from_the_documented_defaults()
	{
		var host = await RenderAsync(CalendarWidgetConfigViews.Build(Empty(), _harness.Cache));

		Assert.Multiple(() =>
		{
			Assert.That(host.ById(CalendarWidgetTypes.LayoutKey).Text(UiConfigProperties.Value),
				Is.EqualTo(CalendarWidgetTypes.LayoutAgenda), "a widget without a layout is an agenda");
			Assert.That(host.ById(CalendarWidgetTypes.LayoutKey).Property(UiConfigProperties.Options)!.Value
					.EnumerateArray().Select(option => option.GetProperty("value").GetString()),
				Is.EqualTo(new[] { CalendarWidgetTypes.LayoutAgenda, CalendarWidgetTypes.LayoutNextEvent }));
			Assert.That(host.ById(CalendarWidgetTypes.CalendarsKey).Property(UiConfigProperties.Value)!.Value
				.GetArrayLength(), Is.Zero, "no selection means every calendar");
			Assert.That(host.ById(CalendarWidgetTypes.DaysKey).Number(UiConfigProperties.Value), Is.EqualTo(1));
			Assert.That(Flag(host, CalendarWidgetTypes.ShowDateKey), Is.True);
			Assert.That(Flag(host, CalendarWidgetTypes.ShowAllDayKey), Is.True);
			Assert.That(Flag(host, CalendarWidgetTypes.ShowTimeKey), Is.True);
			Assert.That(Flag(host, CalendarWidgetTypes.ShowLocationKey), Is.False);
			Assert.That(Flag(host, CalendarWidgetTypes.ShowCalendarKey), Is.False);
		});
	}

	[Test]
	public async Task The_layout_comes_first_and_shows_only_the_fields_of_the_chosen_layout()
	{
		var host = await RenderAsync(CalendarWidgetConfigViews.Build(Empty(), _harness.Cache));
		string[] agendaOnly =
		[
			CalendarWidgetTypes.DaysKey, CalendarWidgetTypes.ShowTimeKey, CalendarWidgetTypes.ShowLocationKey,
			CalendarWidgetTypes.ShowCalendarKey,
		];
		string[] shared =
		[
			CalendarWidgetTypes.CalendarsKey, CalendarWidgetTypes.LeadTimeKey, CalendarWidgetTypes.ShowDateKey,
			CalendarWidgetTypes.ShowAllDayKey,
		];
		var asAgenda = Visibility(host, [.. agendaOnly, .. shared, CalendarWidgetTypes.WhenStartedKey]);
		string[] fields = [CalendarWidgetTypes.LayoutKey, .. agendaOnly, .. shared, CalendarWidgetTypes.WhenStartedKey];
		var order = Flatten(host.Tree.Root).Select(node => node.Id.Split('.')[^1]).Where(fields.Contains).ToList();

		host.ById(CalendarWidgetTypes.LayoutKey).Change(CalendarWidgetTypes.LayoutNextEvent);
		await host.SettleAsync();
		var asNextEvent = Visibility(host, [.. agendaOnly, .. shared, CalendarWidgetTypes.WhenStartedKey]);

		Assert.Multiple(() =>
		{
			Assert.That(order.IndexOf(CalendarWidgetTypes.LayoutKey), Is.Zero, "the layout is the first field");
			foreach (var key in agendaOnly)
			{
				Assert.That(asAgenda[key], Is.True, key + " belongs to the agenda");
				Assert.That(asNextEvent[key], Is.False, key + " is hidden for the next event");
			}

			foreach (var key in shared)
			{
				Assert.That(asAgenda[key] && asNextEvent[key], Is.True, key + " belongs to both layouts");
			}

			Assert.That(asAgenda[CalendarWidgetTypes.WhenStartedKey], Is.False);
			Assert.That(asNextEvent[CalendarWidgetTypes.WhenStartedKey], Is.True);
		});
	}

	[Test]
	public async Task Days_is_picked_from_one_to_seven()
	{
		var host = await RenderAsync(CalendarWidgetConfigViews.Build(Data(new { days = 12 }), _harness.Cache));
		var days = host.ById(CalendarWidgetTypes.DaysKey);

		Assert.Multiple(() =>
		{
			Assert.That(days.Number(UiConfigProperties.Min), Is.EqualTo(CalendarWidgetTypes.MinDays));
			Assert.That(days.Number(UiConfigProperties.Max), Is.EqualTo(CalendarWidgetTypes.MaxDays));
			Assert.That(days.Number(UiConfigProperties.Value), Is.EqualTo(7), "a stored value outside the range is clamped");
			Assert.That(days.Property(UiConfigProperties.Options)!.Value.EnumerateArray()
					.Select(option => option.GetProperty("value").GetString()),
				Is.EqualTo(new[] { "1", "2", "3", "4", "5", "6", "7" }));
		});
	}

	[Test]
	public async Task Every_connected_calendar_is_offered_with_its_account_and_service()
	{
		var host = await RenderAsync(CalendarWidgetConfigViews.Build(Empty(), _harness.Cache));
		var options = host.ById(CalendarWidgetTypes.CalendarsKey).Property(UiConfigProperties.Options)!.Value
			.EnumerateArray()
			.ToList();

		var team = options.Single(option => option.GetProperty("value").GetString() == CalendarWidgetHarness.CalendarKey("team"));

		Assert.Multiple(() =>
		{
			Assert.That(options.Select(option => option.GetProperty("value").GetString()),
				Is.EquivalentTo(new[]
				{
					CalendarWidgetHarness.CalendarKey("main"), CalendarWidgetHarness.CalendarKey("team"),
					CalendarWidgetHarness.CalendarKey("work", "bob"),
				}));
			Assert.That(Resolve(team.GetProperty("label").Deserialize<LocalizedText>()),
				Is.EqualTo(Resolve(Strings.Details.Source(calendar: "Calendar team",
					account: "alice@example.com",
					provider: "Google Calendar"))));
		});
	}

	[Test]
	public async Task A_stored_calendar_selection_is_kept()
	{
		var host = await RenderAsync(CalendarWidgetConfigViews.Build(
			Data(new { calendars = new[] { CalendarWidgetHarness.CalendarKey("team") } }),
			_harness.Cache));

		Assert.That(host.ById(CalendarWidgetTypes.CalendarsKey).Property(UiConfigProperties.Value)!.Value
				.EnumerateArray().Select(item => item.GetString()),
			Is.EqualTo(new[] { CalendarWidgetHarness.CalendarKey("team") }));
	}

	[Test]
	public async Task The_next_event_layout_skips_all_day_events_and_keeps_a_running_event_by_default()
	{
		var host = await RenderAsync(CalendarWidgetConfigViews.Build(
			Data(new { layout = CalendarWidgetTypes.LayoutNextEvent }),
			_harness.Cache));
		var whenStarted = host.ById(CalendarWidgetTypes.WhenStartedKey);

		Assert.Multiple(() =>
		{
			Assert.That(Flag(host, CalendarWidgetTypes.ShowAllDayKey), Is.False);
			Assert.That(Flag(host, CalendarWidgetTypes.ShowDateKey), Is.True);
			Assert.That(whenStarted.Text(UiConfigProperties.Value), Is.EqualTo(CalendarWidgetTypes.WhenStartedNow));
			Assert.That(whenStarted.Property(UiConfigProperties.Options)!.Value.EnumerateArray()
					.Select(option => option.GetProperty("value").GetString()),
				Is.EqualTo(new[] { CalendarWidgetTypes.WhenStartedNow, CalendarWidgetTypes.WhenStartedNext }));
		});
	}

	[Test]
	public async Task Edited_configurations_validate_against_the_registered_widget_schemas()
	{
		var registry = TestWidgetTypeProviders.Registry();
		await new WidgetTypeProviderHost(registry, TimeProvider.System, Serilog.Core.Logger.None)
			.StartAsync(new CalendarIntegration());
		var schemas = new WidgetDataSchemaProvider(registry);

		var host = await RenderAsync(CalendarWidgetConfigViews.Build(Empty(), _harness.Cache));
		host.ById(CalendarWidgetTypes.CalendarsKey).Change(new[] { CalendarWidgetHarness.CalendarKey("team") });
		host.ById(CalendarWidgetTypes.DaysKey).Change(4);
		host.ById(CalendarWidgetTypes.ShowLocationKey).Change(true);
		host.ById(CalendarWidgetTypes.ShowDateKey).Change(false);
		var agenda = Compose(host, """[{"triggerType":"onCalendarEventStartsSoon","children":[]}]""");

		host.ById(CalendarWidgetTypes.LayoutKey).Change(CalendarWidgetTypes.LayoutNextEvent);
		host.ById(CalendarWidgetTypes.WhenStartedKey).Change(CalendarWidgetTypes.WhenStartedNext);
		var nextEvent = Compose(host, """[{"triggerType":"onShortPress","children":[]}]""");

		Assert.Multiple(() =>
		{
			Assert.That(schemas.TryGet(CalendarWidgetTypes.QualifiedId, out var schema), Is.True);
			Assert.That(WidgetDataSchema.Validate(schema!, agenda), Is.Empty);
			Assert.That(WidgetDataSchema.Validate(schema!, nextEvent), Is.Empty);
			Assert.That(agenda.GetProperty(CalendarWidgetTypes.DaysKey).GetDouble(), Is.EqualTo(4));
			Assert.That(nextEvent.GetProperty(CalendarWidgetTypes.LayoutKey).GetString(),
				Is.EqualTo(CalendarWidgetTypes.LayoutNextEvent));
			Assert.That(WidgetDataSchema.Validate(schema!,
				JsonSerializer.SerializeToElement(new { layout = "week" })), Is.Not.Empty, "an unknown layout is refused");
		});
	}

	[TestCase(CalendarWidgetTypes.LayoutAgenda)]
	[TestCase(CalendarWidgetTypes.LayoutNextEvent)]
	public async Task Either_layout_edits_its_flows_for_the_press_and_own_calendar_triggers(string layout)
	{
		var host = await RenderAsync(CalendarWidgetConfigViews.Build(Data(new { layout }), _harness.Cache));
		string[] expected =
		[
			"onShortPress", "onLongPress", "onDoublePress", "onTouchStart", "onTouchEnd",
			"onCalendarEventStartsSoon", "onCalendarEventStarted", "onCalendarEventEnded",
		];

		Assert.That(host.ById("flows").Property(UiConfigProperties.Triggers)!.Value.EnumerateArray()
			.Select(trigger => trigger.GetString()), Is.EquivalentTo(expected));
	}

	[Test]
	public async Task The_lead_time_is_picked_in_minutes_and_starts_at_fifteen()
	{
		var fresh = await RenderAsync(CalendarWidgetConfigViews.Build(Empty(), _harness.Cache));
		var stored = await RenderAsync(CalendarWidgetConfigViews.Build(Data(new { leadTime = 30 }), _harness.Cache));
		var leadTime = fresh.ById(CalendarWidgetTypes.LeadTimeKey);

		Assert.Multiple(() =>
		{
			Assert.That(leadTime.Number(UiConfigProperties.Value), Is.EqualTo(15));
			Assert.That(stored.ById(CalendarWidgetTypes.LeadTimeKey).Number(UiConfigProperties.Value), Is.EqualTo(30));
			Assert.That(leadTime.Property(UiConfigProperties.Options)!.Value.EnumerateArray()
					.Select(option => option.GetProperty("value").GetString()),
				Is.EqualTo(new[] { "1", "5", "10", "15", "30", "60" }));
			Assert.That(leadTime.Property(UiConfigProperties.Options)!.Value.EnumerateArray()
					.Select(option => TestLocalization.Resolve(Read(option.GetProperty("label")))),
				Does.Contain("1 minute").And.Contain("30 minutes"));
		});
	}

	[Test]
	public async Task The_provider_serves_the_configuration_of_the_calendar_widget_and_declines_others()
	{
		var calendar = await _harness.OpenConfigAsync(new { layout = CalendarWidgetTypes.LayoutNextEvent });
		var other = await _harness.Provider.CreateSessionAsync(new MacroDeck.Sdk.Ui.UiSessionRequest
		{
			Surface = CalendarWidgetHarness.ConfigSurface("app.macro-deck.calendar::agenda", new { }),
			UiModelVersion = 1,
		}, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Find(calendar.BuildTree().Root, CalendarWidgetTypes.LayoutKey), Is.Not.Null);
			Assert.That(Find(calendar.BuildTree().Root, CalendarWidgetTypes.WhenStartedKey), Is.Not.Null);
			Assert.That(other, Is.Null, "the two types of the first draft never shipped and are not served");
		});
	}

	[Test]
	public void The_calendar_picker_arrives_with_its_options_in_the_first_tree()
	{
		var picker = UiTestHost.Render(CalendarWidgetConfigViews.Build(Empty(), _harness.Cache))
			.ById(CalendarWidgetTypes.CalendarsKey);

		Assert.Multiple(() =>
		{
			Assert.That(picker.HasProperty(UiConfigProperties.DynamicOptions), Is.False);
			Assert.That(picker.Property(UiConfigProperties.Options)!.Value.GetArrayLength(), Is.EqualTo(3));
		});
	}

	private static async Task<UiTestHost> RenderAsync(MacroDeck.Ui.Dsl.UiElement root)
	{
		var host = UiTestHost.Render(root);
		await host.SettleAsync();
		return host;
	}

	private static bool? Flag(UiTestHost host, string key) => host.ById(key).Flag(UiConfigProperties.Value);

	private static Dictionary<string, bool> Visibility(UiTestHost host, IEnumerable<string> keys)
		=> keys.ToDictionary(key => key, key =>
		{
			if (host.ById(key).Property(UiConfigProperties.VisibleWhen) is not { ValueKind: JsonValueKind.Object } rule)
			{
				return true;
			}

			var sibling = host.ById(rule.GetProperty("parameterName").GetString()!).Text(UiConfigProperties.Value);
			return rule.GetProperty("values").EnumerateArray().Any(value => value.GetString() == sibling);
		});

	private static JsonElement Compose(UiTestHost host, string flows)
		=> JsonSerializer.SerializeToElement(new Dictionary<string, object?>
		{
			[CalendarWidgetTypes.LayoutKey] = host.ById(CalendarWidgetTypes.LayoutKey).Text(UiConfigProperties.Value),
			[CalendarWidgetTypes.CalendarsKey] = host.ById(CalendarWidgetTypes.CalendarsKey).Property(UiConfigProperties.Value),
			[CalendarWidgetTypes.DaysKey] = host.ById(CalendarWidgetTypes.DaysKey).Number(UiConfigProperties.Value),
			[CalendarWidgetTypes.WhenStartedKey] = host.ById(CalendarWidgetTypes.WhenStartedKey).Text(UiConfigProperties.Value),
			[CalendarWidgetTypes.ShowDateKey] = Flag(host, CalendarWidgetTypes.ShowDateKey),
			[CalendarWidgetTypes.ShowAllDayKey] = Flag(host, CalendarWidgetTypes.ShowAllDayKey),
			[CalendarWidgetTypes.ShowTimeKey] = Flag(host, CalendarWidgetTypes.ShowTimeKey),
			[CalendarWidgetTypes.ShowLocationKey] = Flag(host, CalendarWidgetTypes.ShowLocationKey),
			[CalendarWidgetTypes.ShowCalendarKey] = Flag(host, CalendarWidgetTypes.ShowCalendarKey),
			[CalendarWidgetTypes.LeadTimeKey] = host.ById(CalendarWidgetTypes.LeadTimeKey).Number(UiConfigProperties.Value),
			["flows"] = JsonDocument.Parse(flows).RootElement.Clone(),
		});

	private static JsonElement Empty() => Data(new { });

	private static JsonElement Data(object value) => JsonSerializer.SerializeToElement(value);
}
