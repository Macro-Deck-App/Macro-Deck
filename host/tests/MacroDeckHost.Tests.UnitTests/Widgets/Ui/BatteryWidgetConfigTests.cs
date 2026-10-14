using System.Text.Json;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Battery;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class BatteryWidgetConfigTests
{
	private FakeBatteries _batteries = null!;

	[SetUp]
	public void SetUp()
	{
		_batteries = new FakeBatteries();
		_batteries.Set(FakeBatteries.Device("phone", "Pixel", 40),
			FakeBatteries.Device("pods", "Buds", 80, BatteryDeviceKinds.Headphones));
	}

	[Test]
	public void Picking_one_device_stores_its_id_with_the_name_and_kind_it_had()
	{
		var host = Render(new { mode = "one" });

		host.ById("device.id").Change("adb:pods");

		var composed = Compose(host);
		var device = composed.GetProperty("device");

		Assert.Multiple(() =>
		{
			Assert.That(device.GetProperty("id").GetString(), Is.EqualTo("adb:pods"));
			Assert.That(device.GetProperty("name").GetString(), Is.EqualTo("Buds"));
			Assert.That(device.GetProperty("kind").GetString(), Is.EqualTo(BatteryDeviceKinds.Headphones));
			Assert.That(OptionLabels(host.ById("device.id")),
				Has.Some.Contains("ADB"),
				"a device is offered with the provider that reports it");
			Assert.That(WidgetDataSchema.Validate(Schema(), composed), Is.Empty);
		});
	}

	[Test]
	public void A_stored_device_that_is_gone_stays_picked_and_is_offered_as_not_available()
	{
		var stored = new { mode = "one", device = new { id = "adb:old", name = "Old phone", kind = "phone" } };
		var host = Render(stored);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("device.id").Text(UiConfigProperties.Value), Is.EqualTo("adb:old"));
			Assert.That(OptionValues(host.ById("device.id")), Does.Contain("adb:old"));
			Assert.That(Compose(host).GetProperty("device").GetProperty("name").GetString(), Is.EqualTo("Old phone"));
		});
	}

	[Test]
	public void A_device_published_again_under_its_old_id_is_picked_up_without_editing_the_widget()
	{
		var stored = new { mode = "one", device = new { id = "adb:phone", name = "Pixel", kind = "phone" } };

		_batteries.Set();
		var whileGone = Compose(Render(stored));

		_batteries.Set(FakeBatteries.Device("phone", "Pixel 9", 40));
		var host = Render(new
		{
			mode = "one", device = JsonSerializer.Deserialize<JsonElement>(whileGone.GetProperty("device").GetRawText())
		});

		Assert.Multiple(() =>
		{
			Assert.That(whileGone.GetProperty("device").GetProperty("id").GetString(), Is.EqualTo("adb:phone"));
			Assert.That(host.ById("device.id").Text(UiConfigProperties.Value), Is.EqualTo("adb:phone"));
			Assert.That(Compose(host).GetProperty("device").GetProperty("name").GetString(),
				Is.EqualTo("Pixel 9"),
				"the stored name follows the device once it reports again");
		});
	}

	[Test]
	public void A_selection_stores_every_picked_device_with_its_name()
	{
		var host = Render(new { mode = "selection" });

		host.ById("selection.ids").Change(new[] { "adb:pods", "adb:phone" });

		var devices = Compose(host).GetProperty("selection").GetProperty("devices").EnumerateArray().ToList();

		Assert.Multiple(() =>
		{
			Assert.That(devices.Select(device => device.GetProperty("id").GetString()),
				Is.EqualTo(new[] { "adb:pods", "adb:phone" }).AsCollection);
			Assert.That(devices.Select(device => device.GetProperty("name").GetString()),
				Is.EqualTo(new[] { "Buds", "Pixel" }).AsCollection);
			Assert.That(WidgetDataSchema.Validate(Schema(), Compose(host)), Is.Empty);
		});
	}

	[Test]
	public void Only_the_picker_for_the_chosen_mode_is_shown()
	{
		var host = Render(new { mode = "all" });

		Assert.Multiple(() =>
		{
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "device.id"), Is.False);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "selection.ids"), Is.False);
		});

		host.ById("mode").Change(BatteryWidgetData.ModeOne);
		Assert.That(WidgetConfigTestSupport.IsVisible(host, "device.id"), Is.True);

		host.ById("mode").Change(BatteryWidgetData.ModeSelection);
		Assert.Multiple(() =>
		{
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "device.id"), Is.False);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "selection.ids"), Is.True);
		});
	}

	[Test]
	public void Level_colours_and_a_single_colour_are_offered_one_at_a_time()
	{
		var host = Render(new { });

		Assert.Multiple(() =>
		{
			Assert.That(WidgetConfigTestSupport.IsVisible(host, WidgetThresholdsKey), Is.True);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "chargingColor"), Is.True);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "color"), Is.False);
			Assert.That(host.ById("chargingColor").Flag(UiConfigProperties.Value), Is.True);
		});

		host.ById("colorMode").Change(BatteryWidgetData.ColorSingle);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetConfigTestSupport.IsVisible(host, WidgetThresholdsKey), Is.False);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "color"), Is.True);
		});
	}

	[Test]
	public void Every_setting_round_trips_through_the_editor()
	{
		var stored = new
		{
			mode = "one",
			device = new { id = "adb:phone", name = "Pixel", kind = "phone" },
			style = "bar",
			showPercentage = false,
			showName = false,
			showChargingBadge = false,
			colorMode = "level",
			chargingColor = false,
			thresholds = new
			{
				bands = new object[]
					{ new { id = "a", color = "#111111" }, new { id = "b", color = "#222222", from = 50 } }
			}
		};

		var composed = Compose(Render(stored));
		var parsed = BatteryWidgetData.Parse(composed);

		Assert.Multiple(() =>
		{
			Assert.That(parsed.Mode, Is.EqualTo(BatteryWidgetData.ModeOne));
			Assert.That(parsed.Device,
				Is.EqualTo(new BatteryDeviceReference("adb:phone", "Pixel", BatteryDeviceKinds.Phone)));
			Assert.That(parsed.IsBar, Is.True);
			Assert.That(parsed.ShowPercentage, Is.False);
			Assert.That(parsed.ShowName, Is.False);
			Assert.That(parsed.ShowChargingBadge, Is.False);
			Assert.That(parsed.ChargingColor, Is.False);
			Assert.That(parsed.Thresholds!.ColorAt(60), Is.EqualTo("#222222"));
			Assert.That(WidgetDataSchema.Validate(Schema(), composed), Is.Empty);
		});
	}

	[Test]
	public void The_default_data_validates_and_shows_every_device()
	{
		var data = JsonSerializer.Deserialize<JsonElement>(BuiltInWidgetTypes
			.All(new HashSet<string>(StringComparer.Ordinal))
			.Single(type => type.Id == WidgetTypeIds.Battery)
			.DefaultData!);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(Schema(), data), Is.Empty);
			Assert.That(BatteryWidgetData.Parse(data).Mode, Is.EqualTo(BatteryWidgetData.ModeAll));
		});
	}

	private const string WidgetThresholdsKey = "thresholds";

	private UiTestHost Render(object data)
	{
		return UiTestHost.Render(BatteryWidgetConfigView.Build(JsonSerializer.SerializeToElement(data), _batteries));
	}

	private static Json.Schema.JsonSchema Schema()
	{
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));
		Assert.That(provider.TryGet(WidgetTypeIds.Battery, out var schema), Is.True);

		return schema!;
	}

	private static List<string> OptionValues(UiTestNode node)
	{
		return node.Property(UiConfigProperties.Options) is { ValueKind: JsonValueKind.Array } options
			? [.. options.EnumerateArray().Select(option => option.GetProperty("value").GetString() ?? string.Empty)]
			: [];
	}

	private static List<string> OptionLabels(UiTestNode node)
	{
		return node.Property(UiConfigProperties.Options) is { ValueKind: JsonValueKind.Array } options
			? [.. options.EnumerateArray().Select(option => option.GetProperty("label").GetRawText())]
			: [];
	}

	private static JsonElement Compose(UiTestHost host)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal);

		foreach (var key in new[]
			{
				"mode", "device", "selection", "style", "showPercentage", "showName", "showChargingBadge", "colorMode",
				"chargingColor", "thresholds", "color", "backgroundColor"
			})
		{
			if (host.FindById(key)?.Property(UiConfigProperties.Value) is { } value
				&& value.ValueKind != JsonValueKind.Null)
			{
				data[key] = value;
			}
		}

		return JsonSerializer.SerializeToElement(data);
	}
}
