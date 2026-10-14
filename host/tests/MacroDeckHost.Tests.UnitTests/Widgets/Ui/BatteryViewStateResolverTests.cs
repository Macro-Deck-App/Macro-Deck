using MacroDeck.Sdk.Batteries;
using MacroDeck.Ui.Config;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Widgets.Battery;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class BatteryViewStateResolverTests
{
	private const string Red = "#ff3b30";
	private const string Orange = "#ff9500";
	private const string Yellow = "#ffcc00";
	private const string Green = "#34c759";
	private const string Cyan = "#32ade6";

	[TestCase(5, Red)]
	[TestCase(19, Red)]
	[TestCase(20, Orange)]
	[TestCase(39, Orange)]
	[TestCase(40, Yellow)]
	[TestCase(59, Yellow)]
	[TestCase(60, Green)]
	[TestCase(100, Green)]
	public void By_default_the_level_is_red_below_20_orange_below_40_yellow_below_60_and_green_above(
		int level,
		string color)
	{
		var face = Resolve(new BatteryWidgetData(), FakeBatteries.Device("p", "Phone", level));

		Assert.That(face.Color, Is.EqualTo(color));
	}

	[Test]
	public void A_charging_device_is_cyan_unless_the_charging_colour_is_turned_off()
	{
		var charging = FakeBatteries.Device("p", "Phone", 10, state: BatteryChargeState.Charging);

		Assert.Multiple(() =>
		{
			Assert.That(Resolve(new BatteryWidgetData(), charging).Color, Is.EqualTo(Cyan));
			Assert.That(Resolve(new BatteryWidgetData { ChargingColor = false }, charging).Color, Is.EqualTo(Red));
		});
	}

	[Test]
	public void Stored_thresholds_replace_the_default_bands()
	{
		var thresholds = new UiThresholds([
			new UiThresholdBand("low", "#111111"), new UiThresholdBand("high", "#222222", 50)
		]);
		var config = new BatteryWidgetData { Thresholds = thresholds };

		Assert.Multiple(() =>
		{
			Assert.That(Resolve(config, FakeBatteries.Device("p", "Phone", 49)).Color, Is.EqualTo("#111111"));
			Assert.That(Resolve(config, FakeBatteries.Device("p", "Phone", 50)).Color, Is.EqualTo("#222222"));
		});
	}

	[Test]
	public void A_single_colour_is_used_at_every_level_and_while_charging()
	{
		var config = new BatteryWidgetData { ColorMode = BatteryWidgetData.ColorSingle, Color = "#abcdef" };

		Assert.Multiple(() =>
		{
			Assert.That(Resolve(config, FakeBatteries.Device("p", "Phone", 5)).Color, Is.EqualTo("#abcdef"));
			Assert.That(Resolve(config, FakeBatteries.Device("p", "Phone", 90, state: BatteryChargeState.Charging))
					.Color,
				Is.EqualTo("#abcdef"));
		});
	}

	[Test]
	public void A_disconnected_device_keeps_its_last_level_but_is_drawn_muted()
	{
		var face = Resolve(new BatteryWidgetData(), FakeBatteries.Device("p", "Phone", 70, connected: false));

		Assert.Multiple(() =>
		{
			Assert.That(face.Status, Is.EqualTo(BatteryFaceStatus.NotConnected));
			Assert.That(face.Fill, Is.EqualTo(0.7));
			Assert.That(face.Color, Is.EqualTo(BatteryViewStateResolver.MutedColor));
		});
	}

	[Test]
	public void An_unknown_level_draws_an_empty_muted_ring()
	{
		var face = Resolve(new BatteryWidgetData(), FakeBatteries.Device("p", "Phone", null));

		Assert.Multiple(() =>
		{
			Assert.That(face.Fill, Is.Zero);
			Assert.That(face.Color, Is.EqualTo(BatteryViewStateResolver.MutedColor));
			Assert.That(BatteryWidgetView.PercentOf(face), Is.EqualTo(BatteryWidgetView.UnknownLevel));
		});
	}

	[Test]
	public void A_picked_device_the_registry_no_longer_knows_keeps_its_stored_name_and_reads_not_available()
	{
		var config = new BatteryWidgetData
		{
			Mode = BatteryWidgetData.ModeOne,
			Device = new BatteryDeviceReference("adb:gone", "Old phone", BatteryDeviceKinds.Tablet)
		};

		var face = new BatteryViewStateResolver(new FakeBatteries()).Resolve(config).Faces.Single();

		Assert.Multiple(() =>
		{
			Assert.That(face.Name, Is.EqualTo("Old phone"));
			Assert.That(face.Kind, Is.EqualTo(BatteryDeviceKinds.Tablet));
			Assert.That(face.Status, Is.EqualTo(BatteryFaceStatus.NotAvailable));
			Assert.That(BatteryWidgetView.PercentOf(face), Is.EqualTo(BatteryWidgetView.UnknownLevel));
		});
	}

	[Test]
	public void Nothing_to_show_asks_for_a_device_or_says_there_are_none()
	{
		var resolver = new BatteryViewStateResolver(new FakeBatteries());

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(new BatteryWidgetData()).Hint, Is.EqualTo(BatteryViewHint.NoDevices));
			Assert.That(resolver.Resolve(new BatteryWidgetData { Mode = BatteryWidgetData.ModeOne }).Hint,
				Is.EqualTo(BatteryViewHint.PickDevice));
			Assert.That(resolver.Resolve(new BatteryWidgetData { Mode = BatteryWidgetData.ModeSelection }).Hint,
				Is.EqualTo(BatteryViewHint.PickDevice));
		});
	}

	[Test]
	public void A_selection_shows_its_devices_in_the_picked_order()
	{
		var batteries = new FakeBatteries();
		batteries.Set(FakeBatteries.Device("a", "A", 10),
			FakeBatteries.Device("b", "B", 20),
			FakeBatteries.Device("c", "C", 30));

		var config = new BatteryWidgetData
		{
			Mode = BatteryWidgetData.ModeSelection,
			Selection =
			[
				new BatteryDeviceReference("adb:c", "C", BatteryDeviceKinds.Phone),
				new BatteryDeviceReference("adb:a", "A", BatteryDeviceKinds.Phone)
			]
		};

		var faces = new BatteryViewStateResolver(batteries).Resolve(config).Faces;

		Assert.That(faces.Select(face => face.Name), Is.EqualTo(new[] { "C", "A" }).AsCollection);
	}

	[Test]
	public void A_device_withdrawn_and_published_again_under_the_same_id_is_shown_again()
	{
		using var registry = new BatteryRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var device = new BatteryDevice
			{ Id = "phone", Name = "Pixel", Kind = BatteryDeviceKinds.Phone, LevelPercent = 64 };
		registry.Publish("adb", "session-1", [device]);

		var config = new BatteryWidgetData
		{
			Mode = BatteryWidgetData.ModeOne,
			Device = BatteryDeviceReference.Of(registry.Devices.Single())
		};
		var resolver = new BatteryViewStateResolver(registry);

		registry.RemoveSession("adb", "session-1");
		var withdrawn = resolver.Resolve(config).Faces.Single();

		registry.Publish("adb", "session-2", [device with { LevelPercent = 65 }]);
		var back = resolver.Resolve(config).Faces.Single();

		Assert.Multiple(() =>
		{
			Assert.That(withdrawn.Status, Is.EqualTo(BatteryFaceStatus.NotAvailable));
			Assert.That(withdrawn.Name, Is.EqualTo("Pixel"));
			Assert.That(back.Status, Is.EqualTo(BatteryFaceStatus.Live));
			Assert.That(back.Level, Is.EqualTo(65));
		});
	}

	private static BatteryFace Resolve(BatteryWidgetData config, BatteryDeviceSnapshot device)
	{
		var batteries = new FakeBatteries();
		batteries.Set(device);

		return new BatteryViewStateResolver(batteries).Resolve(config).Faces.Single();
	}
}
