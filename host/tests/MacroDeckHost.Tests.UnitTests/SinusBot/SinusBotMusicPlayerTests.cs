using System.Net;
using MacroDeckHost.Integrations.SinusBot;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.SinusBot;

[TestFixture]
internal sealed class SinusBotMusicPlayerTests
{
	private static readonly string[] _expectedArtists = ["Artist"];
	private static readonly int[] _expectedClampedVolumes = [100, 0];
	private static readonly bool[] _expectedRepeatFlags = [true, false];

	private static SinusBotMusicPlayer Connect(FakeSinusBotClient client, string name = "MyBot")
	{
		var player = new SinusBotMusicPlayer();
		player.Connect(client, "inst-1", name);
		return player;
	}

	[Test]
	public async Task GetStateAsync_WhenDisconnected_ReturnsDisconnected()
	{
		var player = new SinusBotMusicPlayer();
		player.Connect(new FakeSinusBotClient(), "inst-1", "MyBot");
		player.Disconnect();

		var state = await player.GetStateAsync();

		Assert.That(state, Is.EqualTo(MusicPlayerState.Disconnected));
	}

	[Test]
	public async Task GetStateAsync_WhenPlaying_MapsTrackAndPlayback()
	{
		var client = new FakeSinusBotClient
		{
			Status = new SinusBotInstanceStatus
			{
				Running = true,
				Playing = true,
				Volume = 42,
				Position = 50_000,
				Shuffle = true,
				Repeat = false,
				CurrentTrack = new SinusBotTrack
				{
					Title = "Song",
					Artist = "Artist",
					Duration = 180_000
				}
			}
		};
		var player = Connect(client);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Playing));
			Assert.That(state.TrackName, Is.EqualTo("Song"));
			Assert.That(state.Artists, Is.EqualTo(_expectedArtists));
			Assert.That(state.AlbumName, Is.Null);
			Assert.That(state.VolumePercent, Is.EqualTo(42));
			Assert.That(state.Position, Is.EqualTo(TimeSpan.FromMilliseconds(50_000)));
			Assert.That(state.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(180_000)));
			Assert.That(state.ShuffleEnabled, Is.True);
			Assert.That(state.RepeatMode, Is.EqualTo(RepeatMode.Off));
			Assert.That(state.DeviceName, Is.EqualTo("MyBot"));
		});
	}

	[Test]
	public async Task GetStateAsync_WhenPausedWithTrack_MapsToPaused()
	{
		var client = new FakeSinusBotClient
		{
			Status = new SinusBotInstanceStatus
			{
				Running = true,
				Playing = false,
				CurrentTrack = new SinusBotTrack { Title = "Song" }
			}
		};
		var player = Connect(client);

		var state = await player.GetStateAsync();

		Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Paused));
		Assert.That(state.IsConnected, Is.True);
	}

	[Test]
	public async Task GetStateAsync_WhenStopped_MapsToStopped()
	{
		var client = new FakeSinusBotClient { Status = new SinusBotInstanceStatus { Running = true, Playing = false } };
		var player = Connect(client);

		var state = await player.GetStateAsync();

		Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Stopped));
	}

	[Test]
	public async Task GetStateAsync_WhenInstanceNotRunning_IsUnavailable()
	{
		var client = new FakeSinusBotClient { Status = new SinusBotInstanceStatus { Running = false } };
		var player = Connect(client);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.False);
			Assert.That(state.IsUnavailable, Is.True);
			Assert.That(state.StatusMessage, Is.EqualTo("Instance not running"));
		});
	}

	[Test]
	public async Task GetStateAsync_WhenApiFails_ReportsUnavailable()
	{
		var client = new FakeSinusBotClient { StatusException = new SinusBotApiException("boom") };
		var player = Connect(client);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsUnavailable, Is.True);
			Assert.That(state.StatusMessage, Is.EqualTo("SinusBot is not responding"));
			Assert.That(state.DeviceName, Is.EqualTo("MyBot"));
		});
	}

	[Test]
	public async Task GetStateAsync_PastTheFreshnessWindow_ReportsUnavailable()
	{
		var client = new FakeSinusBotClient
		{
			Status = new SinusBotInstanceStatus
			{
				Running = true,
				Playing = true,
				CurrentTrack = new SinusBotTrack { Title = "Song", Duration = 180_000 }
			}
		};
		var player = new SinusBotMusicPlayer(lastStateLifetime: TimeSpan.Zero);
		player.Connect(client, "inst-1", "MyBot");
		await player.GetStateAsync();

		client.StatusException = new HttpRequestException("connection reset");
		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsUnavailable, Is.True);
			Assert.That(state.TrackName, Is.Null);
		});
	}

	[Test]
	public async Task GetStateAsync_AfterAFailure_RecoversToConnected()
	{
		var client = new FakeSinusBotClient { StatusException = new SinusBotApiException("boom") };
		var player = Connect(client);
		await player.GetStateAsync();

		client.StatusException = null;
		client.Status = new SinusBotInstanceStatus { Running = true, Playing = false };
		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.IsUnavailable, Is.False);
		});
	}

	// A LAN bot blips constantly; flipping the widget to "Not connected" on every hiccup is the
	// SinusBot half of issue #134. Only a persistent failure may tear the now-playing state down.
	[TestCaseSource(nameof(TransientFailures))]
	public async Task GetStateAsync_OnATransientFailure_KeepsTheLastState(Exception failure)
	{
		var client = new FakeSinusBotClient
		{
			Status = new SinusBotInstanceStatus
			{
				Running = true,
				Playing = true,
				CurrentTrack = new SinusBotTrack { Title = "Song", Artist = "Artist", Duration = 180_000 }
			}
		};
		var player = Connect(client);
		var good = await player.GetStateAsync();

		client.StatusException = failure;
		var afterFailure = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(afterFailure.IsConnected, Is.True);
			Assert.That(afterFailure.TrackName, Is.EqualTo(good.TrackName));
		});
	}

	[Test]
	public void GetStateAsync_WhenTheCallerCancels_DoesNotReportDisconnected()
	{
		var client = new FakeSinusBotClient { StatusException = new OperationCanceledException() };
		var player = Connect(client);
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		Assert.That(async () => await player.GetStateAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());
	}

	private static IEnumerable<Exception> TransientFailures()
	{
		yield return new SinusBotApiException("rate limited", HttpStatusCode.TooManyRequests);
		yield return new SinusBotApiException("bad gateway", HttpStatusCode.BadGateway);
		yield return new HttpRequestException("connection reset");
		yield return new IOException("socket closed");
	}

	[Test]
	public async Task SetVolumeAsync_ClampsToRange()
	{
		var client = new FakeSinusBotClient();
		var player = Connect(client);

		await player.SetVolumeAsync(150);
		await player.SetVolumeAsync(-5);

		var volumes = client.Calls.Where(c => c.Method == "SetVolume").Select(c => c.Arg).Cast<int>().ToArray();
		Assert.That(volumes, Is.EqualTo(_expectedClampedVolumes));
	}

	[Test]
	public async Task SeekAsync_ConvertsTimeSpanToSeconds()
	{
		var client = new FakeSinusBotClient();
		var player = Connect(client);

		await player.SeekAsync(TimeSpan.FromSeconds(90));

		var seek = client.Calls.Single(c => c.Method == "Seek");
		Assert.That(seek.Arg, Is.EqualTo(90));
	}

	[Test]
	public async Task TogglePlayPause_WhenPlaying_Pauses()
	{
		var client = new FakeSinusBotClient { Status = new SinusBotInstanceStatus { Running = true, Playing = true } };
		var player = Connect(client);

		await player.TogglePlayPauseAsync();

		Assert.That(client.Calls.Any(c => c.Method == "Pause"), Is.True);
		Assert.That(client.Calls.Any(c => c.Method == "Play"), Is.False);
	}

	[Test]
	public async Task TogglePlayPause_WhenPaused_Resumes()
	{
		var client = new FakeSinusBotClient
		{
			Status = new SinusBotInstanceStatus { Running = true, Playing = false, CurrentTrack = new SinusBotTrack() }
		};
		var player = Connect(client);

		await player.TogglePlayPauseAsync();

		Assert.That(client.Calls.Any(c => c.Method == "Play"), Is.True);
		Assert.That(client.Calls.Any(c => c.Method == "Pause"), Is.False);
	}

	[Test]
	public async Task CommandFailure_IsSwallowed()
	{
		var client = new FakeSinusBotClient { CommandException = new SinusBotApiException("fail") };
		var player = Connect(client);

		Assert.DoesNotThrowAsync(() => player.PlayAsync());
	}

	[Test]
	public async Task SetRepeatMode_OnlyEnablesForNonOff()
	{
		var client = new FakeSinusBotClient();
		var player = Connect(client);

		await player.SetRepeatModeAsync(RepeatMode.Track);
		await player.SetRepeatModeAsync(RepeatMode.Off);

		var repeats = client.Calls.Where(c => c.Method == "SetRepeat").Select(c => c.Arg).Cast<bool>().ToArray();
		Assert.That(repeats, Is.EqualTo(_expectedRepeatFlags));
	}

	[Test]
	public async Task PlayItemAsync_Track_CallsPlayFile()
	{
		var client = new FakeSinusBotClient();
		var player = Connect(client);

		await player.PlayItemAsync(new MusicPlayerCatalogItem("file-1", "Song", MusicPlayerCatalogItemKind.Track));

		var playFile = client.Calls.Single(c => c.Method == "PlayFile");
		Assert.That(playFile.Arg, Is.EqualTo("file-1"));
	}

	[Test]
	public async Task PlayItemAsync_Playlist_DoesNotCallClient()
	{
		var client = new FakeSinusBotClient();
		var player = Connect(client);

		await player.PlayItemAsync(new MusicPlayerCatalogItem("pl-1", "Playlist", MusicPlayerCatalogItemKind.Playlist));

		Assert.That(client.Calls, Is.Empty);
	}

	[Test]
	public async Task GetCatalogAsync_Track_MapsFiles()
	{
		var client = new FakeSinusBotClient
		{
			Files =
			[
				new SinusBotFile { Uuid = "f1", Title = "Song A", Artist = "Artist A", Duration = 60_000 },
				new SinusBotFile { Uuid = "f2", Title = "Song B", Artist = "Artist B" }
			]
		};
		var player = Connect(client);

		var items = await player.GetCatalogAsync("inst-1",
			MusicPlayerCatalogItemKind.Track,
			null,
			CancellationToken.None);

		Assert.That(items, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(items[0].Id, Is.EqualTo("f1"));
			Assert.That(items[0].Title, Is.EqualTo("Song A"));
			Assert.That(items[0].Subtitle, Is.EqualTo("Artist A"));
			Assert.That(items[0].Kind, Is.EqualTo(MusicPlayerCatalogItemKind.Track));
			Assert.That(items[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(60)));
		});
	}

	[Test]
	public async Task GetCatalogAsync_AppliesFilter()
	{
		var client = new FakeSinusBotClient
		{
			Files =
			[
				new SinusBotFile { Uuid = "f1", Title = "Alpha Song", Artist = "X" },
				new SinusBotFile { Uuid = "f2", Title = "Beta Tune", Artist = "Y" }
			]
		};
		var player = Connect(client);

		var items = await player.GetCatalogAsync("inst-1",
			MusicPlayerCatalogItemKind.Track,
			"alpha",
			CancellationToken.None);

		Assert.That(items.Single().Id, Is.EqualTo("f1"));
	}

	[Test]
	public async Task GetCatalogAsync_Playlist_ReturnsEmpty()
	{
		var client = new FakeSinusBotClient
		{
			Files = [new SinusBotFile { Uuid = "f1", Title = "Song" }]
		};
		var player = Connect(client);

		var items = await player.GetCatalogAsync("inst-1",
			MusicPlayerCatalogItemKind.Playlist,
			null,
			CancellationToken.None);

		Assert.That(items, Is.Empty);
	}

	[Test]
	public void GetCatalogAsync_WhenApiFails_Throws()
	{
		var client = new FakeSinusBotClient { FilesException = new SinusBotApiException("boom") };
		var player = Connect(client);

		Assert.ThatAsync(() => player.GetCatalogAsync("inst-1",
				MusicPlayerCatalogItemKind.Track,
				null,
				CancellationToken.None),
			Throws.InstanceOf<SinusBotApiException>());
	}

	[Test]
	public void GetCatalogAsync_WhenNotConnected_Throws()
	{
		var player = new SinusBotMusicPlayer();

		Assert.ThatAsync(() => player.GetCatalogAsync("inst-1",
				MusicPlayerCatalogItemKind.Track,
				null,
				CancellationToken.None),
			Throws.InstanceOf<InvalidOperationException>());
	}
}
