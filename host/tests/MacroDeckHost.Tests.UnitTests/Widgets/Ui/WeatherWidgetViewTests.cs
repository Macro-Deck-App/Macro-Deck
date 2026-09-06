using System.Reflection;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Weather;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Weather;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class WeatherWidgetViewTests
{
	[Test]
	public void S1_The_tree_carries_no_size_argument_and_every_length_is_a_basis_fraction()
	{
		var buildMethod = typeof(WeatherWidgetView).GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;

		// Narrowed rather than dropped when the corner radius was admitted (ADR 0064): the property this
		// protects is that a resize never requires a new tree, and a radius does not move when a widget
		// is resized - a four-cell tile carries the same corner as a one-cell one. A width, a height or
		// a basis still may not reach a view.
		Assert.That(buildMethod.GetParameters()
				.Where(p => p.ParameterType == typeof(int) || p.ParameterType == typeof(double))
				.Select(p => p.Name),
			Is.SubsetOf(new[] { "cornerRadius" }),
			"The view must not take a width/height/basis argument - a resize must never require a new tree.");

		var host = RenderState(Sample());
		var lengthKeys = new[] { "mainSize", "size", "minSize", "gap", "padding", "thickness" };

		Assert.Multiple(() =>
		{
			foreach (var node in Walk(host.Root))
			{
				foreach (var key in lengthKeys)
				{
					var value = node.Property(key);

					if (value is null)
					{
						continue;
					}

					Assert.That(value.Value.ValueKind,
						Is.EqualTo(JsonValueKind.Object),
						$"'{node.Id}'.{key} must be a {{basis,...}} length, never a raw number.");
					Assert.That(value.Value.TryGetProperty("basis", out var basis), Is.True);
					Assert.That(basis.GetDouble(),
						Is.LessThan(3),
						$"'{node.Id}'.{key} looks like a pixel value rather than a widget-basis fraction.");
				}
			}

			Assert.That(host.Tree.Surface.Attributes.Keys,
				Has.None.Contains("gridSpan").And.None.Contains("width").And.None.Contains("height"));
		});
	}

	[Test]
	public void S2_MainSize_and_size_are_different_properties()
	{
		var host = RenderState(Sample());
		var day = host.ById("weather.forecast.2026-07-20.label.day");
		var min = host.ById("weather.forecast.2026-07-20.min");
		var icon = host.ById("weather.forecast.2026-07-20.label.icon");
		var bar = host.ById("weather.forecast.2026-07-20.bar");

		Assert.Multiple(() =>
		{
			Assert.That(day.Property("size")!.Value.GetRawText(), Is.EqualTo("""{"basis":0.06,"maxOfCross":0.5}"""));
			Assert.That(day.Property("mainSize")!.Value.GetRawText(),
				Is.EqualTo("""{"basis":0.114,"maxOfCross":0.95}"""));
			Assert.That(day.Property("size")!.Value.GetRawText(),
				Is.Not.EqualTo(day.Property("mainSize")!.Value.GetRawText()));

			Assert.That(min.Property("mainSize")!.Value.GetRawText(),
				Is.EqualTo("""{"basis":0.144,"maxOfCross":1.2}"""));
			Assert.That(icon.Property("mainSize")!.Value.GetRawText(),
				Is.EqualTo("""{"basis":0.09,"maxOfCross":0.8}"""));
			Assert.That(bar.Property("thickness")!.Value.GetRawText(),
				Is.EqualTo("""{"basis":0.03,"maxOfCross":0.35}"""));
		});
	}

	[Test]
	public void S3_Forecast_bars_share_one_week_wide_coordinate_space()
	{
		var host = RenderState(Sample());

		var expected = new (string Date, double Start, double End)[]
		{
			("2026-07-20", 0.4666667, 0.8666667),
			("2026-07-21", 0.4, 0.7),
			("2026-07-22", 0.5, 0.7666667),
			("2026-07-23", 0.5666667, 1.0),
			("2026-07-24", 0.0, 0.1666667),
		};

		Assert.Multiple(() =>
		{
			foreach (var (date, start, end) in expected)
			{
				var bar = host.ById($"weather.forecast.{date}.bar");

				Assert.That(bar.Property("start")!.Value.GetDouble(), Is.EqualTo(start).Within(1e-6), date);
				Assert.That(bar.Property("end")!.Value.GetDouble(), Is.EqualTo(end).Within(1e-6), date);
			}
		});
	}

	[Test]
	public void S4a_Equal_min_and_max_floors_the_bar_width_to_0_08()
	{
		var (start, end) = WeatherWidgetView.RangeBarSpan(min: 10, max: 10, weekMin: -2, span: 30);

		Assert.Multiple(() =>
		{
			Assert.That(start, Is.EqualTo(0.4).Within(1e-9));
			Assert.That(end, Is.EqualTo(0.48).Within(1e-9));
		});
	}

	[Test]
	public void S4b_A_day_at_the_week_maximum_clamps_its_end_to_1()
	{
		var (start, end) = WeatherWidgetView.RangeBarSpan(min: 28, max: 40, weekMin: -2, span: 30);

		Assert.Multiple(() =>
		{
			Assert.That(start, Is.EqualTo(1.0).Within(1e-9));
			Assert.That(end, Is.EqualTo(1.0).Within(1e-9));
		});
	}

	[Test]
	public void S4c_A_week_with_no_spread_floors_the_span_to_1_and_produces_no_NaN()
	{
		var state = Sample();
		state.Days = Enumerable.Range(0, 5)
			.Select(i => Day($"2026-07-{20 + i}", "clear", 5, 5))
			.ToList();

		var host = RenderState(state);

		Assert.Multiple(() =>
		{
			foreach (var bar in host.ByType("ui.range-bar"))
			{
				Assert.That(bar.Property("start")!.Value.GetDouble(), Is.EqualTo(0.0).Within(1e-9));
				Assert.That(bar.Property("end")!.Value.GetDouble(), Is.EqualTo(0.08).Within(1e-9));
				Assert.That(double.IsNaN(bar.Property("start")!.Value.GetDouble()), Is.False);
				Assert.That(double.IsNaN(bar.Property("end")!.Value.GetDouble()), Is.False);
			}
		});
	}

	private static readonly string[] _remainingSampleDates = ["2026-07-21", "2026-07-22", "2026-07-23", "2026-07-24"];

	[Test]
	public void S5_The_marker_is_row_zero_only()
	{
		var host = RenderState(Sample());

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("weather.forecast.2026-07-20.bar").Property("marker")!.Value.GetDouble(),
				Is.EqualTo(0.8333333).Within(1e-6));

			foreach (var date in _remainingSampleDates)
			{
				Assert.That(host.ById($"weather.forecast.{date}.bar").HasProperty("marker"),
					Is.False,
					$"row {date} must have no marker property at all, not an explicit null.");
			}
		});
	}

	[Test]
	public void S5_A_null_or_non_finite_temperature_yields_no_marker()
	{
		Assert.Multiple(() =>
		{
			Assert.That(WeatherWidgetView.MarkerFraction(null, -2, 30), Is.Null);
			Assert.That(WeatherWidgetView.MarkerFraction(double.NaN, -2, 30), Is.Null);
			Assert.That(WeatherWidgetView.MarkerFraction(double.PositiveInfinity, -2, 30), Is.Null);
			Assert.That(WeatherWidgetView.MarkerFraction(23, -2, 30), Is.EqualTo(0.8333333).Within(1e-6));
		});
	}

	[Test]
	public void S6_The_temperature_colour_ramp_normalises_by_unit_and_clamps()
	{
		Assert.Multiple(() =>
		{
			Assert.That(WeatherWidgetView.TempColorHex(20, "celsius"),
				Is.EqualTo(WeatherWidgetView.TempColorHex(68, "fahrenheit")),
				"20 C and 68 F are the same temperature and must produce the identical hex.");

			Assert.That(WeatherWidgetView.TempColorHex(-20, "celsius"),
				Is.EqualTo(WeatherWidgetView.TempColorHex(-10, "celsius")),
				"both clamp to -10 C.");

			Assert.That(WeatherWidgetView.TempColorHex(50, "celsius"),
				Is.EqualTo(WeatherWidgetView.TempColorHex(40, "celsius")),
				"both clamp to 40 C.");

			var (rCold, gCold, bCold) = ParseHex(WeatherWidgetView.TempColorHex(-10, "celsius"));
			Assert.That(bCold, Is.GreaterThan(rCold));
			Assert.That(bCold, Is.GreaterThan(gCold));

			var (rMid, gMid, bMid) = ParseHex(WeatherWidgetView.TempColorHex(15, "celsius"));
			Assert.That(gMid, Is.GreaterThan(rMid));
			Assert.That(gMid, Is.GreaterThan(bMid));

			var (rHot, gHot, bHot) = ParseHex(WeatherWidgetView.TempColorHex(40, "celsius"));
			Assert.That(rHot, Is.GreaterThan(gHot));
			Assert.That(rHot, Is.GreaterThan(bHot));

			var samples = new[] { -10, 0, 10, 20, 30, 40 }
				.Select(value => WeatherWidgetView.TempColorHex(value, "celsius"))
				.Distinct(StringComparer.Ordinal)
				.Count();
			Assert.That(samples, Is.EqualTo(6));
		});
	}

	[Test]
	public void S7_Forecast_rows_always_use_the_day_icon_variant()
	{
		var state = Sample();
		state.Condition = "clear";
		state.IsDay = false;
		state.Days[0].Condition = "clear";

		var host = RenderState(state);

		var currentIcon = ResourceId(host.ById("weather.current.head.icon"));
		var rowIcon = ResourceId(host.ById("weather.forecast.2026-07-20.label.icon"));

		Assert.Multiple(() =>
		{
			Assert.That(currentIcon, Is.EqualTo("app.macro-deck.weather.clear-night"));
			Assert.That(rowIcon, Is.EqualTo("app.macro-deck.weather.static.clear-day"));
			Assert.That(rowIcon, Is.Not.EqualTo(currentIcon));
		});
	}

	private static readonly string[] _daySuffixedSlugs =
	[
		"clear", "mainly-clear", "partly-cloudy", "fog", "drizzle", "rain", "rain-showers", "snow-grains",
		"snow", "snow-showers",
	];

	[Test]
	public void S7_The_icon_name_table_never_mechanically_appends_night_for_slugs_without_one()
	{
		Assert.Multiple(() =>
		{
			Assert.That(WeatherWidgetIcons.IconName("overcast", isDay: true), Is.EqualTo("cloudy"));
			Assert.That(WeatherWidgetIcons.IconName("overcast", isDay: false), Is.EqualTo("cloudy"));
			Assert.That(WeatherWidgetIcons.IconName("freezing-rain", isDay: true), Is.EqualTo("rain-and-sleet-mix"));
			Assert.That(WeatherWidgetIcons.IconName("freezing-rain", isDay: false), Is.EqualTo("rain-and-sleet-mix"));
			Assert.That(WeatherWidgetIcons.IconName("thunderstorm", isDay: true), Is.EqualTo("thunderstorms"));
			Assert.That(WeatherWidgetIcons.IconName("thunderstorm", isDay: false), Is.EqualTo("thunderstorms"));

			foreach (var slug in _daySuffixedSlugs)
			{
				Assert.That(WeatherWidgetIcons.IconName(slug, isDay: true), Does.EndWith("-day"));
				Assert.That(WeatherWidgetIcons.IconName(slug, isDay: false), Does.EndWith("-night"));
			}

			Assert.That(WeatherWidgetIcons.IconName("not-a-real-slug", isDay: true), Is.EqualTo("cloudy"));
		});
	}

	[Test]
	public void S8_A_station_that_exists_with_no_data_yet_shows_a_centred_loading_card_with_no_bars()
	{
		var state = WeatherStatePayload.Unavailable("loc1");
		var host = RenderState(state);

		Assert.Multiple(() =>
		{
			Assert.That(host.ByType("ui.range-bar"), Is.Empty);
			Assert.That(host.ById("weather.state.title").Property("text")!.Value.GetRawText(),
				Does.Contain("Widgets.Weather.CardTitle"));
			Assert.That(host.ById("weather.state.hint").Property("text")!.Value.GetRawText(),
				Does.Contain("Widgets.Weather.Loading"));
		});
	}

	[Test]
	public void S8_A_named_station_with_no_data_yet_shows_the_name_as_a_literal_title()
	{
		var state = WeatherStatePayload.Unavailable("loc1", "Vienna");
		var host = RenderState(state);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("weather.state.title").Text("text"), Is.EqualTo("Vienna"));
			Assert.That(host.ById("weather.state.hint").Property("text")!.Value.GetRawText(),
				Does.Contain("Widgets.Weather.Loading"));
		});
	}

	[Test]
	public void S8_A_station_that_no_longer_exists_renders_a_different_tree_from_the_loading_case()
	{
		var loading = RenderState(WeatherStatePayload.Unavailable("loc1"));
		var missing = RenderState(WeatherStatePayload.UnknownStation("loc1"));

		Assert.That(missing.ToCanonicalJson(), Is.Not.EqualTo(loading.ToCanonicalJson()));
	}

	[Test]
	public async Task S8_A_cold_open_with_a_cached_snapshot_seeds_revision_zero_with_no_patches()
	{
		var registry = new FakeWeatherRegistry();
		var station = new FakeStation(SnapshotFrom(Sample()));
		registry.Add("loc1", station);

		var provider
			= new WeatherWidgetUiProvider(registry,
				new UiResourceStore(),
				new WeatherStateNotifier(),
				TestLocalization.SampleText,
				NoLogger());
		var session = await provider.CreateSessionAsync(WidgetRequest("loc1"), CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var tree = session!.BuildTree();
		var patches = session.DrainPatches();
		var temp = FindById(tree.Root, "weather.current.head.temp")!;

		Assert.Multiple(() =>
		{
			Assert.That(tree.Revision, Is.Zero);
			Assert.That(patches,
				Is.Empty,
				"a cold open must already carry the cached data - filling it in asynchronously flashes on " +
				"every deck page turn.");
			Assert.That(temp.Properties["text"].GetRawText(), Does.Contain("\"value\":23"));
		});

		await session.DisposeAsync();
	}

	[Test]
	public void S9_ShowIcon_false_drops_the_current_icon_but_keeps_forecast_row_icons()
	{
		var host = RenderState(Sample(), Config(showIcon: false));

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("weather.current.head.icon"), Is.Null);
			Assert.That(host.FindById("weather.forecast.2026-07-20.label.icon"), Is.Not.Null);
		});
	}

	[Test]
	public void S9_ShowTemperature_false_keeps_forecast_min_and_max()
	{
		var host = RenderState(Sample(), Config(showTemperature: false));

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("weather.current.head.temp"), Is.Null);
			Assert.That(host.FindById("weather.forecast.2026-07-20.min"), Is.Not.Null);
			Assert.That(host.FindById("weather.forecast.2026-07-20.max"), Is.Not.Null);
		});
	}

	[Test]
	public void S9_ShowCondition_false_drops_the_condition_and_the_location_line()
	{
		var host = RenderState(Sample(), Config(showCondition: false));

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("weather.current.labels"), Is.Null);
			Assert.That(host.FindById("weather.current.labels.condition"), Is.Null);
			Assert.That(host.FindById("weather.current.labels.location"), Is.Null);
		});
	}

	[Test]
	public void S9_ShowForecast_false_leaves_zero_bars_and_the_current_block_fills_the_card()
	{
		var host = RenderState(Sample(), Config(showForecast: false));
		var current = host.ById("weather.current");

		Assert.Multiple(() =>
		{
			Assert.That(host.ByType("ui.range-bar"), Is.Empty);
			Assert.That(current.Flag("fill"), Is.True);
			Assert.That(current.HasProperty("mainSize"), Is.False);
		});
	}

	[Test]
	public void S9_ForecastDays_out_of_range_clamps_to_1_and_7_without_throwing()
	{
		Assert.Multiple(() =>
		{
			Assert.That(WeatherWidgetData.Parse(JsonSerializer.SerializeToElement(new { forecastDays = 0 }))
					.ForecastDays,
				Is.EqualTo(1));
			Assert.That(WeatherWidgetData.Parse(JsonSerializer.SerializeToElement(new { forecastDays = 9 }))
					.ForecastDays,
				Is.EqualTo(7));
		});
	}

	[Test]
	public void S9_ForecastDays_5_with_only_3_days_available_renders_3_rows_with_no_placeholders()
	{
		var state = Sample();
		state.Days = state.Days.Take(3).ToList();

		var host = RenderState(state, Config(forecastDays: 5));

		Assert.That(host.ByType("ui.range-bar"), Has.Count.EqualTo(3));
	}

	[Test]
	public async Task S10_Changing_only_the_temperature_patches_exactly_the_temperature_text_and_the_row_zero_marker()
	{
		var initial = Sample();
		var holder = new MutableAsyncSource(initial);
		var icons = WeatherWidgetIcons.EnsureRegistered(new UiResourceStore());
		var view = WeatherWidgetView.Build(holder.State, new WeatherWidgetData(), icons);
		var host = UiTestHost.Render(view, WidgetSurface());

		host.ClearPatches();

		holder.Current = new WeatherStatePayload
		{
			IsAvailable = initial.IsAvailable,
			StationExists = initial.StationExists,
			InstanceId = initial.InstanceId,
			LocationName = initial.LocationName,
			Temperature = 24,
			Condition = initial.Condition,
			IsDay = initial.IsDay,
			Unit = initial.Unit,
			Days = initial.Days,
		};
		holder.State.Reload();

		await host.SettleAsync();

		var patch = host.LastPatch;
		var nodeIds = patch.Operations.Select(op => op.NodeId).ToArray();

		Assert.Multiple(() =>
		{
			Assert.That(host.Patches, Has.Count.EqualTo(1));

			foreach (var operation in patch.Operations)
			{
				Assert.That(operation.Op, Is.EqualTo(UiPatchOperations.SetProperties));
			}

			Assert.That(nodeIds, Has.Length.EqualTo(2));
			Assert.That(nodeIds, Does.Contain("weather.current.head.temp"));
			Assert.That(nodeIds, Does.Contain("weather.forecast.2026-07-20.bar"));
		});
	}

	[Test]
	public void S11_No_user_visible_string_is_resolved_host_side()
	{
		var host = RenderState(Sample());

		Assert.Multiple(() =>
		{
			Assert.That(host.ByText("Partly cloudy"), Is.Empty);
			Assert.That(host.ByText("Teilweise bewölkt"), Is.Empty);
			Assert.That(host.ByText("Mo"), Is.Empty);

			Assert.That(host.ById("weather.current.labels.condition").Property("text")!.Value.GetRawText(),
				Is.EqualTo(
					"""{"$localized":{"scope":"macrodeck.app","key":"Widgets.Weather.Condition.PartlyCloudy"}}"""));
		});
	}

	[Test]
	public async Task S12_Nothing_that_looks_like_a_language_change_touches_the_provider_or_produces_patches()
	{
		var registry = new FakeWeatherRegistry();
		var station = new FakeStation(SnapshotFrom(Sample()));
		registry.Add("loc1", station);

		var provider
			= new WeatherWidgetUiProvider(registry,
				new UiResourceStore(),
				new WeatherStateNotifier(),
				TestLocalization.SampleText,
				NoLogger());
		var session = await provider.CreateSessionAsync(WidgetRequest("loc1"), CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var initialTree = session!.BuildTree();
		session.DrainPatches();

		Assert.That(station.FetchCount, Is.EqualTo(1));

		var treeAgain = session.BuildTree();
		var patchesAgain = session.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(patchesAgain, Is.Empty);
			Assert.That(station.FetchCount, Is.EqualTo(1));
			Assert.That(UiCanonicalJson.Serialize(treeAgain), Is.EqualTo(UiCanonicalJson.Serialize(initialTree)));
		});

		await session.DisposeAsync();
	}

	[Test]
	public async Task S13_One_shared_session_renders_byte_identical_bytes_that_two_catalogs_decode_differently()
	{
		var registry = new FakeWeatherRegistry();
		var station = new FakeStation(SnapshotFrom(Sample()));
		registry.Add("loc1", station);

		var provider
			= new WeatherWidgetUiProvider(registry,
				new UiResourceStore(),
				new WeatherStateNotifier(),
				TestLocalization.SampleText,
				NoLogger());
		var session = await provider.CreateSessionAsync(WidgetRequest("loc1"), CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var treeForConnectionA = session!.BuildTree();
		var treeForConnectionB = session.BuildTree();

		Assert.That(UiCanonicalJson.Serialize(treeForConnectionB),
			Is.EqualTo(UiCanonicalJson.Serialize(treeForConnectionA)));

		var conditionNode = FindById(treeForConnectionA.Root, "weather.current.labels.condition")!;
		using var document = JsonDocument.Parse(conditionNode.Properties["text"].GetRawText());
		var localized = document.RootElement.GetProperty("$localized");
		var key = new LocalizationKey(localized.GetProperty("scope").GetString()!,
			localized.GetProperty("key").GetString()!);

		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(AppStrings.LocalizationCatalog);
		var resolver = new LocalizationResolver(catalogs);

		var english = resolver.Resolve(new LocalizedString(key), "en");
		var german = resolver.Resolve(new LocalizedString(key), "de");

		Assert.That(english,
			Is.Not.EqualTo(german),
			"an implementation resolving per connection cannot produce identical bytes, so the same bytes " +
			"must still decode differently per catalog.");

		await session.DisposeAsync();
	}

	[Test]
	public void S14_The_weekday_is_derived_from_the_ISO_date_never_the_row_index()
	{
		var monday = Sample();
		var tuesday = Sample();
		tuesday.Days[0].Date = "2026-07-21";

		var hostMonday = RenderState(monday);
		var hostTuesday = RenderState(tuesday);

		var dayMonday = hostMonday.ById("weather.forecast.2026-07-20.label.day");
		var dayTuesday = hostTuesday.ById("weather.forecast.2026-07-21.label.day");

		Assert.Multiple(() =>
		{
			Assert.That(dayMonday.Property("text")!.Value.GetRawText(), Does.Contain("Widgets.Weather.Weekday.Mon"));
			Assert.That(dayTuesday.Property("text")!.Value.GetRawText(), Does.Contain("Widgets.Weather.Weekday.Tue"));
			Assert.That(dayMonday.Property("text")!.Value.GetRawText(),
				Is.Not.EqualTo(dayTuesday.Property("text")!.Value.GetRawText()));
		});
	}

	[Test]
	public void S14_An_invalid_or_empty_date_never_produces_a_visible_key_placeholder()
	{
		var state = Sample();
		state.Days = [Day(string.Empty, "clear", 1, 2)];

		var host = RenderState(state, Config(forecastDays: 1));
		var rows = host.ByType("ui.range-bar");

		Assert.That(rows, Has.Count.EqualTo(1));

		var dayNode = rows[0].Parent!.Children
			.Single(child => string.Equals(child.Id.Split('.')[^1], "label", StringComparison.Ordinal))
			.Children.Single(child => string.Equals(child.Id.Split('.')[^1], "day", StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(dayNode.HasProperty("text"), Is.False);
			Assert.That(host.ToCanonicalJson(), Does.Not.Contain("[["));
		});
	}

	[Test]
	public void S14_An_unrecognised_condition_slug_maps_to_Condition_Unknown()
	{
		var state = Sample();
		state.Condition = "not-a-real-condition";

		var host = RenderState(state);

		Assert.That(host.ById("weather.current.labels.condition").Property("text")!.Value.GetRawText(),
			Does.Contain("Widgets.Weather.Condition.Unknown"));
	}

	[Test]
	public async Task A_broadcast_state_change_refreshes_an_open_widget_without_anyone_reloading_it()
	{
		var registry = new FakeWeatherRegistry();
		var station = new FakeStation(SnapshotFrom(Sample()));
		registry.Add("loc1", station);

		var notifier = new WeatherStateNotifier();
		var provider = new WeatherWidgetUiProvider(registry,
			new UiResourceStore(),
			notifier,
			TestLocalization.SampleText,
			NoLogger());
		var session = await provider.CreateSessionAsync(WidgetRequest("loc1"), CancellationToken.None);

		Assert.That(session, Is.Not.Null);
		Assert.That(TemperatureTextOf(session!.BuildTree()), Does.Contain("23"));

		session.DrainPatches();

		var warmer = SampleAt(31);
		station.Update(SnapshotFrom(warmer));

		notifier.Publish("loc1", warmer);

		Assert.That(TemperatureTextOf(session.BuildTree()),
			Does.Contain("31"),
			"an open widget has to follow the station it is showing");
		Assert.That(session.DrainPatches(), Is.Not.Empty, "the refresh has to reach attached clients as a patch");
	}

	[Test]
	public async Task A_broadcast_for_a_different_station_leaves_the_widget_alone()
	{
		var registry = new FakeWeatherRegistry();
		var station = new FakeStation(SnapshotFrom(Sample()));
		registry.Add("loc1", station);
		registry.Add("loc2", new FakeStation(SnapshotFrom(Sample())));

		var notifier = new WeatherStateNotifier();
		var provider = new WeatherWidgetUiProvider(registry,
			new UiResourceStore(),
			notifier,
			TestLocalization.SampleText,
			NoLogger());
		var session = await provider.CreateSessionAsync(WidgetRequest("loc1"), CancellationToken.None);
		var fetchesAfterOpen = station.FetchCount;

		session!.DrainPatches();
		notifier.Publish("loc2", Sample());

		Assert.Multiple(() =>
		{
			Assert.That(station.FetchCount, Is.EqualTo(fetchesAfterOpen));
			Assert.That(session.DrainPatches(), Is.Empty);
		});
	}

	[Test]
	public async Task A_closed_session_stops_following_the_broadcast()
	{
		var registry = new FakeWeatherRegistry();
		var station = new FakeStation(SnapshotFrom(Sample()));
		registry.Add("loc1", station);

		var notifier = new WeatherStateNotifier();
		var provider = new WeatherWidgetUiProvider(registry,
			new UiResourceStore(),
			notifier,
			TestLocalization.SampleText,
			NoLogger());
		var session = await provider.CreateSessionAsync(WidgetRequest("loc1"), CancellationToken.None);

		await session!.DisposeAsync();
		var fetchesAfterClose = station.FetchCount;

		notifier.Publish("loc1", SampleAt(31));

		Assert.That(station.FetchCount,
			Is.EqualTo(fetchesAfterClose),
			"a disposed session must unsubscribe, or every closed widget keeps reloading forever");
	}

	[Test]
	public async Task Only_the_current_condition_animates_and_every_icon_resolves_to_real_artwork()
	{
		var registry = new FakeWeatherRegistry();
		registry.Add("loc1", new FakeStation(SnapshotFrom(Sample())));

		var store = new UiResourceStore();
		var provider = new WeatherWidgetUiProvider(registry,
			store,
			new WeatherStateNotifier(),
			TestLocalization.SampleText,
			NoLogger());
		var session = await provider.CreateSessionAsync(WidgetRequest("loc1"), CancellationToken.None);
		var tree = session!.BuildTree();

		var current = ResourceIdOf(FindById(tree.Root, "weather.current.head.icon")!);
		var rows = CollectForecastIconIds(tree.Root);

		Assert.Multiple(() =>
		{
			Assert.That(current, Does.Not.Contain(".static."));
			Assert.That(rows, Is.Not.Empty);

			foreach (var row in rows)
			{
				Assert.That(row, Does.Contain(".static."));
			}

			foreach (var id in rows.Append(current))
			{
				Assert.That(store.TryGet(id, out _), Is.True, $"'{id}' is not a registered resource");
			}
		});
	}

	private static string ResourceIdOf(UiNode node)
		=> node.Properties["source"].GetProperty("resourceId").GetString()!;

	private static List<string> CollectForecastIconIds(UiNode root)
	{
		var ids = new List<string>();

		void Walk(UiNode node)
		{
			if (node.Id.StartsWith("weather.forecast.", StringComparison.Ordinal) &&
				node.Id.EndsWith(".icon", StringComparison.Ordinal))
			{
				ids.Add(ResourceIdOf(node));
			}

			foreach (var child in node.Children)
			{
				Walk(child);
			}
		}

		Walk(root);

		return ids;
	}

	[Test]
	public void The_animate_icon_setting_swaps_only_the_current_condition_artwork()
	{
		var state = Sample();
		state.Condition = "clear";
		state.IsDay = false;

		var animated = RenderState(state, new WeatherWidgetData { AnimateIcon = true });
		var still = RenderState(state, new WeatherWidgetData { AnimateIcon = false });

		var animatedCurrent = ResourceId(animated.ById("weather.current.head.icon"));
		var stillCurrent = ResourceId(still.ById("weather.current.head.icon"));
		var animatedRow = ResourceId(animated.ById("weather.forecast.2026-07-20.label.icon"));
		var stillRow = ResourceId(still.ById("weather.forecast.2026-07-20.label.icon"));

		Assert.Multiple(() =>
		{
			Assert.That(animatedCurrent, Is.EqualTo("app.macro-deck.weather.clear-night"));
			Assert.That(stillCurrent, Is.EqualTo("app.macro-deck.weather.static.clear-night"));

			Assert.That(stillRow, Is.EqualTo(animatedRow));
			Assert.That(animatedRow, Does.Contain(".static."));
		});
	}

	private static WeatherStatePayload SampleAt(double temperature)
	{
		var payload = Sample();
		payload.Temperature = temperature;

		return payload;
	}

	private static string TemperatureTextOf(UiTree tree)
		=> FindById(tree.Root, "weather.current.head.temp")!.Properties["text"].GetRawText();

	private static WeatherStatePayload Sample() => new()
	{
		IsAvailable = true,
		StationExists = true,
		InstanceId = "loc1",
		LocationName = "Niederkassel, Germany",
		Temperature = 23,
		Condition = "partly-cloudy",
		IsDay = true,
		Unit = "celsius",
		Days =
		[
			Day("2026-07-20", "partly-cloudy", 12, 24),
			Day("2026-07-21", "rain", 10, 19),
			Day("2026-07-22", "thunderstorm", 13, 21),
			Day("2026-07-23", "clear", 15, 28),
			Day("2026-07-24", "snow", -2, 3),
		],
	};

	private static WeatherForecastDayPayload Day(string date, string condition, double min, double max)
		=> new() { Date = date, Condition = condition, Min = min, Max = max };

	private static WeatherWidgetData Config(
		bool showIcon = true,
		bool showTemperature = true,
		bool showCondition = true,
		bool showForecast = true,
		int forecastDays = 5)
		=> new()
		{
			ShowIcon = showIcon,
			ShowTemperature = showTemperature,
			ShowCondition = showCondition,
			ShowForecast = showForecast,
			ForecastDays = forecastDays,
		};

	private static UiSurface WidgetSurface() =>
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared };

	private static UiTestHost RenderState(WeatherStatePayload payload, WeatherWidgetData? config = null)
	{
		var state = new UiAsyncState<WeatherStatePayload>(_ => Task.FromResult(payload), payload);
		var icons = WeatherWidgetIcons.EnsureRegistered(new UiResourceStore());
		var root = WeatherWidgetView.Build(state, config ?? new WeatherWidgetData(), icons);

		return UiTestHost.Render(root, WidgetSurface());
	}

	private static IEnumerable<UiTestNode> Walk(UiTestNode node)
	{
		yield return node;

		foreach (var child in node.Children)
		{
			foreach (var descendant in Walk(child))
			{
				yield return descendant;
			}
		}

		if (node.Fallback is not null)
		{
			foreach (var descendant in Walk(node.Fallback))
			{
				yield return descendant;
			}
		}
	}

	private static string ResourceId(UiTestNode node)
	{
		using var document = JsonDocument.Parse(node.Property("source")!.Value.GetRawText());

		return document.RootElement.GetProperty("resourceId").GetString()!;
	}

	private static (int R, int G, int B) ParseHex(string hex)
	{
		var value = hex.TrimStart('#');

		return (Convert.ToInt32(value[..2], 16), Convert.ToInt32(value[2..4], 16), Convert.ToInt32(value[4..6], 16));
	}

	private static UiNode? FindById(UiNode node, string id)
	{
		if (string.Equals(node.Id, id, StringComparison.Ordinal))
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			if (FindById(child, id) is { } found)
			{
				return found;
			}
		}

		return node.Fallback is not null ? FindById(node.Fallback, id) : null;
	}

	private static Serilog.Core.Logger NoLogger() => new LoggerConfiguration().CreateLogger();

	private static WeatherSnapshot SnapshotFrom(WeatherStatePayload payload) => new()
	{
		IsAvailable = payload.IsAvailable,
		LocationName = payload.LocationName,
		Temperature = payload.Temperature,
		Condition = ConditionFromSlug(payload.Condition),
		IsDay = payload.IsDay,
		Unit = string.Equals(payload.Unit, "fahrenheit", StringComparison.OrdinalIgnoreCase)
			? TemperatureUnit.Fahrenheit
			: TemperatureUnit.Celsius,
		Days = payload.Days
			.Select(d => new WeatherForecastDay(DateOnly.ParseExact(d.Date, "yyyy-MM-dd"),
				ConditionFromSlug(d.Condition),
				d.Min,
				d.Max))
			.ToList(),
	};

	private static WeatherCondition ConditionFromSlug(string slug) => slug switch
	{
		"clear" => WeatherCondition.Clear,
		"mainly-clear" => WeatherCondition.MainlyClear,
		"partly-cloudy" => WeatherCondition.PartlyCloudy,
		"overcast" => WeatherCondition.Overcast,
		"fog" => WeatherCondition.Fog,
		"drizzle" => WeatherCondition.Drizzle,
		"rain" => WeatherCondition.Rain,
		"freezing-rain" => WeatherCondition.FreezingRain,
		"snow" => WeatherCondition.Snow,
		"snow-grains" => WeatherCondition.SnowGrains,
		"rain-showers" => WeatherCondition.RainShowers,
		"snow-showers" => WeatherCondition.SnowShowers,
		"thunderstorm" => WeatherCondition.Thunderstorm,
		_ => WeatherCondition.Unknown,
	};

	private static UiSessionRequest WidgetRequest(string? instanceId)
	{
		var data = instanceId is null
			? JsonSerializer.SerializeToElement(new { })
			: JsonSerializer.SerializeToElement(new { instanceId });

		return new UiSessionRequest
		{
			UiModelVersion = 1,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement("w1"),
					[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement("Weather"),
					[UiWidgetSurfaceAttributes.Data] = data,
				},
			},
		};
	}

	private sealed class MutableAsyncSource
	{
		public MutableAsyncSource(WeatherStatePayload initial)
		{
			Current = initial;
			State = new UiAsyncState<WeatherStatePayload>(_ => Task.FromResult(Current), initial);
		}

		public WeatherStatePayload Current { get; set; }

		public UiAsyncState<WeatherStatePayload> State { get; }
	}

	private sealed class FakeStation : IWeatherStation
	{
		private WeatherSnapshot _snapshot;

		public FakeStation(WeatherSnapshot snapshot) => _snapshot = snapshot;

		public int FetchCount { get; private set; }

		public void Update(WeatherSnapshot snapshot) => _snapshot = snapshot;

		public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct)
		{
			FetchCount++;

			return Task.FromResult(_snapshot);
		}
	}

	private sealed class FakeWeatherRegistry : IWeatherRegistry
	{
		private readonly Dictionary<string, IWeatherStation> _stations = new(StringComparer.Ordinal);
		private readonly List<WeatherStationDescriptor> _instances = [];

		public void Add(string instanceId, IWeatherStation station)
		{
			_stations[instanceId] = station;
			_instances.Add(new WeatherStationDescriptor(instanceId,
				"test",
				LocalizedText.FromLiteral("Test"),
				instanceId,
				false));
		}

		public IReadOnlyList<WeatherStationDescriptor> GetInstances() => _instances;

		public IWeatherStation? GetStation(string instanceId) => _stations.GetValueOrDefault(instanceId);

		public IWeatherStation? DefaultStation => _instances.Count > 0 ? _stations[_instances[0].InstanceId] : null;
	}
}
