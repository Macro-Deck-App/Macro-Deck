using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class MusicPlayerContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.MusicPlayer,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	[Test]
	public async Task Declare_registers_an_adapter_that_passes_the_capability_validator()
	{
		var integration = await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer> { ["a1"] = new TestMusicPlayer() })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(((IMusicPlayerProvider)integration).GetInstances().Select(i => i.Id), Does.Contain("a1"));
		});
	}

	[Test]
	public async Task Every_operation_round_trips_to_the_sdk_type_the_host_contract_requires()
	{
		var catalogDevicePlayer = new TestMusicPlayerWithCatalogAndDevices();
		var integration = await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer> { ["a1"] = catalogDevicePlayer })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		var provider = (IMusicPlayerProvider)integration;

		Assert.That(provider.GetInstances().Select(i => i.Id), Is.EqualTo(new[] { "a1" }));

		var player = provider.GetPlayer("a1");
		Assert.That(player, Is.Not.Null);

		catalogDevicePlayer.StateToReturn = new MusicPlayerState
		{
			IsConnected = true, PlaybackState = PlaybackState.Playing, TrackName = "Song", ArtworkId = "art-1"
		};
		var state = await player!.GetStateAsync(CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(state.TrackName, Is.EqualTo("Song"));
			Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Playing));
		});

		catalogDevicePlayer.ArtworkToReturn = new MusicPlayerArtwork([1, 2, 3], "image/png");
		var artwork = await player.GetArtworkAsync("art-1", CancellationToken.None);
		Assert.That(artwork!.Data, Is.EqualTo(new byte[] { 1, 2, 3 }));

		await player.PlayAsync(CancellationToken.None);
		await player.PauseAsync(CancellationToken.None);
		await player.TogglePlayPauseAsync(CancellationToken.None);
		await player.NextAsync(CancellationToken.None);
		await player.PreviousAsync(CancellationToken.None);
		await player.SeekAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
		await player.SetVolumeAsync(50, CancellationToken.None);
		await player.SetShuffleAsync(true, CancellationToken.None);
		await player.SetRepeatModeAsync(RepeatMode.Track, CancellationToken.None);

		Assert.That(catalogDevicePlayer.Calls,
			Is.EqualTo(new[]
			{
				"play", "pause", "toggle", "next", "previous", "seek:30", "volume:50", "shuffle:True", "repeat:Track"
			}));

		await ((ICatalogMusicPlayer)player).PlayItemAsync(
			new MusicPlayerCatalogItem("t1", "Track", MusicPlayerCatalogItemKind.Track),
			CancellationToken.None);
		Assert.That(catalogDevicePlayer.Calls, Does.Contain("play-item:t1"));

		catalogDevicePlayer.ItemsToReturn =
			[new MusicPlayerCatalogItem("t1", "Track", MusicPlayerCatalogItemKind.Track)];
		var catalogProvider = (IMusicPlayerCatalogProvider)player;
		var items = await catalogProvider.GetCatalogAsync("a1",
			MusicPlayerCatalogItemKind.Track,
			null,
			CancellationToken.None);
		Assert.That(items.Single().Id, Is.EqualTo("t1"));

		catalogDevicePlayer.DevicesToReturn = [new MusicPlayerDevice("d1", "Speaker")];
		var deviceProvider = (IMusicPlayerDeviceProvider)player;
		var devices = await deviceProvider.GetDevicesAsync(CancellationToken.None);
		Assert.That(devices.Single().Id, Is.EqualTo("d1"));

		await deviceProvider.TransferPlaybackAsync("d1", startPlayback: true, CancellationToken.None);
		Assert.That(catalogDevicePlayer.Calls, Does.Contain("transfer:d1:True"));
	}

	[Test]
	public async Task The_instances_operation_round_trips_the_same_instance_list_describe_carries()
	{
		await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer> { ["a1"] = new TestMusicPlayer() })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		var raw = await InvokeRawAsync(CapabilityKinds.MusicPlayer,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.MusicPlayer.Instances);
		var result = raw!.Value.Deserialize<MusicPlayerInstancesResult>(PluginProtocolJson.Options);

		Assert.That(result!.Instances.Select(i => i.Id), Is.EqualTo(new[] { "a1" }));
	}

	[Test]
	public async Task Catalog_and_devices_only_exist_on_the_instance_that_declared_them()
	{
		var catalogOnly = new TestMusicPlayerWithCatalog();
		var deviceOnly = new TestMusicPlayerWithDevices();
		var plain = new TestMusicPlayer();
		var integration = await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer>
								{ ["catalog"] = catalogOnly, ["devices"] = deviceOnly, ["plain"] = plain })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		var provider = (IMusicPlayerProvider)integration;

		Assert.Multiple(() =>
		{
			Assert.That(provider.GetPlayer("catalog"), Is.InstanceOf<IMusicPlayerCatalogProvider>());
			Assert.That(provider.GetPlayer("catalog"), Is.Not.InstanceOf<IMusicPlayerDeviceProvider>());
			Assert.That(provider.GetPlayer("devices"), Is.InstanceOf<IMusicPlayerDeviceProvider>());
			Assert.That(provider.GetPlayer("devices"), Is.Not.InstanceOf<IMusicPlayerCatalogProvider>());
			Assert.That(provider.GetPlayer("plain"), Is.Not.InstanceOf<IMusicPlayerCatalogProvider>());
			Assert.That(provider.GetPlayer("plain"), Is.Not.InstanceOf<IMusicPlayerDeviceProvider>());
		});
	}

	[Test]
	public async Task Only_the_catalog_instance_is_a_catalog_music_player_and_can_play_items_end_to_end()
	{
		var catalogPlayer = new TestMusicPlayerWithCatalog();
		var deviceOnly = new TestMusicPlayerWithDevices();
		var plain = new TestMusicPlayer();
		var integration = await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer>
								{ ["catalog"] = catalogPlayer, ["devices"] = deviceOnly, ["plain"] = plain })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		var provider = (IMusicPlayerProvider)integration;
		var catalogRemote = provider.GetPlayer("catalog");
		var devicesRemote = provider.GetPlayer("devices");
		var plainRemote = provider.GetPlayer("plain");

		Assert.Multiple(() =>
		{
			Assert.That(catalogRemote, Is.InstanceOf<ICatalogMusicPlayer>());
			Assert.That(devicesRemote, Is.Not.InstanceOf<ICatalogMusicPlayer>());
			Assert.That(plainRemote, Is.Not.InstanceOf<ICatalogMusicPlayer>());
		});

		await ((ICatalogMusicPlayer)catalogRemote!).PlayItemAsync(
			new MusicPlayerCatalogItem("t1", "Track", MusicPlayerCatalogItemKind.Track),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(catalogPlayer.Calls, Does.Contain("play-item:t1"));
			Assert.That(deviceOnly.Calls.Any(call => call.StartsWith("play-item:", StringComparison.Ordinal)),
				Is.False);
			Assert.That(plain.Calls.Any(call => call.StartsWith("play-item:", StringComparison.Ordinal)), Is.False);
		});
	}

	[Test]
	public async Task A_timeout_degrades_state_but_catalog_throws_instead_of_degrading()
	{
		var player = new TestMusicPlayerWithCatalogAndDevices { StateOverride = NeverReplies };
		var integration = await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer> { ["a1"] = player })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		var remotePlayer = ((IMusicPlayerProvider)integration).GetPlayer("a1")!;

		var stateTask = remotePlayer.GetStateAsync(CancellationToken.None);
		Time.Advance(ProtocolTimeouts.CapabilityInvoke);
		var state = await stateTask;
		Assert.That(state.IsUnavailable, Is.True);

		player.CatalogOverride = NeverRepliesCatalog;
		var catalogTask = ((IMusicPlayerCatalogProvider)remotePlayer).GetCatalogAsync("a1",
			MusicPlayerCatalogItemKind.Track,
			null,
			CancellationToken.None);
		Time.Advance(ProtocolTimeouts.CapabilityInvoke);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await catalogTask);
		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.Timeout));
	}

	[Test]
	public async Task A_dropped_connection_degrades_state_but_catalog_and_devices_throw_instead_of_degrading()
	{
		var player = new TestMusicPlayerWithCatalogAndDevices();
		var integration = await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer> { ["a1"] = player })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		var remotePlayer = ((IMusicPlayerProvider)integration).GetPlayer("a1")!;

		Disconnect();

		var state = await remotePlayer.GetStateAsync(CancellationToken.None);
		Assert.That(state.IsUnavailable, Is.True);

		Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await ((IMusicPlayerCatalogProvider)remotePlayer).GetCatalogAsync("a1",
				MusicPlayerCatalogItemKind.Track,
				null,
				CancellationToken.None));
		Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await ((IMusicPlayerDeviceProvider)remotePlayer).GetDevicesAsync(CancellationToken.None));
	}

	[Test]
	public async Task Caller_cancellation_puts_capability_cancel_on_the_wire()
	{
		var stillRunning = new TaskCompletionSource<MusicPlayerState>();
		var player = new TestMusicPlayer { StateOverride = (ct) => stillRunning.Task.WaitAsync(ct) };
		var integration = await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer> { ["a1"] = player })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		using var cts = new CancellationTokenSource();
		var remotePlayer = ((IMusicPlayerProvider)integration).GetPlayer("a1")!;
		var stateTask = remotePlayer.GetStateAsync(cts.Token);

		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await stateTask);
		Assert.That(Link.SentByHost.Any(envelope => envelope.Type == MessageTypes.CapabilityCancel), Is.True);

		stillRunning.TrySetResult(MusicPlayerState.Disconnected);
	}

	[Test]
	public async Task A_throwing_handler_yields_a_redacted_internal_error()
	{
		var player = new TestMusicPlayer { ThrowOnState = new InvalidOperationException("boom: token=abc123") };
		await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer> { ["a1"] = player })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.MusicPlayer,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.MusicPlayer.State,
			new { instanceId = "a1" }));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
			Assert.That(exception.Message, Does.Not.Contain("boom"));
			Assert.That(exception.Message, Does.Not.Contain("token=abc123"));
		});
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer> { ["a1"] = new TestMusicPlayer() })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider()],
			[CapabilityKinds.MusicPlayer]);

		var unknownLocalId = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.MusicPlayer,
			"nope",
			CapabilityOperations.MusicPlayer.State,
			new { instanceId = "a1" }));
		var unknownOperation = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.MusicPlayer,
			ProviderCapabilityId.LocalId,
			"rewind",
			new { instanceId = "a1" }));

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	private static async Task<MusicPlayerState> NeverReplies(CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		return MusicPlayerState.Disconnected;
	}

	private static async Task<IReadOnlyList<MusicPlayerCatalogItem>> NeverRepliesCatalog(
		CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		return [];
	}
}
