using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

/// <summary>Per-device deck navigation, including the client's own history quirks.</summary>
[TestFixture]
internal sealed class DeviceSurfaceNavigationTests
{
	private DeviceSurfaceFixture _fixture = null!;

	[SetUp]
	public void SetUp() => _fixture = new DeviceSurfaceFixture();

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public async Task Changing_folder_moves_only_the_addressed_device_and_records_where_it_came_from()
	{
		var deckA = await _fixture.OpenDeviceAsync("DeckA");
		var deckB = await _fixture.OpenDeviceAsync("DeckB");

		await _fixture.ChangeToAsync(deckA, DeviceSurfaceFixture.LightsFolderId);
		var afterChange = _fixture.Provider.Latest(deckA).Folder!.Id;

		await _fixture.BackAsync(deckA);

		Assert.Multiple(() =>
		{
			Assert.That(afterChange, Is.EqualTo(DeviceSurfaceFixture.LightsFolderId));
			Assert.That(_fixture.Provider.Latest(deckA).Folder!.Id, Is.EqualTo(DeviceSurfaceFixture.HomeFolderId));
			Assert.That(_fixture.Provider.PushCount(deckB), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Device_navigation_reports_the_stable_device_id_not_only_the_client_encoding()
	{
		// A device session's client id is the device wrapped in DeviceOrigin's "device:" encoding, which
		// a user's condition must never have to unwrap.
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.LightsFolderId);

		var occurrence = _fixture.Bus.Published
			.Single(o => o.EventId.EndsWith(EventIds.FolderChanged, StringComparison.Ordinal));
		Assert.Multiple(() =>
		{
			Assert.That(occurrence.Parameters["deviceId"], Is.EqualTo(deviceId.ToString()));
			Assert.That(occurrence.Parameters["clientId"], Is.EqualTo(DeviceOrigin.For(deviceId)));
		});
	}

	[Test]
	public async Task Going_to_the_parent_of_a_root_folder_does_nothing_at_all()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		var moved = await _fixture.ParentAsync(deviceId);

		Assert.Multiple(() =>
		{
			Assert.That(moved, Is.False);
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(1));
			Assert.That(_fixture.Provider.Latest(deviceId).Revision, Is.EqualTo(1));
			Assert.That(_fixture.Provider.Latest(deviceId).Folder!.Id, Is.EqualTo(DeviceSurfaceFixture.HomeFolderId));
		});
	}

	[Test]
	public async Task Parent_pushes_history_the_way_the_client_does_and_back_does_not()
	{
		var deviceId = await _fixture.OpenDeviceAsync();
		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.LightsFolderId);
		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.ScenesFolderId);

		await _fixture.ParentAsync(deviceId);
		var afterParent = _fixture.Provider.Latest(deviceId).Folder!.Id;

		await _fixture.BackAsync(deviceId);
		var afterFirstBack = _fixture.Provider.Latest(deviceId).Folder!.Id;

		await _fixture.BackAsync(deviceId);
		var afterSecondBack = _fixture.Provider.Latest(deviceId).Folder!.Id;

		Assert.Multiple(() =>
		{
			Assert.That(afterParent, Is.EqualTo(DeviceSurfaceFixture.LightsFolderId));
			Assert.That(afterFirstBack, Is.EqualTo(DeviceSurfaceFixture.ScenesFolderId));
			Assert.That(afterSecondBack, Is.EqualTo(DeviceSurfaceFixture.LightsFolderId));
		});
	}

	[Test]
	public async Task History_keeps_the_newest_fifty_entries_and_stops_dead_once_they_are_spent()
	{
		var folders = new List<Folder> { _fixture.Home };
		folders.AddRange(Enumerable.Range(1, 55)
			.Select(index => new Folder
			{
				Id = $"F{index}",
				Name = $"F{index}",
				ProfileId = DeviceSurfaceFixture.StudioProfileId,
				ParentId = DeviceSurfaceFixture.HomeFolderId,
				Order = index
			}));
		_fixture.SetFolders(DeviceSurfaceFixture.StudioProfileId, [.. folders]);

		var deviceId = await _fixture.OpenDeviceAsync();
		for (var index = 1; index <= 55; index++)
		{
			await _fixture.ChangeToAsync(deviceId, $"F{index}");
		}

		var landings = new List<string>();
		for (var step = 0; step < 50; step++)
		{
			await _fixture.BackAsync(deviceId);
			landings.Add(_fixture.Provider.Latest(deviceId).Folder!.Id);
		}

		var pushesBeforeExhaustedBack = _fixture.Provider.PushCount(deviceId);
		var exhausted = await _fixture.BackAsync(deviceId);

		Assert.Multiple(() =>
		{
			Assert.That(landings,
				Is.EqualTo(Enumerable.Range(5, 50).Reverse().Select(index => $"F{index}").ToArray()));
			Assert.That(exhausted, Is.False);
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(pushesBeforeExhaustedBack));
		});
	}

	[Test]
	public async Task A_cross_profile_jump_and_the_back_out_of_it_carry_the_profile_too()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.ChangeToAsync(deviceId,
			DeviceSurfaceFixture.StageHomeFolderId,
			DeviceSurfaceFixture.StageProfileId);
		var jumped = _fixture.Provider.Latest(deviceId);

		await _fixture.BackAsync(deviceId);
		var returned = _fixture.Provider.Latest(deviceId);

		Assert.Multiple(() =>
		{
			Assert.That(jumped.Profile!.Id, Is.EqualTo(DeviceSurfaceFixture.StageProfileId));
			Assert.That(jumped.Folder!.Id, Is.EqualTo(DeviceSurfaceFixture.StageHomeFolderId));
			Assert.That(returned.Profile!.Id, Is.EqualTo(DeviceSurfaceFixture.StudioProfileId));
			Assert.That(returned.Folder!.Id, Is.EqualTo(DeviceSurfaceFixture.HomeFolderId));
		});
	}
}
