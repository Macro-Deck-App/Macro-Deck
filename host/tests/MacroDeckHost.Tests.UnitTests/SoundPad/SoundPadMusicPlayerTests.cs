using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Integrations.SoundPad;
using static MacroDeckHost.Tests.UnitTests.SoundPad.SoundPadTestSupport;

namespace MacroDeckHost.Tests.UnitTests.SoundPad;

[TestFixture]
public class SoundPadMusicPlayerTests
{
	private FakeSoundPadClient _client = null!;
	private SoundPadConnection _connection = null!;
	private SoundPadMusicPlayer _player = null!;

	[SetUp]
	public async Task SetUp()
	{
		_client = new FakeSoundPadClient();
		_connection = await ConnectedAsync(_client);
		_player = new SoundPadMusicPlayer(_connection);
	}

	[TearDown]
	public void TearDown()
	{
		_connection.Dispose();
		_client.Dispose();
	}

	[Test]
	public async Task State_WhilePlaying_ReportsTheSoundPositionDurationAndVolume()
	{
		_client.Sounds = [Sound(1, "Airhorn", artist: "DJ", lastPlayedOn: DateTime.UtcNow)];
		_client.Status = SoundPadPlayStatus.Playing;
		_client.PositionMs = 1500;
		_client.DurationMs = 3000;
		_client.Volume = 42;

		var state = await _player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Playing));
			Assert.That(state.TrackName, Is.EqualTo("Airhorn"));
			Assert.That(state.Artists, Is.EqualTo(new[] { "DJ" }));
			Assert.That(state.Position, Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
			Assert.That(state.Duration, Is.EqualTo(TimeSpan.FromSeconds(3)));
			Assert.That(state.VolumePercent, Is.EqualTo(42));
		});
	}

	[TestCase("Paused", PlaybackState.Paused)]
	[TestCase("Seeking", PlaybackState.Playing)]
	[TestCase("Stopped", PlaybackState.Stopped)]
	public async Task State_MapsSoundPadStatus(string status, PlaybackState expected)
	{
		_client.Status = Enum.Parse<SoundPadPlayStatus>(status);

		var state = await _player.GetStateAsync();

		Assert.That(state.PlaybackState, Is.EqualTo(expected));
	}

	[Test]
	public async Task State_WhenSoundPadIsClosed_IsUnavailableNotDisconnected()
	{
		_client.Reachable = false;
		_client.Disconnect();

		var state = await _player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.False);
			Assert.That(state.IsUnavailable, Is.True);
		});
	}

	[Test]
	public async Task State_ShowsTheSoundMacroDeckStarted()
	{
		_client.Sounds =
		[
			Sound(1, "Airhorn", lastPlayedOn: DateTime.UtcNow),
			Sound(2, "Drumroll", lastPlayedOn: DateTime.UtcNow.AddMinutes(-5))
		];

		await _player.PlayItemAsync(new MusicPlayerCatalogItem(@"C:\Sounds\Drumroll.mp3", "Drumroll",
			MusicPlayerCatalogItemKind.Track));
		_client.Status = SoundPadPlayStatus.Playing;
		_client.DurationMs = 3000;

		var state = await _player.GetStateAsync();

		Assert.That(state.TrackName, Is.EqualTo("Drumroll"));
	}

	[Test]
	public async Task State_AfterAShortMacroDeckSoundEnded_ShowsTheNextSoundStartedInSoundPad()
	{
		_client.Sounds =
		[
			Sound(1, "Airhorn", lastPlayedOn: DateTime.UtcNow),
			Sound(2, "Drumroll", lastPlayedOn: DateTime.UtcNow.AddMinutes(-5))
		];
		await _player.PlayItemAsync(new MusicPlayerCatalogItem(@"C:\Sounds\Drumroll.mp3", "Drumroll",
			MusicPlayerCatalogItemKind.Track));
		await _player.GetStateAsync();

		_client.Status = SoundPadPlayStatus.Playing;
		_client.DurationMs = 3000;
		var state = await _player.GetStateAsync();

		Assert.That(state.TrackName, Is.EqualTo("Airhorn"));
	}

	[Test]
	public async Task State_WhenTheSameLengthSoundStartsAgainInSoundPad_ShowsTheNewSound()
	{
		_client.Sounds =
		[
			Sound(1, "Airhorn", lastPlayedOn: DateTime.UtcNow.AddMinutes(-5)),
			Sound(2, "Drumroll", lastPlayedOn: DateTime.UtcNow.AddMinutes(-1))
		];
		_client.Status = SoundPadPlayStatus.Playing;
		_client.DurationMs = 3000;
		_client.PositionMs = 2500;
		await _player.GetStateAsync();

		_client.Sounds = [Sound(1, "Airhorn", lastPlayedOn: DateTime.UtcNow), _client.Sounds[1]];
		_client.PositionMs = 200;
		var state = await _player.GetStateAsync();

		Assert.That(state.TrackName, Is.EqualTo("Airhorn"));
	}

	[Test]
	public async Task State_WhenSoundPadRejectsAnOptionalQuery_StillReportsPlayback()
	{
		_client.RejectsMuteQuery = true;
		_client.Status = SoundPadPlayStatus.Playing;
		_client.DurationMs = 3000;

		var state = await _player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Playing));
		});
	}

	[Test]
	public async Task State_ForASoundStartedInSoundPad_ShowsTheMostRecentlyPlayedSound()
	{
		_client.Sounds =
		[
			Sound(1, "Airhorn", lastPlayedOn: DateTime.UtcNow.AddMinutes(-5)),
			Sound(2, "Drumroll", lastPlayedOn: DateTime.UtcNow),
			Sound(3, "Never played")
		];
		_client.Status = SoundPadPlayStatus.Playing;
		_client.DurationMs = 3000;

		var state = await _player.GetStateAsync();

		Assert.That(state.TrackName, Is.EqualTo("Drumroll"));
	}

	[Test]
	public async Task State_WhenSoundPadReportsNoPlayTimes_LeavesTheTitleEmpty()
	{
		_client.Sounds = [Sound(1, "Airhorn"), Sound(2, "Drumroll")];
		_client.Status = SoundPadPlayStatus.Playing;
		_client.DurationMs = 3000;

		var state = await _player.GetStateAsync();

		Assert.That(state.TrackName, Is.Null);
	}

	[Test]
	public async Task Play_ResumesWhenPaused_AndReplaysWhenStopped()
	{
		_client.Status = SoundPadPlayStatus.Paused;
		await _player.PlayAsync();

		_client.Status = SoundPadPlayStatus.Stopped;
		await _player.PlayAsync();

		_client.Status = SoundPadPlayStatus.Playing;
		await _player.PlayAsync();

		Assert.That(_client.Commands, Is.EqualTo(new[] { "toggle pause", "play again" }));
	}

	[Test]
	public async Task Pause_OnlyPausesWhatIsPlaying()
	{
		_client.Status = SoundPadPlayStatus.Paused;
		await _player.PauseAsync();

		_client.Status = SoundPadPlayStatus.Playing;
		await _player.PauseAsync();

		Assert.That(_client.Commands, Is.EqualTo(new[] { "toggle pause" }));
	}

	[Test]
	public async Task SeekAndVolume_ReachSoundPad()
	{
		await _player.SeekAsync(TimeSpan.FromSeconds(2));
		await _player.SetVolumeAsync(150);

		Assert.That(_client.Commands, Is.EqualTo(new[] { "seek 2000", "volume 100" }));
	}

	[Test]
	public async Task Catalog_ListsSoundsAndFiltersByTitleOrArtist()
	{
		_client.Sounds = [Sound(1, "Airhorn"), Sound(2, "Drumroll", artist: "Band"), Sound(3, "Applause")];

		var all = await _player.GetCatalogAsync(SoundPadIntegration.InstanceId, MusicPlayerCatalogItemKind.Track,
			null, CancellationToken.None);
		var byTitle = await _player.GetCatalogAsync(SoundPadIntegration.InstanceId,
			MusicPlayerCatalogItemKind.Track, "air", CancellationToken.None);
		var byArtist = await _player.GetCatalogAsync(SoundPadIntegration.InstanceId,
			MusicPlayerCatalogItemKind.Track, "band", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(all.Select(item => item.Title), Is.EqualTo(new[] { "Airhorn", "Drumroll", "Applause" }));
			Assert.That(byTitle.Select(item => item.Title), Is.EqualTo(new[] { "Airhorn" }));
			Assert.That(byArtist.Select(item => item.Title), Is.EqualTo(new[] { "Drumroll" }));
		});
	}

	[Test]
	public void Catalog_WhenSoundPadIsClosed_Throws()
	{
		_client.Reachable = false;
		_client.Disconnect();

		Assert.ThrowsAsync<SoundPadUnavailableException>(() => _player.GetCatalogAsync(SoundPadIntegration.InstanceId,
			MusicPlayerCatalogItemKind.Track, null, CancellationToken.None));
	}

	[Test]
	public async Task PlayItem_AfterTheListWasReordered_PlaysTheSameSound()
	{
		_client.Sounds = [Sound(1, "Airhorn"), Sound(2, "Drumroll")];
		var items = await _player.GetCatalogAsync(SoundPadIntegration.InstanceId, MusicPlayerCatalogItemKind.Track,
			"drum", CancellationToken.None);

		_client.Sounds = [Sound(1, "New sound"), Sound(2, "Airhorn"), Sound(3, "Drumroll")];
		await _player.PlayItemAsync(items.Single());

		Assert.That(_client.Commands, Is.EqualTo(new[] { "play 3" }));
	}

	[Test]
	public async Task PlayItem_WithAnIndex_PlaysThatPosition()
	{
		await _player.PlayItemAsync(new MusicPlayerCatalogItem("5", "5", MusicPlayerCatalogItemKind.Track));

		Assert.That(_client.Commands, Is.EqualTo(new[] { "play 5" }));
	}

	[Test]
	public async Task PlayItem_WhenSoundPadIsClosed_DoesNotThrow()
	{
		_client.Reachable = false;
		_client.Disconnect();

		await _player.PlayItemAsync(new MusicPlayerCatalogItem("5", "5", MusicPlayerCatalogItemKind.Track));

		Assert.That(_client.Commands, Is.Empty);
	}
}
