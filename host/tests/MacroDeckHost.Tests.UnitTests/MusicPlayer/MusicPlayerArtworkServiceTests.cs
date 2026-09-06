using MacroDeckHost.Application.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
public class MusicPlayerArtworkServiceTests
{
	private const string InstanceId = "test::player";
	private const string ArtworkId = "abc123";

	private FakePlayer _player = null!;
	private FakeRegistry _registry = null!;
	private FakeProcessor _processor = null!;

	[SetUp]
	public void SetUp()
	{
		_player = new FakePlayer();
		_registry = new FakeRegistry(InstanceId, _player);
		_processor = new FakeProcessor();
	}

	private MusicPlayerArtworkService CreateService(long maxCacheBytes = 16 * 1024 * 1024)
		=> new(_registry, _processor, new LoggerConfiguration().CreateLogger(), maxCacheBytes);

	[Test]
	public async Task GetImage_ReencodedArtwork_ServesWebpMasterWithETag()
	{
		var service = CreateService();

		var result = await service.GetImage(InstanceId, ArtworkId, null, CancellationToken.None);

		Assert.That(result, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result.Content, Is.EqualTo(_processor.Master));
			Assert.That(result.ContentType, Is.EqualTo("image/webp"));
			Assert.That(result.ETag, Is.EqualTo($"\"{ArtworkId}-master\""));
		});
	}

	[Test]
	public async Task GetImage_WithSize_ServesSmallestSufficientVariant()
	{
		var service = CreateService();

		var result = await service.GetImage(InstanceId, ArtworkId, 100, CancellationToken.None);

		Assert.That(result, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result.Content, Is.EqualTo(_processor.Variants[128]));
			Assert.That(result.ETag, Is.EqualTo($"\"{ArtworkId}-100\""));
		});
	}

	[Test]
	public async Task GetImage_SizeLargerThanAllVariants_ServesMaster()
	{
		var service = CreateService();

		var result = await service.GetImage(InstanceId, ArtworkId, 999, CancellationToken.None);

		Assert.That(result, Is.Not.Null);
		Assert.That(result.Content, Is.EqualTo(_processor.Master));
	}

	[Test]
	public async Task GetImage_SecondRequest_IsServedFromCache()
	{
		var service = CreateService();

		await service.GetImage(InstanceId, ArtworkId, null, CancellationToken.None);
		await service.GetImage(InstanceId, ArtworkId, 128, CancellationToken.None);

		Assert.That(_player.ArtworkCalls, Is.EqualTo(1));
	}

	[Test]
	public async Task GetImage_ConcurrentRequests_DownloadOnlyOnce()
	{
		var service = CreateService();
		var gate = new TaskCompletionSource();
		_player.Gate = gate.Task;

		var first = service.GetImage(InstanceId, ArtworkId, null, CancellationToken.None);
		var second = service.GetImage(InstanceId, ArtworkId, 128, CancellationToken.None);
		gate.SetResult();
		var results = await Task.WhenAll(first, second);

		Assert.Multiple(() =>
		{
			Assert.That(results[0], Is.Not.Null);
			Assert.That(results[1], Is.Not.Null);
			Assert.That(_player.ArtworkCalls, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task GetImage_UnknownInstance_ReturnsNull()
	{
		var service = CreateService();

		var result = await service.GetImage("unknown::player", ArtworkId, null, CancellationToken.None);

		Assert.That(result, Is.Null);
	}

	[Test]
	public async Task GetImage_ProviderReturnsNull_IsNotCachedAndRetries()
	{
		var service = CreateService();
		_player.Artwork = null;

		var first = await service.GetImage(InstanceId, ArtworkId, null, CancellationToken.None);
		var second = await service.GetImage(InstanceId, ArtworkId, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Null);
			Assert.That(second, Is.Null);
			Assert.That(_player.ArtworkCalls, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task GetImage_ProviderThrows_ReturnsNullAndRetries()
	{
		var service = CreateService();
		_player.Exception = new InvalidOperationException("boom");

		var first = await service.GetImage(InstanceId, ArtworkId, null, CancellationToken.None);
		_player.Exception = null;
		var second = await service.GetImage(InstanceId, ArtworkId, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Null);
			Assert.That(second, Is.Not.Null);
			Assert.That(_player.ArtworkCalls, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task GetImage_UndecodableArtwork_ServesOriginalBytesWithProviderMime()
	{
		var service = CreateService();
		_processor.ReturnNull = true;

		var result = await service.GetImage(InstanceId, ArtworkId, 128, CancellationToken.None);
		await service.GetImage(InstanceId, ArtworkId, null, CancellationToken.None);

		Assert.That(result, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result.Content, Is.EqualTo(_player.Artwork!.Data));
			Assert.That(result.ContentType, Is.EqualTo("image/jpeg"));
			Assert.That(_player.ArtworkCalls, Is.EqualTo(1), "fallback entries must be cached too");
		});
	}

	[Test]
	public async Task GetImage_OverByteBudget_EvictsLeastRecentlyUsed()
	{
		var service = CreateService(maxCacheBytes: 70);

		await service.GetImage(InstanceId, "art-a", null, CancellationToken.None);
		await service.GetImage(InstanceId, "art-b", null, CancellationToken.None);
		await service.GetImage(InstanceId, "art-c", null, CancellationToken.None);
		Assert.That(_player.ArtworkCalls, Is.EqualTo(3));

		await service.GetImage(InstanceId, "art-a", null, CancellationToken.None);
		Assert.That(_player.ArtworkCalls, Is.EqualTo(4));
		await service.GetImage(InstanceId, "art-c", null, CancellationToken.None);
		Assert.That(_player.ArtworkCalls, Is.EqualTo(4));
	}

	private sealed class FakeRegistry : IMusicPlayerRegistry
	{
		private readonly string _instanceId;
		private readonly IMusicPlayer _player;

		public FakeRegistry(string instanceId, IMusicPlayer player)
		{
			_instanceId = instanceId;
			_player = player;
		}

		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances() => [];

		public IMusicPlayer? GetPlayer(string instanceId) => instanceId == _instanceId ? _player : null;

		public IMusicPlayer? DefaultPlayer => _player;
	}

	private sealed class FakeProcessor : IArtworkProcessor
	{
		public byte[] Master { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

		public Dictionary<int, byte[]> Variants { get; } = new()
		{
			[128] = [128, 1, 2, 3, 4, 5, 6, 7, 8, 9],
			[256] = [255, 1, 2, 3, 4, 5, 6, 7, 8, 9]
		};

		public bool ReturnNull { get; set; }

		public Task<ProcessedArtworkResult?> Process(byte[] original, CancellationToken cancellationToken)
			=> Task.FromResult(ReturnNull ? null : new ProcessedArtworkResult(Master, Variants));
	}

	private sealed class FakePlayer : IMusicPlayer
	{
		public int ArtworkCalls { get; private set; }

		public MusicPlayerArtwork? Artwork { get; set; } = new([9, 9, 9], "image/jpeg");

		public Exception? Exception { get; set; }

		public Task? Gate { get; set; }

		public async Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
			CancellationToken cancellationToken = default)
		{
			ArtworkCalls++;
			if (Gate is not null)
			{
				await Gate;
			}

			if (Exception is not null)
			{
				throw Exception;
			}

			return Artwork;
		}

		public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult(new MusicPlayerState { IsConnected = true });

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
