using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.WebNowPlaying;
using MacroDeckHost.Tests.UnitTests.YtmDesktop;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.WebNowPlaying;

[TestFixture]
internal sealed class WebNowPlayingIntegrationTests
{
	private const string ExtensionOrigin = "chrome-extension://webnowplaying";

	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

	[Test]
	public async Task The_adapter_handshake_is_the_first_frame_the_extension_receives()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);

		Assert.That(await ReceiveTextAsync(extension), Is.EqualTo("ADAPTER_VERSION 3.2.0;WNPLIB_REVISION 3"));
	}

	[Test]
	public async Task An_added_player_becomes_the_music_player_state_and_fills_every_variable()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);
		await SendCoverAsync(extension, 7, [1, 2, 3]);
		await SendTextAsync(extension, Added(7, title: "Never\\|Gonna"));

		var state = await EventuallyAsync(integration, s => s.TrackName is not null);
		var readings = new Dictionary<string, object?>();
		foreach (var variable in integration.Variables)
		{
			readings[variable.ResolvedId!] = (await integration.ReadAsync(variable.ResolvedId!, CancellationToken.None)).Value;
		}

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.TrackName, Is.EqualTo("Never|Gonna"));
			Assert.That(state.Artists, Is.EqualTo(new[] { "Rick" }));
			Assert.That(state.AlbumName, Is.EqualTo("Album"));
			Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Playing));
			Assert.That(state.Position, Is.EqualTo(TimeSpan.FromSeconds(30)));
			Assert.That(state.Duration, Is.EqualTo(TimeSpan.FromSeconds(200)));
			Assert.That(state.VolumePercent, Is.EqualTo(50));
			Assert.That(state.DeviceName, Is.EqualTo("YouTube"));
			Assert.That(readings.Where(reading => reading.Value is null).Select(reading => reading.Key), Is.Empty);
		});
	}

	[Test]
	public async Task A_partial_update_changes_only_the_fields_it_carries()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);
		await SendTextAsync(extension, Added(7));
		await EventuallyAsync(integration, s => s.TrackName is not null);

		await SendTextAsync(extension, "1 7 ||||||" + "|95|" + "||||||||||||||||||");
		var state = await EventuallyAsync(integration, s => s.Position == TimeSpan.FromSeconds(95));

		await SendTextAsync(extension, "1 7 |||\u0001|");
		var cleared = await EventuallyAsync(integration, s => s.Artists.Count == 0);

		Assert.Multiple(() =>
		{
			Assert.That(state.TrackName, Is.EqualTo("Song"));
			Assert.That(state.Artists, Is.EqualTo(new[] { "Rick" }));
			Assert.That(cleared.TrackName, Is.EqualTo("Song"));
		});
	}

	[Test]
	public async Task A_cover_sent_before_its_player_is_served_as_artwork_until_the_extension_clears_it()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);
		byte[] cover = [0x89, 0x50, 0x4E, 0x47, 42];

		await SendCoverAsync(extension, 7, cover);
		await SendTextAsync(extension, Added(7));
		var state = await EventuallyAsync(integration, s => s.ArtworkId is not null);
		var artwork = await Player(integration).GetArtworkAsync(state.ArtworkId!);

		await SendTextAsync(extension, "1 7 |||||\u0001|");
		var cleared = await EventuallyAsync(integration, s => s.ArtworkId is null);

		Assert.Multiple(() =>
		{
			Assert.That(artwork?.Data, Is.EqualTo(cover));
			Assert.That(cleared.TrackName, Is.EqualTo("Song"));
		});
	}

	[Test]
	public async Task Playback_commands_reach_the_extension_in_the_adapter_format()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);
		await SendTextAsync(extension, Added(7, portId: 4711));
		await EventuallyAsync(integration, s => s.TrackName is not null);
		var player = Player(integration);

		await player.TogglePlayPauseAsync();
		var toggle = await ReceiveCommandAsync(extension);
		await player.NextAsync();
		var next = await ReceiveCommandAsync(extension);
		await player.SeekAsync(TimeSpan.FromSeconds(500));
		var seek = await ReceiveCommandAsync(extension);
		await player.SetVolumeAsync(150);
		var volume = await ReceiveCommandAsync(extension);
		await player.SetShuffleAsync(true);
		var shuffle = await ReceiveCommandAsync(extension);
		await player.SetRepeatModeAsync(RepeatMode.Track);
		var repeat = await ReceiveCommandAsync(extension);

		Assert.Multiple(() =>
		{
			Assert.That(toggle, Is.EqualTo((4711L, 0, 1L)), "pause a playing player");
			Assert.That(next, Is.EqualTo((4711L, 2, 0L)));
			Assert.That(seek, Is.EqualTo((4711L, 3, 200L)), "seek clamps to the duration");
			Assert.That(volume, Is.EqualTo((4711L, 4, 100L)));
			Assert.That(shuffle, Is.EqualTo((4711L, 7, 1L)));
			Assert.That(repeat, Is.EqualTo((4711L, 6, 4L)));
		});
	}

	[Test]
	public async Task A_command_the_site_does_not_support_is_not_sent()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);
		await SendTextAsync(extension, Added(7, canSkipNext: 0));
		await EventuallyAsync(integration, s => s.TrackName is not null);

		await Player(integration).NextAsync();
		await Player(integration).SetVolumeAsync(20);

		Assert.That(await ReceiveCommandAsync(extension), Is.EqualTo((7L, 4, 20L)));
	}

	[Test]
	public async Task Without_a_browser_the_player_is_unavailable_and_commands_do_nothing()
	{
		using var integration = await StartAsync();

		var state = await Player(integration).GetStateAsync();
		var connected = (await integration.ReadAsync(WebNowPlayingVariables.IsConnectedId)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(state.IsUnavailable, Is.True);
			Assert.That(state.StatusMessage, Is.Null);
			Assert.That(connected, Is.EqualTo(false));
			Assert.DoesNotThrowAsync(() => Player(integration).TogglePlayPauseAsync());
		});
	}

	[Test]
	public async Task A_connected_browser_with_nothing_playing_is_connected_and_stopped()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);

		var state = await EventuallyAsync(integration, s => s.IsConnected);

		Assert.Multiple(() =>
		{
			Assert.That(state.IsUnavailable, Is.False);
			Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Stopped));
		});
	}

	[Test]
	public async Task A_muted_playing_tab_does_not_take_over_from_an_audible_one()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);
		await SendTextAsync(extension, Added(2, title: "Muted", volume: 0, activeAt: 2000));
		await SendTextAsync(extension, Added(1, title: "Audible", volume: 50, activeAt: 1000));

		var state = await EventuallyAsync(integration, s => s.TrackName == "Audible");

		Assert.That(state.TrackName, Is.EqualTo("Audible"));
	}

	[Test]
	public async Task Closing_the_extension_connection_removes_its_players()
	{
		using var integration = await StartAsync();
		var extension = await ConnectAsync(integration);
		await SendTextAsync(extension, Added(7));
		await EventuallyAsync(integration, s => s.TrackName is not null);

		await extension.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
		extension.Dispose();

		var state = await EventuallyAsync(integration, s => s.IsUnavailable);
		Assert.That(state.TrackName, Is.Null);
	}

	[TestCase("https://example.test")]
	[TestCase("null")]
	public async Task A_web_page_cannot_connect(string origin)
	{
		using var integration = await StartAsync();

		Assert.ThrowsAsync<WebSocketException>(() => ConnectAsync(integration, origin));
	}

	[Test]
	public async Task An_oversized_message_closes_the_connection()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);
		await ReceiveTextAsync(extension);

		await SendTextAsync(extension, "0 1 " + new string('a', WebNowPlayingServer.MaxTextMessageBytes));
		var result = await extension.ReceiveAsync(new byte[256], new CancellationTokenSource(_timeout).Token);

		Assert.That(result.CloseStatus, Is.EqualTo(WebSocketCloseStatus.MessageTooBig));
	}

	[Test]
	public async Task An_unreadable_line_is_ignored_and_the_connection_stays_usable()
	{
		using var integration = await StartAsync();
		using var extension = await ConnectAsync(integration);

		await SendTextAsync(extension, "not a webnowplaying message");
		await SendTextAsync(extension, Added(7));

		var state = await EventuallyAsync(integration, s => s.TrackName is not null);
		Assert.That(state.TrackName, Is.EqualTo("Song"));
	}

	[Test]
	public async Task A_port_taken_by_another_application_is_reported_as_an_issue()
	{
		var occupied = new TcpListener(IPAddress.Loopback, 0);
		occupied.Start();
		try
		{
			using var integration = new WebNowPlayingIntegration(((IPEndPoint)occupied.LocalEndpoint).Port);
			await integration.InitializeAsync(new FakeYtmDesktopIntegrationContext());

			var issues = await integration.GetIssuesAsync();

			Assert.Multiple(() =>
			{
				Assert.That(issues.Select(issue => issue.Id), Is.EqualTo(new[] { WebNowPlayingIntegration.PortInUseIssueId }));
				Assert.That(integration.GetInstances(), Is.Empty);
			});
		}
		finally
		{
			occupied.Stop();
		}
	}

	[Test]
	public async Task Turning_the_integration_off_releases_the_port_and_turning_it_on_binds_it_again()
	{
		var port = FreePort();
		using var integration = new WebNowPlayingIntegration(port);

		await integration.InitializeAsync(new FakeYtmDesktopIntegrationContext());
		var boundWhileOn = integration.ListeningPort;
		await integration.ShutdownAsync();
		var probe = new TcpListener(IPAddress.Loopback, port);
		probe.Start();
		probe.Stop();
		await integration.InitializeAsync(new FakeYtmDesktopIntegrationContext());
		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(boundWhileOn, Is.EqualTo(port));
			Assert.That(integration.ListeningPort, Is.EqualTo(port));
			Assert.That(issues, Is.Empty);
		});
	}

	[Test]
	public async Task Macro_Deck_2_WebNowPlaying_buttons_migrate_to_this_integration_with_a_setup_hint()
	{
		var claimants = IntegrationDiscovery.DiscoverIntegrations(Serilog.Log.Logger)
			.OfType<IMigrationProvider>()
			.Where(provider => provider.Migrations.Any(migration =>
				migration.Source == MigrationSource.MacroDeck2 &&
				migration.ClaimedActionSources.Contains("WebNowPlaying Plugin")))
			.ToList();

		var migrated = await claimants.Single().Migrations.Single().MigrateActionAsync(
			new ForeignAction(TypeName: "jbcarreon123.WebNowPlayingPlugin.Actions.PlayPauseAction",
				ActionSource: "WebNowPlaying Plugin",
				DisplayName: null,
				Configuration: null,
				ConfigurationSummary: null),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(claimants.Single(), Is.InstanceOf<WebNowPlayingIntegration>());
			Assert.That(migrated?.IntegrationId, Is.EqualTo(WebNowPlayingIntegration.IntegrationId));
			Assert.That(migrated?.ActionId, Is.EqualTo("toggle-play-pause"));
			Assert.That(migrated?.Warnings, Has.Count.EqualTo(1));
		});
	}

	private static string Added(
		long id,
		long? portId = null,
		string title = "Song",
		int volume = 50,
		long activeAt = 1000,
		int canSkipNext = 1)
		=> $"0 {id} " + string.Join('|',
			portId ?? id, "YouTube", title, "Rick", "Album", "https://example.test/cover.png",
			0, 30, 200, volume, 0, 1, 0, 0, 7,
			1, 1, canSkipNext, 1, 1, 1, 1, 1,
			1, 1, activeAt) + "|";

	private static async Task<WebNowPlayingIntegration> StartAsync()
	{
		var integration = new WebNowPlayingIntegration(0);
		await integration.InitializeAsync(new FakeYtmDesktopIntegrationContext());
		return integration;
	}

	private static IMusicPlayer Player(WebNowPlayingIntegration integration)
		=> integration.GetPlayer(WebNowPlayingIntegration.InstanceId)!;

	private static async Task<ClientWebSocket> ConnectAsync(WebNowPlayingIntegration integration, string origin = ExtensionOrigin)
	{
		var socket = new ClientWebSocket();
		socket.Options.SetRequestHeader("Origin", origin);
		try
		{
			using var timeout = new CancellationTokenSource(_timeout);
			await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{integration.ListeningPort}"), timeout.Token);
			return socket;
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private static Task SendTextAsync(ClientWebSocket socket, string text)
		=> socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

	private static Task SendCoverAsync(ClientWebSocket socket, uint playerId, byte[] cover)
	{
		var message = new byte[sizeof(uint) + cover.Length];
		BinaryPrimitives.WriteUInt32LittleEndian(message, playerId);
		cover.CopyTo(message, sizeof(uint));
		return socket.SendAsync(message, WebSocketMessageType.Binary, true, CancellationToken.None);
	}

	private static async Task<string> ReceiveTextAsync(ClientWebSocket socket)
	{
		using var timeout = new CancellationTokenSource(_timeout);
		var buffer = new byte[4096];
		using var message = new MemoryStream();

		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, timeout.Token);
			message.Write(buffer, 0, result.Count);
			if (result.EndOfMessage)
			{
				return Encoding.UTF8.GetString(message.ToArray());
			}
		}
	}

	private static async Task<(long PortId, int Event, long Data)> ReceiveCommandAsync(ClientWebSocket socket)
	{
		while (true)
		{
			var text = await ReceiveTextAsync(socket);
			if (text.StartsWith("ADAPTER_VERSION", StringComparison.Ordinal))
			{
				continue;
			}

			var parts = text.Split(' ');
			return (long.Parse(parts[0], CultureInfo.InvariantCulture),
				int.Parse(parts[2], CultureInfo.InvariantCulture),
				long.Parse(parts[3], CultureInfo.InvariantCulture));
		}
	}

	private static async Task<MusicPlayerState> EventuallyAsync(
		WebNowPlayingIntegration integration,
		Func<MusicPlayerState, bool> condition)
	{
		var deadline = DateTime.UtcNow + _timeout;
		while (true)
		{
			var state = await Player(integration).GetStateAsync();
			if (condition(state) || DateTime.UtcNow > deadline)
			{
				return state;
			}

			await Task.Delay(20);
		}
	}

	private static int FreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}
