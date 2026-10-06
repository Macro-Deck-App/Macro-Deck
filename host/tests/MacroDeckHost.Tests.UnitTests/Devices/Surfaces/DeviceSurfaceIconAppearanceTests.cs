using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.Icons;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceSurfaceIconAppearanceTests
{
	private static readonly Guid _iconId = Guid.Parse("0198aaaa-1111-2222-3333-444444444444");
	private static readonly Guid _staticId = Guid.Parse("0198bbbb-1111-2222-3333-444444444444");

	private DeviceSurfaceFixture _fixture = null!;

	[SetUp]
	public void SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
		_fixture.Icons.Add(new IconEntity
		{
			Id = _iconId,
			PackId = Guid.Empty,
			Name = "spinner",
			IsAnimated = true,
			MasterContentHash = "sha256:animated",
			ProcessingState = IconProcessingState.Ready
		});
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0, iconId: _iconId.ToString()));
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[TestCase(true)]
	[TestCase(false)]
	public async Task An_icon_without_appearances_reaches_every_device_as_before(bool animatesIcons)
	{
		var appearance = await OpenWith(animatesIcons);

		Assert.Multiple(() =>
		{
			Assert.That(appearance.IconId, Is.EqualTo(_iconId.ToString()));
			Assert.That(appearance.IconVersion, Is.EqualTo("sha256:animated"));
		});
	}

	[TestCase(false, "0198bbbb-1111-2222-3333-444444444444", "sha256:still")]
	[TestCase(true, "0198aaaa-1111-2222-3333-444444444444", "sha256:animated")]
	public async Task A_device_that_cannot_animate_gets_the_static_appearance(bool animatesIcons,
		string expectedId,
		string expectedVersion)
	{
		AddStaticAppearance();

		var appearance = await OpenWith(animatesIcons);

		Assert.Multiple(() =>
		{
			Assert.That(appearance.IconId, Is.EqualTo(expectedId));
			Assert.That(appearance.IconVersion, Is.EqualTo(expectedVersion));
		});
	}

	[Test]
	public async Task A_device_without_declared_visuals_is_treated_as_able_to_animate()
	{
		AddStaticAppearance();

		var deviceId = await _fixture.OpenDeviceAsync();

		Assert.That(_fixture.Provider.Latest(deviceId).Widgets.Single().Appearance!.IconId,
			Is.EqualTo(_iconId.ToString()));
	}

	[Test]
	public async Task The_icon_id_a_device_is_given_is_a_guid_its_session_serves_the_chosen_appearance_under()
	{
		using var harness = new IconTestHarness();
		var pack = await harness.CreatePack();
		var icon = await harness.AddReadyIcon(pack.Id, "spinner", [1, 1, 1]);
		var still = await harness.AddReadyAppearance(icon, "motion=static", [2, 2, 2]);
		var icons = new IconService(harness.Cache,
			harness.Storage,
			harness.FallbackStore,
			harness.VariantDeriver,
			harness.Coalescer,
			harness.Mediator,
			new IconPackOwnerRegistry([]));
		using var fixture = new DeviceSurfaceFixture(icons);
		fixture.Icons.Add(icon);
		fixture.Icons.Add(still);
		fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0, iconId: icon.Id.ToString()));

		var (deviceId, appearance) = await OpenWith(fixture, animatesIcons: false);
		var image = await fixture.Service.GetIconAsync(deviceId, appearance.IconId!, size: 72);

		Assert.Multiple(() =>
		{
			Assert.That(Guid.TryParse(appearance.IconId, out var parsed), Is.True);
			Assert.That(parsed, Is.EqualTo(still.Id));
			Assert.That(image, Is.Not.Null);
			Assert.That(image!.Content.ToArray(), Is.EqualTo(new byte[] { 2, 2, 2 }));
		});
	}

	private void AddStaticAppearance()
		=> _fixture.Icons.Add(new IconEntity
		{
			Id = _staticId,
			PackId = Guid.Empty,
			Name = "spinner",
			MasterContentHash = "sha256:still",
			ProcessingState = IconProcessingState.Ready,
			AppearanceOfId = _iconId,
			AppearanceTraits = new Dictionary<string, string> { ["motion"] = "static" }
		});

	private async Task<MacroDeck.Sdk.Devices.DeviceSurfaceAppearance> OpenWith(bool animatesIcons)
		=> (await OpenWith(_fixture, animatesIcons)).Appearance;

	private static async Task<(Guid DeviceId, MacroDeck.Sdk.Devices.DeviceSurfaceAppearance Appearance)> OpenWith(
		DeviceSurfaceFixture fixture,
		bool animatesIcons)
	{
		var deviceId = fixture.AddDevice("DeckA");
		var device = fixture.Devices.Devices.Single(candidate => candidate.Id == deviceId);
		device.LayoutSnapshot = LayoutSnapshotSerializer.Serialize(new LayoutDescriptor("deck",
			"Deck",
			[new LayoutRegion { Id = "grid", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = 2, Columns = 3 } }],
			new LayoutCapabilities
			{
				Visuals = LayoutVisualCapabilities.Full with { AnimatedIcons = animatesIcons }
			}));
		await fixture.Service.OpenAsync(deviceId, fixture.Provider.ProviderId, "DeckA");

		return (deviceId, fixture.Provider.Latest(deviceId).Widgets.Single().Appearance!);
	}
}
