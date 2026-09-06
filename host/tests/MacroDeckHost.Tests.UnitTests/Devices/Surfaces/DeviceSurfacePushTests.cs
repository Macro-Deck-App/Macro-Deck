using System.Text.Json;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

/// <summary>
/// When the host pushes a new surface and when it deliberately does not, and that one device's session
/// is never observable from another's.
/// </summary>
[TestFixture]
internal sealed class DeviceSurfacePushTests
{
	private static readonly long[] _expectedNavigationRevisions = [2, 3, 4];

	private static readonly string[] _expectedAfterAdd = ["w1", "w2", "w3", "w4"];

	private static readonly string[] _expectedAfterDelete = ["w1", "w2", "w3"];

	private DeviceSurfaceFixture _fixture = null!;

	[SetUp]
	public void SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
		_fixture.Home.Widgets.AddRange([
			DeviceSurfaceFixture.Button("w1", 0, 0),
			DeviceSurfaceFixture.Button("w2", 1, 0),
			DeviceSurfaceFixture.Button("w3", 2, 0)
		]);
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public async Task Navigating_one_device_leaves_another_where_it_was_and_pushes_it_nothing()
	{
		var deckA = await _fixture.OpenDeviceAsync("DeckA");
		var deckB = await _fixture.OpenDeviceAsync("DeckB");

		await _fixture.ChangeToAsync(deckA, DeviceSurfaceFixture.LightsFolderId);
		await _fixture.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.Latest(deckA).Folder!.Id, Is.EqualTo(DeviceSurfaceFixture.LightsFolderId));
			Assert.That(_fixture.Provider.Latest(deckB).Folder!.Id, Is.EqualTo(DeviceSurfaceFixture.HomeFolderId));
			Assert.That(_fixture.Provider.PushCount(deckB), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Each_device_walks_its_own_history_back()
	{
		var deckA = await _fixture.OpenDeviceAsync("DeckA");
		var deckB = await _fixture.OpenDeviceAsync("DeckB");

		await _fixture.ChangeToAsync(deckA, DeviceSurfaceFixture.LightsFolderId);
		await _fixture.ChangeToAsync(deckA, DeviceSurfaceFixture.ScenesFolderId);
		await _fixture.ChangeToAsync(deckB, DeviceSurfaceFixture.AudioFolderId);

		await _fixture.BackAsync(deckA);
		await _fixture.BackAsync(deckB);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.Latest(deckA).Folder!.Id, Is.EqualTo(DeviceSurfaceFixture.LightsFolderId));
			Assert.That(_fixture.Provider.Latest(deckB).Folder!.Id, Is.EqualTo(DeviceSurfaceFixture.HomeFolderId));
		});
	}

	[Test]
	public async Task Revisions_advance_only_on_the_device_that_moved()
	{
		var deckA = await _fixture.OpenDeviceAsync("DeckA");
		var deckB = await _fixture.OpenDeviceAsync("DeckB");

		await _fixture.ChangeToAsync(deckA, DeviceSurfaceFixture.LightsFolderId);
		await _fixture.ChangeToAsync(deckA, DeviceSurfaceFixture.ScenesFolderId);
		await _fixture.ChangeToAsync(deckA, DeviceSurfaceFixture.AudioFolderId);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.SurfacesFor(deckA).Skip(1).Select(surface => surface.Revision),
				Is.EqualTo(_expectedNavigationRevisions));
			Assert.That(_fixture.Provider.PushCount(deckB), Is.EqualTo(1));
			Assert.That(_fixture.Provider.Latest(deckB).Revision, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_widget_added_to_the_current_folder_arrives_in_one_push()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.MutateAsync(() => _fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w4", 0, 1)));

		var surface = _fixture.Provider.Latest(deviceId);
		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(2));
			Assert.That(surface.Revision, Is.EqualTo(2));
			Assert.That(surface.Widgets.Select(widget => widget.Id), Is.EqualTo(_expectedAfterAdd));
			Assert.That(surface.Widgets.Single(widget => widget.Id == "w4").PositionY, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_deleted_widget_leaves_the_surface()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w4", 0, 1));
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.MutateAsync(() => _fixture.Home.Widgets.RemoveAll(widget => widget.Id == "w4"));

		var surface = _fixture.Provider.Latest(deviceId);
		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(2));
			Assert.That(surface.Revision, Is.EqualTo(2));
			Assert.That(surface.Widgets.Select(widget => widget.Id), Is.EqualTo(_expectedAfterDelete));
		});
	}

	[Test]
	public async Task A_moved_widget_keeps_its_identity_at_its_new_position()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.MutateAsync(() =>
		{
			var widget = _fixture.Home.Widgets.Single(candidate => candidate.Id == "w1");
			widget.PositionX = 2;
			widget.PositionY = 0;
		});

		var moved = _fixture.Provider.Latest(deviceId).Widgets.Single(widget => widget.Id == "w1");
		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(2));
			Assert.That(_fixture.Provider.Latest(deviceId).Revision, Is.EqualTo(2));
			Assert.That(moved.PositionX, Is.EqualTo(2));
			Assert.That(moved.PositionY, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task A_resized_widget_reports_its_new_extent()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.MutateAsync(() =>
		{
			var widget = _fixture.Home.Widgets.Single(candidate => candidate.Id == "w1");
			widget.Width = 2;
			widget.Height = 2;
		});

		var resized = _fixture.Provider.Latest(deviceId).Widgets.Single(widget => widget.Id == "w1");
		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(2));
			Assert.That(resized.Width, Is.EqualTo(2));
			Assert.That(resized.Height, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Advancing_a_widgets_state_delivers_the_resolved_face_rather_than_the_stored_blob()
	{
		var widgetId = Guid.NewGuid();
		var offIcon = Guid.NewGuid().ToString();
		var onIcon = Guid.NewGuid().ToString();
		var stateful = DeviceSurfaceFixture.StatefulButton(widgetId.ToString(), 1, 1, offIcon, onIcon);
		_fixture.Home.Widgets.Add(stateful);
		_fixture.WidgetStates.Set(widgetId, "off", "Idle");

		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.MutateAsync(() => _fixture.WidgetStates.Set(widgetId, "on", "Recording"));

		var surface = _fixture.Provider.Latest(deviceId);
		var widget = surface.Widgets.Single(candidate => candidate.Id == widgetId.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(2));
			Assert.That(surface.Revision, Is.EqualTo(2));
			Assert.That(widget.StateId, Is.EqualTo("on"));
			Assert.That(widget.StateLabel, Is.EqualTo("Recording"));
			Assert.That(widget.Appearance!.Label, Is.EqualTo("Recording"));
			Assert.That(widget.Appearance.IconId, Is.EqualTo(onIcon));
			Assert.That(widget.PositionX, Is.EqualTo(1));
			Assert.That(widget.PositionY, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Layout_edits_on_the_current_folder_reach_the_device()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.MutateAsync(() => _fixture.Home.WidgetSpacing = 4);
		var afterSpacing = _fixture.Provider.Latest(deviceId);

		await _fixture.MutateAsync(() => _fixture.Home.Rows = 3);
		var afterRows = _fixture.Provider.Latest(deviceId);

		Assert.Multiple(() =>
		{
			Assert.That(afterSpacing.Layout.WidgetSpacing, Is.EqualTo(4));
			Assert.That(afterSpacing.Revision, Is.EqualTo(2));
			Assert.That(afterRows.Layout.Rows, Is.EqualTo(3));
			Assert.That(afterRows.Revision, Is.EqualTo(3));
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(3));
		});
	}

	[Test]
	public async Task Reassigning_a_devices_profile_moves_it_to_the_new_profiles_start_folder()
	{
		var deckA = await _fixture.OpenDeviceAsync("DeckA");
		var deckB = await _fixture.OpenDeviceAsync("DeckB");

		await _fixture.MutateAsync(()
			=> _fixture.Devices.Devices.Single(device => device.Id == deckA).StartupProfileId =
				DeviceSurfaceFixture.StageProfileId);

		var surface = _fixture.Provider.Latest(deckA);
		Assert.Multiple(() =>
		{
			Assert.That(surface.Profile!.Id, Is.EqualTo(DeviceSurfaceFixture.StageProfileId));
			Assert.That(surface.Folder!.Id, Is.EqualTo(DeviceSurfaceFixture.StageHomeFolderId));
			Assert.That(_fixture.Provider.PushCount(deckB), Is.EqualTo(1));
			Assert.That(_fixture.Provider.Latest(deckB).Profile!.Id, Is.EqualTo(DeviceSurfaceFixture.StudioProfileId));
		});
	}

	[Test]
	public async Task A_change_in_a_folder_the_device_is_not_on_pushes_nothing()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.MutateAsync(() => _fixture.Audio.Widgets.Add(DeviceSurfaceFixture.Button("elsewhere", 0, 0)));

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(1));
			Assert.That(_fixture.Provider.Latest(deviceId).Revision, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Editing_a_widget_pinned_into_the_current_folder_does_push()
	{
		_fixture.Audio.Widgets.Add(DeviceSurfaceFixture.Button("pinned",
			0,
			1,
			label: "Before",
			isPinned: true,
			pinScope: PinScope.Profile));
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.MutateAsync(() =>
		{
			var widget = _fixture.Audio.Widgets.Single(candidate => candidate.Id == "pinned");
			widget.Data = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["label"] = "After"
			});
		});

		var widget = _fixture.Provider.Latest(deviceId).Widgets.Single(candidate => candidate.Id == "pinned");
		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(2));
			Assert.That(widget.Appearance!.Label, Is.EqualTo("After"));
		});
	}

	[Test]
	public async Task Editing_a_widgets_flows_changes_nothing_the_device_renders()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.MutateAsync(() =>
		{
			var widget = _fixture.Home.Widgets.Single(candidate => candidate.Id == "w1");
			widget.Data = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["label"] = "Button",
				["flows"] = new[]
				{
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["triggerType"] = "onShortPress",
						["nodes"] = new[] { new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = "n1" } }
					}
				}
			});
		});

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(1));
			Assert.That(_fixture.Provider.Latest(deviceId).Revision, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_idle_session_neither_fetches_nor_pushes()
	{
		var deviceId = await _fixture.OpenDeviceAsync();
		var readsAfterOpen = _fixture.Devices.Reads;

		_fixture.Time.Advance(TimeSpan.FromSeconds(60));
		await DeviceSurfaceFixture.DrainAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(1));
			Assert.That(_fixture.Devices.Reads,
				Is.EqualTo(readsAfterOpen),
				"an idle session is driven by invalidation, never by polling");
		});
	}

	[Test]
	public async Task A_reconnecting_device_gets_a_complete_snapshot_from_revision_one()
	{
		var deviceId = await _fixture.OpenDeviceAsync();
		await _fixture.Service.CloseAsync(deviceId, "gone");

		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("added-while-away", 0, 1));
		await _fixture.Service.OpenAsync(deviceId, _fixture.Provider.ProviderId, "DeckA");

		var surface = _fixture.Provider.Latest(deviceId);
		Assert.Multiple(() =>
		{
			Assert.That(surface.Revision, Is.EqualTo(1));
			Assert.That(surface.Widgets.Select(widget => widget.Id),
				Does.Contain("added-while-away").And.Contains("w1"));
			Assert.That(surface.Widgets, Has.Count.EqualTo(4));
		});
	}
}
