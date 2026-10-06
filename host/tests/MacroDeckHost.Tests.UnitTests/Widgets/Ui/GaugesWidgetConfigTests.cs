using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Icons.Included;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Gauges;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class GaugesWidgetConfigTests
{
	private static readonly object _two = new
	{
		title = "System",
		gauges = new object[]
		{
			new { id = "one", variable = "system_cpu_usage_percent", name = "CPU", max = 100 },
			new { id = "two", variable = "system_ram_usage_percent", name = "RAM", max = 100 },
		},
	};

	[Test]
	public void Editing_one_gauge_changes_only_that_gauge_and_keeps_every_id()
	{
		var host = Render(_two);

		host.ById("selectedGauge").Change("two");
		host.ById("gauges.two.max").Change(64);
		host.ById("gauges.two.name").Change("Memory");

		var gauges = Gauges(host);

		Assert.Multiple(() =>
		{
			Assert.That(gauges.Select(Id), Is.EqualTo(new[] { "one", "two" }).AsCollection);
			Assert.That(gauges[0].GetProperty("max").GetDouble(), Is.EqualTo(100));
			Assert.That(gauges[0].GetProperty("name").GetString(), Is.EqualTo("CPU"));
			Assert.That(gauges[1].GetProperty("max").GetDouble(), Is.EqualTo(64));
			Assert.That(gauges[1].GetProperty("name").GetString(), Is.EqualTo("Memory"));
		});
	}

	[Test]
	public void Every_gauge_field_is_written_and_the_result_validates_against_the_schema()
	{
		var host = Render(_two);

		host.ById("gauges.one.variable").Change("system_gpu_0_usage_percent");
		host.ById("gauges.one.icon").Change(new { type = "icon-pack", reference = "abc" });
		host.ById("gauges.one.min").Change(10);
		host.ById("gauges.one.color").Change("#00ff00");
		host.ById("gauges.one.iconColor").Change("#ffffff");
		host.ById("gauges.one.warnWhen").Change(GaugeConfig.WarnAbove);
		host.ById("gauges.one.warnAt").Change(90);
		host.ById("style").Change(GaugesWidgetData.StyleArc);

		var gauge = Gauges(host)[0];

		Assert.Multiple(() =>
		{
			Assert.That(gauge.GetProperty("variable").GetString(), Is.EqualTo("system_gpu_0_usage_percent"));
			Assert.That(gauge.GetProperty("icon").GetProperty("reference").GetString(), Is.EqualTo("abc"));
			Assert.That(gauge.GetProperty("min").GetDouble(), Is.EqualTo(10));
			Assert.That(gauge.GetProperty("color").GetString(), Is.EqualTo("#00ff00"));
			Assert.That(gauge.GetProperty("iconColor").GetString(), Is.EqualTo("#ffffff"));
			Assert.That(gauge.GetProperty("warnWhen").GetString(), Is.EqualTo("above"));
			Assert.That(gauge.GetProperty("warnAt").GetDouble(), Is.EqualTo(90));
			Assert.That(WidgetDataSchema.Validate(Schema(), Compose(host)), Is.Empty);
		});
	}

	[Test]
	public void A_gauge_name_may_insert_variables()
	{
		var host = Render(_two);

		host.ById("gauges.one.name").Change("CPU {{ vars.system_cpu_name }}");

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("gauges.one.name").Flag(UiConfigProperties.LiteralOnly), Is.Not.True);
			Assert.That(Gauges(host)[0].GetProperty("name").GetString(), Is.EqualTo("CPU {{ vars.system_cpu_name }}"));
		});
	}

	[Test]
	public void The_threshold_appears_only_once_a_warning_direction_is_chosen()
	{
		var host = Render(_two);

		Assert.That(host.FindById("gauges.one.warnAt"), Is.Null);

		host.ById("gauges.one.warnWhen").Change(GaugeConfig.WarnBelow);

		Assert.That(host.FindById("gauges.one.warnAt"), Is.Not.Null);

		host.ById("gauges.one.warnWhen").Change("none");

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("gauges.one.warnAt"), Is.Null);
			Assert.That(Gauges(host)[0].TryGetProperty("warnWhen", out _), Is.False);
		});
	}

	[Test]
	public void Clearing_the_icon_and_the_colours_removes_them_from_the_gauge()
	{
		var host = Render(new
		{
			gauges = new[]
			{
				new { id = "a", icon = new { type = "icon-pack", reference = "abc" }, color = "#ff0000", iconColor = "#ffffff" },
			},
		});

		host.ById("gauges.a.icon").Change(JsonSerializer.SerializeToElement<object?>(null));
		host.ById("gauges.a.color").Change(string.Empty);
		host.ById("gauges.a.iconColor").Change(string.Empty);

		var gauge = Gauges(host)[0];

		Assert.Multiple(() =>
		{
			Assert.That(gauge.TryGetProperty("icon", out _), Is.False);
			Assert.That(gauge.TryGetProperty("color", out _), Is.False);
			Assert.That(gauge.TryGetProperty("iconColor", out _), Is.False);
		});
	}

	[Test]
	public void A_preset_appends_a_ready_gauge_with_a_fresh_id()
	{
		var host = Render(JsonSerializer.Deserialize<JsonElement>(DefaultData()));

		host.ById("root.properties.presets.presetCpu").Activate();

		var gauges = Gauges(host);
		var added = gauges[^1];

		Assert.Multiple(() =>
		{
			Assert.That(gauges, Has.Count.EqualTo(3));
			Assert.That(gauges.Select(Id).Distinct().Count(), Is.EqualTo(3));
			Assert.That(added.GetProperty("variable").GetString(), Is.EqualTo("system_cpu_usage_percent"));
			Assert.That(added.GetProperty("icon").GetProperty("type").GetString(), Is.EqualTo("icon-pack"));
			Assert.That(added.GetProperty("icon").GetProperty("reference").GetString(),
				Is.EqualTo(IncludedIconPack.IconId(IncludedIconPack.Cpu).ToString()));
			Assert.That(added.GetProperty("max").GetDouble(), Is.EqualTo(100));
		});
	}

	[Test]
	public void Only_the_gauge_chosen_in_the_dropdown_shows_its_fields()
	{
		var host = Render(_two);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("selectedGauge").Text(UiConfigProperties.Value), Is.EqualTo("one"));
			Assert.That(host.FindById("gauges.one.max"), Is.Not.Null);
			Assert.That(host.FindById("gauges.two.max"), Is.Null);
		});

		host.ById("selectedGauge").Change("two");

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("gauges.one.max"), Is.Null);
			Assert.That(host.FindById("gauges.two.max"), Is.Not.Null);
			Assert.That(Gauges(host).Select(Id), Is.EqualTo(new[] { "one", "two" }).AsCollection,
				"a gauge whose fields are hidden is still part of the saved list");
		});
	}

	[Test]
	public void The_dropdown_names_each_gauge_and_numbers_the_unnamed_ones()
	{
		var host = Render(new { gauges = new object[] { new { id = "a", name = "CPU" }, new { id = "b" } } });
		var options = host.ById("selectedGauge").Property(UiConfigProperties.Options)!.Value.EnumerateArray().ToList();

		Assert.Multiple(() =>
		{
			Assert.That(options.Select(option => option.GetProperty("value").GetString()),
				Is.EqualTo(new[] { "a", "b" }).AsCollection);
			Assert.That(options[0].GetProperty("label").GetRawText(), Does.Contain("CPU"));
			Assert.That(options[1].GetProperty("label").GetRawText(), Does.Contain("Widgets.Gauges.GaugeNumber"));
		});
	}

	[Test]
	public void Add_selects_the_new_gauge_and_delete_removes_the_selected_one()
	{
		var host = Render(_two);

		host.ById("root.properties.gauge-row.addGauge").Activate();
		var added = Gauges(host)[2];

		Assert.Multiple(() =>
		{
			Assert.That(Gauges(host), Has.Count.EqualTo(3));
			Assert.That(Id(added), Is.Not.Empty.And.Not.EqualTo("one").And.Not.EqualTo("two"));
			Assert.That(host.ById("selectedGauge").Text(UiConfigProperties.Value), Is.EqualTo(Id(added)));
		});

		host.ById("selectedGauge").Change("one");
		host.ById("root.properties.gauge-row.deleteGauge").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(Gauges(host).Select(Id), Is.EqualTo(new[] { "two", Id(added) }).AsCollection);
			Assert.That(host.ById("selectedGauge").Text(UiConfigProperties.Value), Is.EqualTo("two"));
		});
	}

	[Test]
	public void Deleting_the_last_gauge_leaves_an_empty_list_with_only_add()
	{
		var host = Render(new { gauges = new[] { new { id = "only" } } });

		host.ById("root.properties.gauge-row.deleteGauge").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(Gauges(host), Is.Empty);
			Assert.That(host.FindById("selectedGauge"), Is.Null);
			Assert.That(host.FindById("root.properties.gauge-row.deleteGauge"), Is.Null);
			Assert.That(host.FindById("root.properties.gauge-row.addGauge"), Is.Not.Null);
		});
	}

	[Test]
	public void A_full_widget_offers_no_way_to_add_a_ninth_gauge_and_says_why()
	{
		var host = Render(new
		{
			gauges = Enumerable.Range(0, GaugesWidgetData.MaxGauges).Select(index => new { id = $"g{index}" }).ToArray(),
		});

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("root.properties.gauge-row.addGauge"), Is.Null);
			Assert.That(host.FindById("root.properties.presets"), Is.Null);
			Assert.That(host.FindById("root.properties.full-hint"), Is.Not.Null);
		});
	}

	[Test]
	public void The_chosen_gauge_is_not_stored_with_the_widget()
	{
		var host = Render(_two);

		Assert.That(host.ById("selectedGauge").Flag(UiConfigProperties.Transient), Is.True);
	}

	[Test]
	public void Stored_gauges_without_an_id_get_one_that_the_widget_reads_the_same_way()
	{
		var data = new { gauges = new object[] { new { name = "A" }, new { name = "B" } } };
		var host = Render(data);

		var ids = Gauges(host).Select(Id).ToList();
		var parsed = GaugesWidgetData.Parse(JsonSerializer.SerializeToElement(data)).Gauges.Select(gauge => gauge.Id);

		Assert.Multiple(() =>
		{
			Assert.That(ids.Distinct().Count(), Is.EqualTo(2));
			Assert.That(ids, Is.EqualTo(parsed).AsCollection);
		});
	}

	[Test]
	public void The_longest_kept_id_still_builds_every_editor_node()
	{
		var id = new string('a', 32);
		var host = Render(new { gauges = new[] { new { id, warnWhen = "above" } } });

		Assert.That(host.FindById($"gauges.{id}.warnAt"), Is.Not.Null);
	}

	[Test]
	public void An_id_that_cannot_key_an_editor_node_is_replaced()
	{
		var data = new { gauges = new object[] { new { id = new string('x', 200) }, new { id = "has spaces" } } };

		var ids = Gauges(Render(data)).Select(Id).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ids, Has.All.Matches<string>(id => id.Length <= 32 && !id.Contains(' ')));
			Assert.That(ids.Distinct().Count(), Is.EqualTo(2));
		});
	}

	[Test]
	public void The_default_data_validates_and_shows_two_working_gauges()
	{
		var data = JsonSerializer.Deserialize<JsonElement>(DefaultData());
		var config = GaugesWidgetData.Parse(data);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(Schema(), data), Is.Empty);
			Assert.That(config.Gauges.Select(gauge => gauge.Variable),
				Is.EqualTo(new[] { "system_cpu_usage_percent", "system_ram_usage_percent" }).AsCollection);
			Assert.That(config.Gauges.Select(gauge => gauge.Icon?.Reference),
				Is.EqualTo(new[]
				{
					IncludedIconPack.IconId(IncludedIconPack.Cpu).ToString(),
					IncludedIconPack.IconId(IncludedIconPack.MemoryStick).ToString(),
				}).AsCollection);
		});
	}

	private static string DefaultData()
		=> BuiltInWidgetTypes.All(new HashSet<string>(StringComparer.Ordinal))
			.Single(type => type.Id == WidgetTypeIds.Gauges)
			.DefaultData!;

	private static Json.Schema.JsonSchema Schema()
	{
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));
		Assert.That(provider.TryGet(WidgetTypeIds.Gauges, out var schema), Is.True);

		return schema!;
	}

	private static string Id(JsonElement gauge) => gauge.GetProperty("id").GetString() ?? string.Empty;

	private static List<JsonElement> Gauges(UiTestHost host)
		=> host.ById("gauges").Property(UiConfigProperties.Value)!.Value.EnumerateArray().ToList();

	private static JsonElement Compose(UiTestHost host)
		=> JsonSerializer.SerializeToElement(new Dictionary<string, object?>
		{
			["title"] = host.ById("title").Text(UiConfigProperties.Value),
			["style"] = host.ById("style").Text(UiConfigProperties.Value),
			["gauges"] = host.ById("gauges").Property(UiConfigProperties.Value),
			["backgroundColor"] = host.ById("backgroundColor").Text(UiConfigProperties.Value),
		});

	private static UiTestHost Render(object data)
		=> UiTestHost.Render(GaugesWidgetConfigView.Build(JsonSerializer.SerializeToElement(data)));
}
