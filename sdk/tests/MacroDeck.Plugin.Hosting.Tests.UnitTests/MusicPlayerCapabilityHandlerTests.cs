using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.MusicPlayer;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class MusicPlayerCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(string localId, string operation, object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.MusicPlayer,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	[Test]
	public void A_provider_declares_exactly_one_provider_local_id()
	{
		var integration = new TestMusicPlayerIntegration("Spotify",
			new Dictionary<string, IMusicPlayer> { ["a1"] = new TestMusicPlayer() });
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var declared = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Count.EqualTo(1));
			Assert.That(declared[0].LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(declared[0].Kind, Is.EqualTo(CapabilityKinds.MusicPlayer));
		});
	}

	[Test]
	public void No_provider_declares_nothing()
		=> Assert.That(new MusicPlayerCapabilityHandler([], TestMetadata.Default, new FakeAssetUploader())
				.DeclareCapabilities(),
			Is.Empty);

	[Test]
	public async Task Describe_reports_the_provider_name_and_the_catalog_devices_flags_per_instance()
	{
		var catalogOnly = new TestMusicPlayerWithCatalog();
		var plain = new TestMusicPlayer();
		var integration = new TestMusicPlayerIntegration("Spotify",
			new Dictionary<string, IMusicPlayer> { ["a1"] = catalogOnly, ["a2"] = plain });
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, CapabilityOperations.MusicPlayer.Describe),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<MusicPlayerDescribePayload>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.ProviderName, Is.EqualTo("Spotify"));
			Assert.That(payload.Instances.Single(i => i.Id == "a1").HasCatalog, Is.True);
			Assert.That(payload.Instances.Single(i => i.Id == "a1").HasDevices, Is.False);
			Assert.That(payload.Instances.Single(i => i.Id == "a2").HasCatalog, Is.False);
		});
	}

	[Test]
	public async Task Describe_HasCatalog_MeansIMusicPlayerCatalogProvider_NotJustICatalogMusicPlayer()
	{
		var catalogPlayer = new TestMusicPlayerWithCatalog();
		var browseOnly = new TestMusicPlayerBrowseOnly();
		var bare = new TestMusicPlayer();
		var integration = new TestMusicPlayerIntegration("Spotify",
			new Dictionary<string, IMusicPlayer>
				{ ["catalog"] = catalogPlayer, ["browse-only"] = browseOnly, ["bare"] = bare });
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, CapabilityOperations.MusicPlayer.Describe),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<MusicPlayerDescribePayload>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.Instances.Single(i => i.Id == "catalog").HasCatalog, Is.True);
			Assert.That(payload.Instances.Single(i => i.Id == "browse-only").HasCatalog, Is.True);
			Assert.That(payload.Instances.Single(i => i.Id == "bare").HasCatalog, Is.False);
			Assert.That(payload.Instances.Select(i => i.HasDevices), Is.All.False);

			var dtoProperties = typeof(MusicPlayerInstanceDto).GetProperties().Select(p => p.Name).ToArray();
			Assert.That(dtoProperties, Is.EquivalentTo(new[] { "Id", "DisplayName", "HasCatalog", "HasDevices" }));
		});
	}

	[Test]
	public async Task PlayItem_AgainstABarePlayer_FailsWithCapabilityUnavailableAndDoesNotCallTheDecoy()
	{
		var decoy = new TestMusicPlayerDecoy();
		var integration
			= new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer> { ["a1"] = decoy });
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.MusicPlayer.PlayItem,
				new { instanceId = "a1", item = new { id = "t1", title = "t1", kind = "Track" } }),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(result.Error!.Message, Does.Contain("a1"));
			Assert.That(decoy.WasCalled, Is.False);
			Assert.That(decoy.Calls, Does.Not.Contain("play-item:t1"));
		});
	}

	[Test]
	public async Task PlayItem_AgainstACatalogPlayer_Succeeds()
	{
		var player = new TestMusicPlayerWithCatalog();
		var integration
			= new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer> { ["a1"] = player });
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.MusicPlayer.PlayItem,
				new { instanceId = "a1", item = new { id = "t1", title = "t1", kind = "Track" } }),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsFailure, Is.False);
			Assert.That(player.Calls, Does.Contain("play-item:t1"));
			Assert.That(player.PlayedItem?.Kind, Is.EqualTo(MusicPlayerCatalogItemKind.Track));
		});
	}

	[Test]
	public async Task State_round_trips_to_the_dto()
	{
		var player = new TestMusicPlayer
		{
			StateToReturn = new MusicPlayerState
				{ IsConnected = true, PlaybackState = PlaybackState.Playing, TrackName = "Song" }
		};
		var integration
			= new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer> { ["a1"] = player });
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, CapabilityOperations.MusicPlayer.State, new { instanceId = "a1" }),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var dto = result.Data!.Value.Deserialize<MusicPlayerStateDto>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(dto!.TrackName, Is.EqualTo("Song"));
			Assert.That(dto.PlaybackState, Is.EqualTo("Playing"));
		});
	}

	[Test]
	public async Task Play_forwards_to_the_resolved_player()
	{
		var player = new TestMusicPlayer();
		var integration
			= new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer> { ["a1"] = player });
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, CapabilityOperations.MusicPlayer.Play, new { instanceId = "a1" }),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsFailure, Is.False);
			Assert.That(player.Calls, Does.Contain("play"));
		});
	}

	[Test]
	public async Task An_unknown_instance_id_is_unavailable()
	{
		var integration = new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer>());
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.MusicPlayer.Play,
				new { instanceId = "gone" }),
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task Catalog_returns_items_on_success_and_throws_on_failure_instead_of_degrading()
	{
		var withItems = new TestMusicPlayerWithCatalog
		{
			ItemsToReturn = [new MusicPlayerCatalogItem("t1", "Track", MusicPlayerCatalogItemKind.Track)]
		};
		var successIntegration =
			new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer> { ["a1"] = withItems });
		var successHandler
			= new MusicPlayerCapabilityHandler([successIntegration], TestMetadata.Default, new FakeAssetUploader());

		var successResult = await successHandler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.MusicPlayer.Catalog,
				new { instanceId = "a1", kind = "Track" }),
			CancellationToken.None);

		Assert.That(successResult.IsFailure, Is.False);
		var payload = successResult.Data!.Value.Deserialize<MusicPlayerCatalogResult>(PluginProtocolJson.Options);
		Assert.That(payload!.Items.Single().Id, Is.EqualTo("t1"));

		var throwing = new TestMusicPlayerWithCatalog
			{ ThrowOnCatalog = new InvalidOperationException("network down") };
		var throwingIntegration =
			new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer> { ["a1"] = throwing });
		var throwingHandler
			= new MusicPlayerCapabilityHandler([throwingIntegration], TestMetadata.Default, new FakeAssetUploader());

		Assert.ThrowsAsync<InvalidOperationException>(async () => await throwingHandler.InvokeAsync(Invocation(
				ProviderCapabilityId.LocalId,
				CapabilityOperations.MusicPlayer.Catalog,
				new { instanceId = "a1", kind = "Track" }),
			CancellationToken.None));
	}

	[Test]
	public async Task Devices_returns_devices_on_success_and_throws_on_failure_instead_of_degrading()
	{
		var withDevices = new TestMusicPlayerWithDevices
		{
			DevicesToReturn = [new MusicPlayerDevice("d1", "Speaker")]
		};
		var successIntegration =
			new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer> { ["a1"] = withDevices });
		var successHandler
			= new MusicPlayerCapabilityHandler([successIntegration], TestMetadata.Default, new FakeAssetUploader());

		var successResult = await successHandler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.MusicPlayer.Devices,
				new { instanceId = "a1" }),
			CancellationToken.None);

		Assert.That(successResult.IsFailure, Is.False);
		var payload = successResult.Data!.Value.Deserialize<MusicPlayerDevicesResult>(PluginProtocolJson.Options);
		Assert.That(payload!.Devices.Single().Id, Is.EqualTo("d1"));

		var throwing = new TestMusicPlayerWithDevices
			{ ThrowOnDevices = new InvalidOperationException("network down") };
		var throwingIntegration =
			new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer> { ["a1"] = throwing });
		var throwingHandler
			= new MusicPlayerCapabilityHandler([throwingIntegration], TestMetadata.Default, new FakeAssetUploader());

		Assert.ThrowsAsync<InvalidOperationException>(async () => await throwingHandler.InvokeAsync(Invocation(
				ProviderCapabilityId.LocalId,
				CapabilityOperations.MusicPlayer.Devices,
				new { instanceId = "a1" }),
			CancellationToken.None));
	}

	[Test]
	public async Task Catalog_or_devices_against_a_player_that_does_not_support_them_is_unavailable()
	{
		var plain = new TestMusicPlayer();
		var integration
			= new TestMusicPlayerIntegration("Spotify", new Dictionary<string, IMusicPlayer> { ["a1"] = plain });
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var catalogResult = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.MusicPlayer.Catalog,
				new { instanceId = "a1", kind = "Track" }),
			CancellationToken.None);
		var devicesResult = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.MusicPlayer.Devices,
				new { instanceId = "a1" }),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(catalogResult.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(devicesResult.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
		});
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		var integration = new TestMusicPlayerIntegration("Spotify",
			new Dictionary<string, IMusicPlayer> { ["a1"] = new TestMusicPlayer() });
		var handler = new MusicPlayerCapabilityHandler([integration], TestMetadata.Default, new FakeAssetUploader());

		var unknownLocalId = await handler.InvokeAsync(Invocation("nope", CapabilityOperations.MusicPlayer.Instances),
			CancellationToken.None);
		var unknownOperation =
			await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId, "rewind"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}
}
