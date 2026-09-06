using MacroDeck.Sdk.Devices;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

/// <summary>
/// What a device is handed to render: the complete first snapshot, the effective grid it resolves to,
/// and which pinned widgets reach it.
/// </summary>
[TestFixture]
internal sealed class DeviceSurfaceProjectionTests
{
	private static readonly (string, string, int, int, int, int)[] _expectedPlacements =
	[
		("w1", WidgetTypeIds.ActionButton, 0, 0, 1, 1), ("w2", WidgetTypeIds.ActionButton, 1, 0, 2, 1)
	];

	private static readonly string?[] _expectedLabels = ["One", "Two"];

	private static readonly (string, int, int)[] _expectedFarPlacement = [("far", 4, 3)];

	private static readonly string[] _expectedSubtreePin = ["subtree"];

	private DeviceSurfaceFixture _fixture = null!;

	[SetUp]
	public void SetUp() => _fixture = new DeviceSurfaceFixture();

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public async Task The_first_snapshot_is_complete_and_costs_the_provider_no_further_calls()
	{
		_fixture.Home.Widgets.AddRange([
			DeviceSurfaceFixture.Button("w1", 0, 0, label: "One"),
			DeviceSurfaceFixture.Button("w2", 1, 0, width: 2, height: 1, label: "Two")
		]);

		var deviceId = await _fixture.OpenDeviceAsync();

		var surface = _fixture.Provider.Latest(deviceId);
		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(1));
			Assert.That(surface.Revision, Is.EqualTo(1));
			Assert.That(surface.Profile,
				Is.EqualTo(new DeviceSurfaceProfile(DeviceSurfaceFixture.StudioProfileId,
					"Studio")));
			Assert.That(surface.Folder,
				Is.EqualTo(new DeviceSurfaceFolder(DeviceSurfaceFixture.HomeFolderId, "Home", null, true)));
			Assert.That(surface.Layout.LayoutReference, Is.EqualTo(DeviceSurfaceFixture.DeckLayoutReference));
			Assert.That(surface.Widgets.Select(widget
					=> (widget.Id, widget.Type, widget.PositionX, widget.PositionY, widget.Width, widget.Height)),
				Is.EqualTo(_expectedPlacements));
			Assert.That(surface.Widgets.Select(widget => widget.Appearance?.Label), Is.EqualTo(_expectedLabels));
		});
	}

	[Test]
	public async Task A_widget_declares_the_interactions_the_host_will_accept_for_it()
	{
		_fixture.Home.Widgets.AddRange([
			DeviceSurfaceFixture.Button("pressable", 0, 0),
			DeviceSurfaceFixture.Button("inert", 1, 0, withFlows: false)
		]);

		var deviceId = await _fixture.OpenDeviceAsync();

		var widgets = _fixture.Provider.Latest(deviceId).Widgets;
		Assert.Multiple(() =>
		{
			Assert.That(widgets[0].SupportedInteractions,
				Does.Contain(DeviceInteractionKind.Press).And.Contains(DeviceInteractionKind.Release));
			Assert.That(widgets[1].SupportedInteractions, Is.Empty);
		});
	}

	[Test]
	public async Task A_folder_with_no_grid_of_its_own_inherits_its_parents_rather_than_the_profile_default()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.LightsFolderId);

		var layout = _fixture.Provider.Latest(deviceId).Layout;
		Assert.Multiple(() =>
		{
			Assert.That(layout.Rows, Is.EqualTo(2));
			Assert.That(layout.Columns, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task Each_grid_value_is_resolved_independently_up_the_chain()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.ScenesFolderId);

		var layout = _fixture.Provider.Latest(deviceId).Layout;
		Assert.Multiple(() =>
		{
			Assert.That(layout.Rows, Is.EqualTo(4));
			Assert.That(layout.Columns, Is.EqualTo(3));
			Assert.That(layout.WidgetSpacing, Is.EqualTo(2));
			Assert.That(layout.WidgetBorderRadius, Is.EqualTo(12));
			Assert.That(layout.BackgroundColor, Is.EqualTo("#101010"));
		});
	}

	[Test]
	public async Task A_background_is_the_folders_own_or_the_profile_default_and_is_never_inherited()
	{
		// The client resolves a background from the folder itself and then the profile default alone -
		// unlike rows, columns, spacing and radius, which walk the chain on both sides. A device and a
		// browser render the same folder, so the device must not inherit one either.
		_fixture.Home.BackgroundColor = "#ababab";
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.LightsFolderId);
		var inherited = _fixture.Provider.Latest(deviceId).Layout.BackgroundColor;

		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.HomeFolderId);
		var own = _fixture.Provider.Latest(deviceId).Layout.BackgroundColor;

		Assert.Multiple(() =>
		{
			Assert.That(inherited, Is.EqualTo("#101010"));
			Assert.That(own, Is.EqualTo("#ababab"));
		});
	}

	[Test]
	public async Task The_layout_reference_is_echoed_verbatim_and_never_reconciled_with_the_grid()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		var layout = _fixture.Provider.Latest(deviceId).Layout;
		Assert.Multiple(() =>
		{
			Assert.That(layout.LayoutReference, Is.EqualTo(DeviceSurfaceFixture.DeckLayoutReference));
			Assert.That(layout.Rows, Is.EqualTo(2));
			Assert.That(layout.Columns, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task A_grid_larger_than_the_device_is_delivered_whole_rather_than_clipped()
	{
		_fixture.Home.Rows = 4;
		_fixture.Home.Columns = 5;
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("far", 4, 3));

		var deviceId = await _fixture.OpenDeviceAsync();

		var surface = _fixture.Provider.Latest(deviceId);
		Assert.Multiple(() =>
		{
			Assert.That(surface.Layout.Rows, Is.EqualTo(4));
			Assert.That(surface.Layout.Columns, Is.EqualTo(5));
			Assert.That(surface.Widgets.Select(widget => (widget.Id, widget.PositionX, widget.PositionY)),
				Is.EqualTo(_expectedFarPlacement));
			Assert.That(_fixture.Service.IsOpen(deviceId), Is.True);
		});
	}

	[Test]
	public async Task A_profile_pinned_widget_reaches_a_folder_in_another_branch_under_its_own_identity()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("pinned",
			2,
			1,
			isPinned: true,
			pinScope: PinScope.Profile));

		var deviceId = await _fixture.OpenDeviceAsync();
		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.ScenesFolderId);

		var widget = _fixture.Provider.Latest(deviceId).Widgets.Single();
		Assert.Multiple(() =>
		{
			Assert.That(widget.Id, Is.EqualTo("pinned"));
			Assert.That(widget.PositionX, Is.EqualTo(2));
			Assert.That(widget.PositionY, Is.EqualTo(1));
			Assert.That(widget.IsPinned, Is.True);
		});
	}

	[Test]
	public async Task A_subtree_pinned_widget_reaches_only_the_folders_below_its_own()
	{
		_fixture.Lights.Widgets.Add(DeviceSurfaceFixture.Button("subtree",
			0,
			0,
			isPinned: true,
			pinScope: PinScope.Subtree));

		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.ScenesFolderId);
		var inScenes = _fixture.Provider.Latest(deviceId).Widgets.Select(widget => widget.Id).ToArray();

		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.AudioFolderId);
		var inAudio = _fixture.Provider.Latest(deviceId).Widgets.Select(widget => widget.Id).ToArray();

		Assert.Multiple(() =>
		{
			Assert.That(inScenes, Is.EqualTo(_expectedSubtreePin));
			Assert.That(inAudio, Is.Empty);
		});
	}

	[Test]
	public async Task A_pinned_widget_appears_once_in_the_folder_it_lives_in()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("pinned",
			0,
			0,
			isPinned: true,
			pinScope: PinScope.Profile));

		var deviceId = await _fixture.OpenDeviceAsync();

		Assert.That(_fixture.Provider.Latest(deviceId).Widgets.Count(widget => widget.Id == "pinned"),
			Is.EqualTo(1));
	}

	[Test]
	public async Task A_device_with_no_assigned_profile_still_gets_a_session_and_an_empty_surface()
	{
		var deviceId = await _fixture.OpenDeviceAsync("DeckA", profileId: null);

		var surface = _fixture.Provider.Latest(deviceId);
		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Service.IsOpen(deviceId), Is.True);
			Assert.That(surface.Revision, Is.EqualTo(1));
			Assert.That(surface.Profile, Is.Null);
			Assert.That(surface.Folder, Is.Null);
			Assert.That(surface.Widgets, Is.Empty);
		});
	}

	[Test]
	public async Task A_dangling_assigned_profile_is_the_same_as_none()
	{
		var deviceId = await _fixture.OpenDeviceAsync("DeckA", profileId: "deleted-profile");

		var surface = _fixture.Provider.Latest(deviceId);
		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Service.IsOpen(deviceId), Is.True);
			Assert.That(surface.Profile, Is.Null);
			Assert.That(surface.Widgets, Is.Empty);
		});
	}
}
