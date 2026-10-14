using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using IMusicPlayer = MacroDeck.Sdk.MusicPlayer.IMusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerApiTests
{
	private const string PluginId = "com.example.reader";

	[Test]
	public void A_built_in_reader_sees_every_providers_players_including_plugins()
	{
		var registry = new Registry();
		registry.Instances.Add(Descriptor("spotify::alice", "Spotify (alice)"));
		registry.Instances.Add(Descriptor("net.example.jukebox::default", "Jukebox"));
		using var api = new InProcessMusicPlayerApi(registry,
			new FixedColor(),
			new MusicPlayerStateNotifier(),
			Serilog.Core.Logger.None);

		Assert.That(api.Instances.Select(instance => (instance.Id, instance.ProviderId, instance.DisplayName)),
			Is.EqualTo(new[]
			{
				("spotify::alice", "spotify", "Spotify (alice)"),
				("net.example.jukebox::default", "net.example.jukebox", "Jukebox")
			}));
	}

	[Test]
	public async Task InstancesChanged_is_raised_when_the_players_change_and_not_when_only_widget_options_do()
	{
		var registry = new Registry();
		var notifier = new MusicPlayerStateNotifier();
		using var api = new InProcessMusicPlayerApi(registry, new FixedColor(), notifier, Serilog.Core.Logger.None);
		var raised = 0;
		api.InstancesChanged += (_, _) => Interlocked.Increment(ref raised);

		notifier.PublishInstancesChanged();
		registry.Instances.Add(Descriptor("spotify::alice", "Spotify (alice)"));
		notifier.PublishInstancesChanged();

		await WaitForAsync(() => Volatile.Read(ref raised) == 1);
		await Task.Delay(100);
		Assert.That(Volatile.Read(ref raised), Is.EqualTo(1));
	}

	[Test]
	public async Task A_reinitialized_integration_no_longer_receives_the_previous_handlers()
	{
		var registry = new Registry();
		var notifier = new MusicPlayerStateNotifier();
		var api = new InProcessMusicPlayerApi(registry, new FixedColor(), notifier, Serilog.Core.Logger.None);
		var raised = 0;
		api.InstancesChanged += (_, _) => Interlocked.Increment(ref raised);

		api.Dispose();
		registry.Instances.Add(Descriptor("spotify::alice", "Spotify (alice)"));
		notifier.PublishInstancesChanged();

		await Task.Delay(100);
		Assert.That(Volatile.Read(ref raised), Is.Zero);
	}

	[Test]
	public async Task Every_connected_plugin_receives_all_players_on_registration_and_after_a_change()
	{
		var registry = new Registry();
		registry.Instances.Add(Descriptor("spotify::alice", "Spotify (alice)"));
		var notifier = new MusicPlayerStateNotifier();
		var (pusher, connection) = await PusherAsync(registry, notifier);
		using var _ = pusher;

		await pusher.PushAllAsync(PluginId);
		registry.Instances.Add(Descriptor("net.example.jukebox::default", "Jukebox"));
		notifier.PublishInstancesChanged();
		await WaitForAsync(() => Pushes(connection).Count == 2);
		notifier.PublishInstancesChanged();
		await Task.Delay(100);

		var pushes = Pushes(connection);
		Assert.Multiple(() =>
		{
			Assert.That(pushes, Has.Count.EqualTo(2), "an unchanged list is not pushed again");
			Assert.That(pushes[0].Instances.Select(instance => instance.Id), Is.EqualTo(new[] { "spotify::alice" }));
			Assert.That(pushes[1].Instances.Select(instance => instance.Id),
				Is.EqualTo(new[] { "spotify::alice", "net.example.jukebox::default" }));
			Assert.That(pushes[1].Revision, Is.GreaterThan(pushes[0].Revision));
		});
	}

	[Test]
	public async Task A_plugin_reads_the_album_colour_of_any_providers_player()
	{
		var result = await AlbumColorAsync(new FixedColor { Colors = { ["spotify::alice"] = "#102030" } },
			"spotify::alice");

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(result.Data!.Value.Deserialize<MusicPlayerAlbumColorResult>(PluginProtocolJson.Options)!.Color,
				Is.EqualTo("#102030"));
		});
	}

	[Test]
	public async Task A_player_without_a_colour_answers_none()
	{
		var result = await AlbumColorAsync(new FixedColor(), "nobody::x");

		Assert.That(result.Data!.Value.Deserialize<MusicPlayerAlbumColorResult>(PluginProtocolJson.Options)!.Color,
			Is.Null);
	}

	[Test]
	public async Task An_empty_instance_id_is_an_invalid_payload()
	{
		var result = await AlbumColorAsync(new FixedColor(), string.Empty);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	[Test]
	public async Task A_host_without_album_colours_answers_unsupported()
	{
		var result = await AlbumColorAsync(null, "spotify::alice");

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	private static Task<HostCallbackResult> AlbumColorAsync(IMusicPlayerAlbumColor? albumColor, string instanceId)
	{
		return Router(albumColor)
			.RouteAsync(PluginId,
				"session-1",
				Guid.CreateVersion7().ToString(),
				new HostInvokePayload
				{
					Api = HostApis.MusicPlayers,
					Operation = HostOperations.MusicPlayers.AlbumColor,
					Arguments = JsonSerializer.SerializeToElement(
						new MusicPlayerAlbumColorArguments { InstanceId = instanceId },
						PluginProtocolJson.Options)
				},
				CancellationToken.None);
	}

	private static PluginCallbackRouter Router(IMusicPlayerAlbumColor? albumColor)
	{
		return new PluginCallbackRouter(new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None),
			null!,
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			null!,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			null!,
			new NoOpUiSessionSink(),
			new FakePluginDeviceRegistry(),
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			null!,
			new HostCallbackThrottle(TimeProvider.System, 1000, 1000),
			new FakeHostLockState(),
			Serilog.Core.Logger.None,
			albumColor: albumColor);
	}

	private static async Task<(HostStatePusher Pusher, FakePluginConnection Connection)> PusherAsync(
		Registry registry,
		MusicPlayerStateNotifier notifier)
	{
		var sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var connection = new FakePluginConnection();
		var pusher = new HostStatePusher(sessions,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new StubEventBindingTracker(),
			new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
			new FakeAdbManager(),
			new FixedAdbAccessPolicy(PluginAdbAccess.NotEnabled),
			Serilog.Core.Logger.None,
			musicPlayers: registry,
			musicPlayerNotifier: notifier);

		await sessions.Create(new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = PluginId,
			DisplayName = "Reader",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
		sessions.TryAttach("session-1", connection, null);
		return (pusher, connection);
	}

	private static List<MusicPlayersStateDto> Pushes(FakePluginConnection connection)
	{
		ProtocolEnvelope[] sent;
		lock (connection.Sent)
		{
			sent = [.. connection.Sent];
		}

		return sent
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
			.Where(payload => payload.Api == HostApis.MusicPlayers)
			.Select(payload => payload.Data!.Value.Deserialize<MusicPlayersStateDto>(PluginProtocolJson.Options)!)
			.ToList();
	}

	private static MusicPlayerInstanceDescriptor Descriptor(string instanceId, string displayName)
	{
		return new MusicPlayerInstanceDescriptor(instanceId,
			instanceId[..instanceId.IndexOf("::", StringComparison.Ordinal)],
			"Provider",
			displayName,
			false);
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && !condition())
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "the expected state was never reached");
	}

	private sealed class Registry : IMusicPlayerRegistry
	{
		public List<MusicPlayerInstanceDescriptor> Instances { get; } = [];

		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances()
		{
			return [.. Instances];
		}

		public IMusicPlayer? GetPlayer(string instanceId)
		{
			return null;
		}

		public IMusicPlayer? GetPlayerWithOptions(string instanceId, IReadOnlyDictionary<string, object> options)
		{
			return null;
		}

		public IMusicPlayer? DefaultPlayer => null;
	}

	private sealed class FixedColor : IMusicPlayerAlbumColor
	{
		public Dictionary<string, string> Colors { get; } = new(StringComparer.Ordinal);

		public Task<string?> GetAsync(string instanceId, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(Colors.GetValueOrDefault(instanceId));
		}
	}
}
