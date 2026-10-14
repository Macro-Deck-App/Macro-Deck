using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class MusicPlayerReaderContractTests
{
	private const string PluginId = "com.example.music-reader";

	private CallbackHostLink? _link;
	private Task<ConnectionOutcome>? _run;
	private RemoteMusicPlayerApi? _reader;

	[TearDown]
	public async Task TearDown()
	{
		_reader?.Dispose();
		if (_run is not null && _link is not null)
		{
			await _link.DisposeAsync();
			await _run;
			_run.Dispose();
		}
	}

	[Test]
	public async Task A_plugin_reads_the_album_colour_of_another_providers_player()
	{
		var reader = await ConnectAsync(new FixedAlbumColor());

		var color = await reader.GetAlbumColorAsync("spotify::alice");
		var none = await reader.GetAlbumColorAsync("nobody::x");

		Assert.Multiple(() =>
		{
			Assert.That(color, Is.EqualTo("#102030"));
			Assert.That(none, Is.Null);
		});
	}

	[Test]
	public async Task The_players_the_host_pushes_reach_the_plugin_with_every_field()
	{
		var reader = await ConnectAsync(new FixedAlbumColor());
		var raised = 0;
		reader.Lifecycle.InstancesChanged += (_, _) => Interlocked.Increment(ref raised);

		_link!.PushHostState(HostApis.MusicPlayers,
			new MusicPlayersStateDto
			{
				Instances =
				[
					.. MusicPlayerInstanceSnapshots
						.From([
							new MusicPlayerInstanceDescriptor("spotify::alice",
								"spotify",
								"Spotify",
								"Spotify (alice)",
								true)
						])
						.Select(MusicPlayerWire.ToDto)
				],
				Revision = 1
			});
		await WaitForAsync(() => Volatile.Read(ref raised) == 1);

		var player = reader.Instances.Single();
		Assert.Multiple(() =>
		{
			Assert.That(player.Id, Is.EqualTo("spotify::alice"));
			Assert.That(player.ProviderId, Is.EqualTo("spotify"));
			Assert.That(player.ProviderName.Literal, Is.EqualTo("Spotify"));
			Assert.That(player.DisplayName, Is.EqualTo("Spotify (alice)"));
		});
	}

	[Test]
	public async Task A_host_that_predates_the_api_gives_no_players_and_no_colour_instead_of_failing()
	{
		var reader = await ConnectAsync(null);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(reader.Instances, Is.Empty);
			Assert.That(await reader.GetAlbumColorAsync("spotify::alice"), Is.Null);
		});
	}

	private async Task<RemoteMusicPlayerApi> ConnectAsync(IMusicPlayerAlbumColor? albumColor)
	{
		var sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		await sessions.Create(new PluginSessionRecord
		{
			SessionId = "session-music",
			PluginId = PluginId,
			DisplayName = "Music Reader",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});

		var router = new PluginCallbackRouter(sessions,
			new CallbackFakeCapabilityInvoker(),
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new CallbackFakeNotificationStore(),
			new CallbackFakeDeckNavigator(),
			new CallbackFakeScriptApi(),
			new CallbackFakeWidgetApi(),
			new CallbackFakeWidgetIconInvalidator(),
			new CallbackFakeUserVariableApi(),
			new CallbackFakeActionInteractions(),
			new RecordingUiSessionSink(),
			new RecordingDeviceRegistry(),
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System, 1000, 1000),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None,
			albumColor: albumColor);

		var state = new PluginConnectionState();
		var hostInvoker = new HostInvoker(state, TimeProvider.System, Serilog.Core.Logger.None);
		var stateCache = new HostStateCache(state);
		_link = new CallbackHostLink(PluginId, "session-music", router, new CallbackFakeEventBus());

		var connection = new PluginSessionConnection(_link,
			PluginTestSession.Create("session-music", 1, DateTimeOffset.UnixEpoch),
			PluginTestSession.Dispatcher([], state, TimeProvider.System),
			state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker,
			stateCache);

		state.ActiveConnection = connection;
		_run = connection.RunAsync(null, "music-fixture", CancellationToken.None);
		await WaitForAsync(() => state.IsReady);

		_reader = new RemoteMusicPlayerApi(stateCache, hostInvoker, Serilog.Core.Logger.None);
		return _reader;
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(5);
		}

		Assert.Fail("The condition was never met.");
	}

	private sealed class FixedAlbumColor : IMusicPlayerAlbumColor
	{
		public Task<string?> GetAsync(string instanceId, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(instanceId == "spotify::alice" ? "#102030" : null);
		}
	}
}
