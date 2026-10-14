using System.Text.Json;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Battery;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class BatteryWidgetSessionTests
{
	private FakeBatteries _batteries = null!;
	private BatteryWidgetUiProvider _provider = null!;

	[SetUp]
	public void SetUp()
	{
		_batteries = new FakeBatteries();
		_batteries.Set(FakeBatteries.Device("phone", "Pixel", 40));
		_provider = new BatteryWidgetUiProvider(_batteries, TestLocalization.SampleText);
	}

	[Test]
	public async Task The_first_tree_already_shows_the_current_level()
	{
		await using var session = await Open(new { mode = "all" });

		Assert.That(Levels(session.BuildTree().Root), Is.EqualTo(new[] { 0.4 }));
	}

	[Test]
	public async Task A_registry_change_updates_the_ring_and_a_new_device_joins_the_widget()
	{
		await using var session = await Open(new { mode = "all" });
		session.DrainPatches();

		_batteries.Set(FakeBatteries.Device("phone", "Pixel", 55), FakeBatteries.Device("pods", "Buds", 90));

		Assert.Multiple(() =>
		{
			Assert.That(session.DrainPatches(), Is.Not.Empty);
			Assert.That(Levels(session.BuildTree().Root), Is.EquivalentTo(new[] { 0.55, 0.9 }));
		});
	}

	[Test]
	public async Task A_change_to_another_device_leaves_a_single_device_widget_untouched()
	{
		await using var session = await Open(new { mode = "one", device = new { id = "adb:phone", name = "Pixel" } });
		session.DrainPatches();

		_batteries.Set(FakeBatteries.Device("phone", "Pixel", 40), FakeBatteries.Device("pods", "Buds", 90));

		Assert.That(session.DrainPatches().SelectMany(patch => patch.Operations), Is.Empty);
	}

	[TestCase("one")]
	[TestCase("all")]
	public async Task A_device_whose_kind_changes_patches_only_its_glyph(string mode)
	{
		_batteries.Set(FakeBatteries.Device("phone", "Pixel", 40), FakeBatteries.Device("pods", "Buds", 90));
		await using var session = await Open(new { mode, device = new { id = "adb:phone", name = "Pixel" } });
		session.DrainPatches();
		var before = Walk(session.BuildTree().Root).Select(node => node.Id).ToList();

		_batteries.Set(FakeBatteries.Device("phone", "Pixel", 40, BatteryDeviceKinds.Tablet),
			FakeBatteries.Device("pods", "Buds", 90));

		var operations = session.DrainPatches().SelectMany(patch => patch.Operations).ToList();
		var tree = session.BuildTree().Root;
		Assert.Multiple(() =>
		{
			Assert.That(operations, Is.Not.Empty);
			Assert.That(operations.Select(operation => operation.Op), Is.All.EqualTo(UiPatchOperations.SetProperties));
			Assert.That(operations.SelectMany(operation => operation.Properties?.Keys ?? []),
				Is.All.EqualTo(UiComponentProperties.Icon));
			Assert.That(Walk(tree).Select(node => node.Id), Is.EqualTo(before));
			Assert.That(Walk(tree)
					.Where(node => node.Type == UiComponents.Icon)
					.Select(node => node.Properties[UiComponentProperties.Icon].GetString()),
				Does.Contain(UiIcons.DeviceTablet));
		});
	}

	[Test]
	public async Task A_disposed_session_stops_listening_to_the_registry()
	{
		var session = await Open(new { mode = "all" });
		Assert.That(_batteries.SubscriberCount, Is.EqualTo(1));

		await session.DisposeAsync();
		_batteries.Set(FakeBatteries.Device("phone", "Pixel", 99));

		Assert.Multiple(() =>
		{
			Assert.That(_batteries.SubscriberCount, Is.Zero);
			Assert.That(Levels(session.BuildTree().Root), Is.EqualTo(new[] { 0.4 }));
		});
	}

	private async Task<IUiSession> Open(object data)
	{
		var session = await _provider.CreateSessionAsync(new UiSessionRequest
			{
				UiModelVersion = 1,
				Surface = new UiSurface
				{
					Kind = UiSurfaceKinds.Widget,
					SessionMode = UiSessionModes.Shared,
					Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
					{
						[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data)
					}
				}
			},
			CancellationToken.None);

		return session!;
	}

	private static List<double> Levels(UiNode root)
	{
		return Walk(root)
			.Where(node => node.Type == UiComponents.Gauge && !node.Id.Contains("_fallback", StringComparison.Ordinal))
			.Select(node => node.Properties[UiComponentProperties.Level].GetDouble())
			.Distinct()
			.ToList();
	}

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		return new[] { node }
			.Concat(node.Children.SelectMany(Walk))
			.Concat(node.Fallback is null ? [] : Walk(node.Fallback));
	}
}
