using MacroDeckHost.Application.Deck;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceSurfaceClientTrackingTests
{
	private DeviceSurfaceFixture _fixture = null!;

	[SetUp]
	public void SetUp() => _fixture = new DeviceSurfaceFixture();

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	private IReadOnlyList<MacroDeck.Sdk.Decks.DeckClient> Listed() => _fixture.DeckClients.Snapshot();

	[Test]
	public async Task A_freshly_opened_device_is_listed_on_its_start_folder_without_navigating()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		var client = Listed().Single();
		Assert.Multiple(() =>
		{
			Assert.That(client.ClientId, Is.EqualTo(DeviceOrigin.For(deviceId)));
			Assert.That(client.DeviceId, Is.EqualTo(deviceId.ToString("D")));
			Assert.That(client.ProfileId, Is.EqualTo(DeviceSurfaceFixture.StudioProfileId));
			Assert.That(client.FolderId, Is.EqualTo(_fixture.Home.Id));
		});
	}

	[Test]
	public async Task Navigating_moves_the_listed_device()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.ChangeToAsync(deviceId, _fixture.Lights.Id);

		Assert.That(Listed().Single().FolderId, Is.EqualTo(_fixture.Lights.Id));
	}

	[Test]
	public async Task A_device_whose_folder_is_deleted_is_listed_on_the_folder_it_fell_back_to()
	{
		var deviceId = await _fixture.OpenDeviceAsync();
		await _fixture.ChangeToAsync(deviceId, _fixture.Lights.Id);

		await _fixture.MutateAsync(() => _fixture.SetFolders(DeviceSurfaceFixture.StudioProfileId,
			_fixture.Home,
			_fixture.Scenes,
			_fixture.Audio));

		Assert.That(Listed().Single().FolderId, Is.Not.EqualTo(_fixture.Lights.Id));
	}

	[Test]
	public async Task A_closed_device_is_not_listed_even_when_a_scheduled_rebuild_runs_afterwards()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.Service.InvalidateAsync();
		await _fixture.Service.CloseAsync(deviceId, reason: null);
		await _fixture.SettleAsync();

		Assert.That(Listed(), Is.Empty);
	}

	[Test]
	public async Task A_device_going_offline_leaves_the_list_and_stays_out_through_a_rebuild_until_it_returns()
	{
		var deviceId = await _fixture.OpenDeviceAsync();

		await _fixture.Service.SetPresenceAsync(deviceId, online: false);
		var listedWhileOffline = Listed();
		await _fixture.MutateAsync(() => { });
		var listedAfterRebuild = Listed();
		await _fixture.Service.SetPresenceAsync(deviceId, online: true);

		Assert.Multiple(() =>
		{
			Assert.That(listedWhileOffline, Is.Empty);
			Assert.That(listedAfterRebuild, Is.Empty);
			Assert.That(Listed().Single().FolderId, Is.EqualTo(_fixture.Home.Id));
		});
	}

	[Test]
	public async Task A_device_opened_while_offline_is_not_listed()
	{
		var deviceId = _fixture.AddDevice("DeckB");
		_fixture.Presence.Set(deviceId, online: false);

		await _fixture.Service.OpenAsync(deviceId, _fixture.Provider.ProviderId, "DeckB");

		Assert.That(Listed(), Is.Empty);
	}

	[Test]
	public async Task A_device_whose_profile_does_not_resolve_is_not_listed()
	{
		await _fixture.OpenDeviceAsync("DeckC", profileId: "no-such-profile");

		Assert.That(Listed(), Is.Empty);
	}
}
