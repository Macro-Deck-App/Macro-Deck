using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

/// <summary>
/// What a device is handed for a widget's icon, before and after an action icon provider becomes
/// authoritative for it (issue #425 work package 6, acceptance scenarios 27 and 28). The absolute
/// constraint under test is that <c>DeviceSurfaceAppearance.IconId</c> never carries anything but an
/// icon-pack GUID or null - a provider-owned icon travels only through the new field alongside it.
/// </summary>
[TestFixture]
internal sealed class DeviceSurfaceProviderIconTests
{
	private const string IconA = "0198aaaa-1111-2222-3333-444444444444";

	private DeviceSurfaceFixture _fixture = null!;

	[SetUp]
	public void SetUp() => _fixture = new DeviceSurfaceFixture();

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	/// <summary>Scenario 27 - the compatibility floor: a legacy profile with no active icon provider still
	/// reaches a pre-feature device provider exactly as it always has.</summary>
	[Test]
	public async Task An_icon_pack_icon_still_reaches_a_device_as_a_bare_guid_in_icon_id()
	{
		_fixture.Icons.Add(new IconEntity
		{
			Id = Guid.Parse(IconA), PackId = Guid.NewGuid(), Name = "icon-a", MasterContentHash = "hash-a"
		});
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0, label: "Record", iconId: IconA));

		var deviceId = await _fixture.OpenDeviceAsync();

		var appearance = _fixture.Provider.Latest(deviceId).Widgets.Single().Appearance!;
		Assert.Multiple(() =>
		{
			Assert.That(appearance.IconId, Is.EqualTo(IconA));
			Assert.That(appearance.IconVersion, Is.EqualTo("hash-a"));
			Assert.That(appearance.HasProviderIcon, Is.False);
			Assert.That(appearance.Label, Is.EqualTo("Record"));
		});
	}

	/// <summary>Scenario 28 - the counterexample: an active icon provider overrides a widget that already
	/// has a manually configured icon-pack icon, and the id it reaches the device under is never widened
	/// to carry the provider's identity.</summary>
	[Test]
	public async Task A_provider_controlled_icon_never_widens_icon_id_and_its_version_moves_between_pushes()
	{
		var widgetId = Guid.NewGuid();
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button(widgetId.ToString(), 0, 0, iconId: IconA));

		var resourceName = $"app.macro-deck.widget-icon-provider.{widgetId:N}";
		var resourceV1 = new UiResource
		{
			ResourceId = resourceName, ContentHash = "sha256:v1", MediaType = "image/png", ByteLength = 3
		};
		_fixture.WidgetIcons.Set(widgetId, WidgetIconResolution.Active(resourceV1));

		var deviceId = await _fixture.OpenDeviceAsync();
		var first = _fixture.Provider.Latest(deviceId).Widgets.Single().Appearance!;

		var resourceV2 = resourceV1 with { ContentHash = "sha256:v2" };
		await _fixture.MutateAsync(() => _fixture.WidgetIcons.Set(widgetId, WidgetIconResolution.Active(resourceV2)));
		var second = _fixture.Provider.Latest(deviceId).Widgets.Single().Appearance!;

		Assert.Multiple(() =>
		{
			Assert.That(first.IconId, Is.Null, "never a reference string, never a synthesised pseudo-GUID");
			Assert.That(first.HasProviderIcon, Is.True);
			Assert.That(first.IconVersion, Is.EqualTo("sha256:v1"));

			Assert.That(second.IconId, Is.Null);
			Assert.That(second.HasProviderIcon, Is.True);
			Assert.That(second.IconVersion, Is.EqualTo("sha256:v2"));

			Assert.That(second.IconVersion,
				Is.Not.EqualTo(first.IconVersion),
				"the reported content identity must change between the two pushes");
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(2));
		});
	}
}
