using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class GetMusicPlayerStateRequestMessageHandlerTests
{
	private const string InstanceId = "spotify::a";

	[Test]
	public async Task Handle_WhenTheProviderHangs_AnswersImmediatelyFromTheCache()
	{
		var player = new HangingMusicPlayer();
		var cache = new MusicPlayerStateCache();
		cache.Record(InstanceId, new MusicPlayerStatePayload { InstanceId = InstanceId, IsConnected = true });
		var handler = new GetMusicPlayerStateRequestMessageHandler(new StubRegistry(InstanceId, player), cache);

		var handle = handler.Handle(new GetMusicPlayerStateRequest { InstanceId = InstanceId }, CancellationToken.None);

		Assert.That(handle.IsCompleted, Is.True, "the handler must not await the provider");

		var response = await handle;
		Assert.Multiple(() =>
		{
			Assert.That(response.State?.IsConnected, Is.True);
			Assert.That(response.State?.InstanceId, Is.EqualTo(InstanceId));
			Assert.That(player.WasRead, Is.False, "the handler must not read through to the provider");
		});
	}

	[Test]
	public async Task Handle_WhenTheInstanceHasNotBeenPolledYet_AnswersUnknownRatherThanDisconnected()
	{
		var handler = new GetMusicPlayerStateRequestMessageHandler(
			new StubRegistry(InstanceId, new HangingMusicPlayer()),
			new MusicPlayerStateCache());

		var response = await handler.Handle(new GetMusicPlayerStateRequest { InstanceId = InstanceId },
			CancellationToken.None);

		Assert.That(response.State, Is.Null);
	}

	[Test]
	public async Task Handle_WhenTheInstanceIsUnknown_AnswersDisconnected()
	{
		var handler = new GetMusicPlayerStateRequestMessageHandler(new StubRegistry(InstanceId, player: null),
			new MusicPlayerStateCache());

		var response = await handler.Handle(new GetMusicPlayerStateRequest { InstanceId = "spotify::gone" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.State, Is.Not.Null);
			Assert.That(response.State?.IsConnected, Is.False);
			Assert.That(response.State?.InstanceId, Is.EqualTo("spotify::gone"));
		});
	}

	[Test]
	public async Task Handle_WithoutAnInstanceId_FallsBackToTheFirstInstance()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(InstanceId, new MusicPlayerStatePayload { InstanceId = InstanceId, IsConnected = true });
		var handler = new GetMusicPlayerStateRequestMessageHandler(
			new StubRegistry(InstanceId, new HangingMusicPlayer()),
			cache);

		var response = await handler.Handle(new GetMusicPlayerStateRequest(), CancellationToken.None);

		Assert.That(response.State?.InstanceId, Is.EqualTo(InstanceId));
	}

	[Test]
	public void Forget_DropsInstancesThatAreNoLongerRegistered()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(InstanceId, new MusicPlayerStatePayload { InstanceId = InstanceId });
		cache.Record("sinusbot::b", new MusicPlayerStatePayload { InstanceId = "sinusbot::b" });

		cache.Forget(new HashSet<string>(StringComparer.Ordinal) { InstanceId });

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetState(InstanceId), Is.Not.Null);
			Assert.That(cache.GetState("sinusbot::b"), Is.Null);
		});
	}

	private sealed class StubRegistry : IMusicPlayerRegistry
	{
		private readonly string _instanceId;
		private readonly IMusicPlayer? _player;

		public StubRegistry(string instanceId, IMusicPlayer? player)
		{
			_instanceId = instanceId;
			_player = player;
		}

		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances()
			=> [new(_instanceId, "spotify", "Spotify", "Spotify", false)];

		public IMusicPlayer? GetPlayer(string instanceId)
			=> instanceId == _instanceId ? _player : null;

		public IMusicPlayer? DefaultPlayer => _player;
	}

	private sealed class HangingMusicPlayer : IMusicPlayer
	{
		public bool WasRead { get; private set; }

		public async Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
		{
			WasRead = true;
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return MusicPlayerState.Disconnected;
		}

		public Task<MusicPlayerArtwork?> GetArtworkAsync(
			string artworkId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<MusicPlayerArtwork?>(null);

		public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}
}
