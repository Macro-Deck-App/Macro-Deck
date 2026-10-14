using MacroDeck.Sdk.Batteries;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Widgets.Battery;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class BatteryWidgetViewTests
{
	[Test]
	public void One_device_draws_a_full_ring_at_its_level_around_the_device_glyph()
	{
		var host = Render(new BatteryWidgetData(),
			FakeBatteries.Device("p", "Pixel", 42, BatteryDeviceKinds.Headphones));
		var ring = Drawn(host, UiComponents.Gauge).First();
		var glyph = Drawn(host, UiComponents.Icon).First(node => node.Id.EndsWith(".glyph", StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(ring.Number(UiComponentProperties.Level), Is.EqualTo(0.42));
			Assert.That(ring.Number(UiComponentProperties.StartAngle), Is.EqualTo(0));
			Assert.That(ring.Number(UiComponentProperties.EndAngle), Is.EqualTo(360));
			Assert.That(glyph.Text(UiComponentProperties.Icon), Is.EqualTo(UiIcons.DeviceHeadphones));
			Assert.That(glyph.RequiredComponentVersion, Is.EqualTo(UiIcons.VersionOf(UiIcons.DeviceHeadphones)));
			Assert.That(UiIcons.VersionOf(glyph.Fallback!.Text(UiComponentProperties.Icon)!),
				Is.EqualTo(1),
				"a reader of the first icon group must still get a glyph");
			Assert.That(host.ByText("42"), Is.Not.Empty);
		});
	}

	[Test]
	public void One_device_has_a_wide_and_a_tall_arrangement_with_its_name_and_state_beside_the_ring()
	{
		var host = Render(new BatteryWidgetData(),
			FakeBatteries.Device("p", "Pixel", 80, state: BatteryChargeState.Full));
		var layout = host.SingleByType(UiComponents.Responsive);
		var texts = Texts(host);

		Assert.Multiple(() =>
		{
			Assert.That(layout.Children.Count(child => child.Type == UiComponents.Stack), Is.EqualTo(3));
			Assert.That(host.ByText("Pixel"), Is.Not.Empty);
			Assert.That(texts, Does.Contain("Widgets.Battery.State.Full"));
		});
	}

	[Test]
	public void Several_devices_sit_in_a_grid_of_rings_and_in_rows_on_a_tall_tile()
	{
		var host = Render(new BatteryWidgetData(),
			FakeBatteries.Device("a", "A", 10),
			FakeBatteries.Device("b", "B", 20),
			FakeBatteries.Device("c", "C", 30),
			FakeBatteries.Device("d", "D", 40));
		var layout = host.SingleByType(UiComponents.Responsive);
		var square = layout.Children[0];

		Assert.Multiple(() =>
		{
			Assert.That(square.Type, Is.EqualTo(UiComponents.Grid));
			Assert.That(square.Number(UiComponentProperties.Columns), Is.EqualTo(2));
			Assert.That(square.Number(UiComponentProperties.Rows), Is.EqualTo(2));
			Assert.That(layout.Children.Any(child
					=> child.Id.EndsWith("tall-rows", StringComparison.Ordinal)
					&& child.Children.Count(row => row.Type == UiComponents.Stack) == 4),
				Is.True);
		});
	}

	[Test]
	public void The_bar_style_draws_a_bar_per_device_and_no_ring()
	{
		var host = Render(new BatteryWidgetData { Style = BatteryWidgetData.StyleBar },
			FakeBatteries.Device("a", "A", 10),
			FakeBatteries.Device("b", "B", 75));
		var narrow = host.SingleByType(UiComponents.Responsive).Children[0];
		var bars = narrow.Children.Select(row => Find(row, UiComponents.RangeBar)).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(host.ByType(UiComponents.Gauge), Is.Empty);
			Assert.That(bars.Select(bar => bar!.Number(UiComponentProperties.End)),
				Is.EqualTo(new double?[] { 0.1, 0.75 }));
			Assert.That(bars.Select(bar => bar!.Text(UiComponentProperties.StartColor)),
				Is.EqualTo(new[] { "#ff3b30", "#34c759" }));
		});
	}

	[Test]
	public void The_charging_bolt_follows_the_charge_state_and_the_badge_setting()
	{
		var charging = FakeBatteries.Device("p", "Pixel", 50, state: BatteryChargeState.Charging);

		Assert.Multiple(() =>
		{
			Assert.That(Bolts(Render(new BatteryWidgetData(), charging)), Is.Not.Empty);
			Assert.That(Bolts(Render(new BatteryWidgetData { ShowChargingBadge = false }, charging)), Is.Empty);
			Assert.That(Bolts(Render(new BatteryWidgetData(), FakeBatteries.Device("p", "Pixel", 50))), Is.Empty);
		});
	}

	[Test]
	public void An_unknown_level_shows_the_unknown_symbol_and_no_percent_sign()
	{
		var host = Render(new BatteryWidgetData(), FakeBatteries.Device("p", "Pixel", null));

		Assert.Multiple(() =>
		{
			Assert.That(host.ByText(BatteryWidgetView.UnknownLevel), Is.Not.Empty);
			Assert.That(host.ByText("%"), Is.Empty);
			Assert.That(Drawn(host, UiComponents.Gauge).All(ring => ring.Number(UiComponentProperties.Level) == 0),
				Is.True);
		});
	}

	[Test]
	public void A_disconnected_device_says_so_and_a_missing_one_says_it_is_not_available()
	{
		var disconnected = Render(new BatteryWidgetData(), FakeBatteries.Device("p", "Pixel", 30, connected: false));
		var missing = UiTestHost.Render(BatteryWidgetView.Build(new UiState<BatteryViewState>(
				new BatteryViewStateResolver(new FakeBatteries()).Resolve(new BatteryWidgetData
				{
					Mode = BatteryWidgetData.ModeOne,
					Device = new BatteryDeviceReference("adb:x", "Old phone", BatteryDeviceKinds.Phone)
				})),
			new BatteryWidgetData()));

		Assert.Multiple(() =>
		{
			Assert.That(Texts(disconnected), Does.Contain("Widgets.Battery.State.NotConnected"));
			Assert.That(Texts(missing), Does.Contain("Widgets.Battery.State.NotAvailable"));
			Assert.That(missing.ByText("Old phone"), Is.Not.Empty);
		});
	}

	[Test]
	public void No_devices_shows_a_hint_instead_of_rings()
	{
		var host = UiTestHost.Render(BatteryWidgetView.Build(new UiState<BatteryViewState>(BatteryViewState.NoDevices),
			new BatteryWidgetData()));

		Assert.Multiple(() =>
		{
			Assert.That(host.ByType(UiComponents.Gauge), Is.Empty);
			Assert.That(Texts(host), Does.Contain("Widgets.Battery.EmptyHint"));
		});
	}

	[Test]
	public void Eight_devices_stay_far_inside_the_tree_limits()
	{
		var devices = Enumerable.Range(0, 8)
			.Select(index
				=> FakeBatteries.Device($"d{index}", $"Device {index}", index * 10, state: BatteryChargeState.Charging))
			.ToArray();

		Assert.Multiple(() =>
		{
			Assert.That(Count(Render(new BatteryWidgetData(), devices).Root), Is.LessThan(1000));
			Assert.That(Count(Render(new BatteryWidgetData { Style = BatteryWidgetData.StyleBar }, devices).Root),
				Is.LessThan(1000));
		});
	}

	[Test]
	public void A_new_device_count_changes_the_layout_in_place()
	{
		var batteries = new FakeBatteries();
		batteries.Set(FakeBatteries.Device("a", "A", 10));
		var config = new BatteryWidgetData();
		var resolver = new BatteryViewStateResolver(batteries);
		var state = new UiState<BatteryViewState>(resolver.Resolve(config));
		var host = UiTestHost.Render(BatteryWidgetView.Build(state, config));

		batteries.Set(FakeBatteries.Device("a", "A", 10), FakeBatteries.Device("b", "B", 90));
		state.Set(resolver.Resolve(config));

		Assert.That(Drawn(host, UiComponents.Gauge).Select(ring => ring.Number(UiComponentProperties.Level)).Distinct(),
			Is.EquivalentTo(new double?[] { 0.1, 0.9 }));
	}

	internal static UiTestHost Render(BatteryWidgetData config, params BatteryDeviceSnapshot[] devices)
	{
		var batteries = new FakeBatteries();
		batteries.Set(devices);

		return UiTestHost.Render(BatteryWidgetView.Build(
			new UiState<BatteryViewState>(new BatteryViewStateResolver(batteries).Resolve(config)),
			config));
	}

	private static IEnumerable<UiTestNode> Drawn(UiTestHost host, string type)
	{
		return host.ByType(type).Where(node => !node.Id.Contains("_fallback", StringComparison.Ordinal));
	}

	private static IEnumerable<UiTestNode> Bolts(UiTestHost host)
	{
		return host.ByType(UiComponents.Icon)
			.Where(node => node.Text(UiComponentProperties.Icon) == UiIcons.Zap
				&& node.Id.EndsWith(".bolt", StringComparison.Ordinal));
	}

	private static UiTestNode? Find(UiTestNode node, string type)
	{
		return node.Type == type
			? node
			: node.Children.Select(child => Find(child, type)).FirstOrDefault(found => found is not null);
	}

	private static string Texts(UiTestHost host)
	{
		return string.Join('\n',
			Walk(host.Root).Select(node => node.Property(UiComponentProperties.Text)?.ToString() ?? string.Empty));
	}

	private static IEnumerable<UiTestNode> Walk(UiTestNode node)
	{
		return new[] { node }
			.Concat(node.Children.SelectMany(Walk))
			.Concat(node.Fallback is null ? [] : Walk(node.Fallback));
	}

	private static int Count(UiTestNode node)
	{
		return Walk(node).Count();
	}
}
