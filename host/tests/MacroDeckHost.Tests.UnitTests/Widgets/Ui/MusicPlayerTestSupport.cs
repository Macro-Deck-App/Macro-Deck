using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Widgets.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The stand-ins the Music Player's UI tests drive the real resolver through: a registry, a state cache,
/// an artwork service and a palette extractor. All four are real boundaries the host owns behind an
/// interface, so a test that swapped the resolver itself would prove less than one that swaps these.
/// </summary>
internal sealed class MusicPlayerTestHarness
{
	internal const string InstanceId = "spotify.1";

	private readonly FakeTimeProvider _time = new()
	{
		Now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
	};

	private readonly StubRegistry _registry = new();
	private readonly StubStateCache _stateCache = new();
	private readonly StubArtworkService _artwork = new();
	private readonly MusicPlayerViewStateResolver _resolver;

	public MusicPlayerTestHarness()
	{
		_resolver = new MusicPlayerViewStateResolver(_registry,
			_stateCache,
			_artwork,
			new StubPaletteExtractor(),
			new FakeIntegrationRegistry(),
			new UiResourceStore(),
			_time,
			new LoggerConfiguration().CreateLogger());
	}

	public DateTimeOffset Now => _time.GetUtcNow();

	public int ArtworkFetches => _artwork.Fetches;

	public void Record(MusicPlayerStatePayload payload) => _stateCache.Record(InstanceId, payload);

	public void RecordSecond(MusicPlayerStatePayload payload)
	{
		_registry.HasSecondInstance = true;
		payload.InstanceId = StubRegistry.SecondInstanceId;
		_stateCache.Record(StubRegistry.SecondInstanceId, payload);
	}

	public void Focus(string? instanceId) => _stateCache.ActiveInstanceId = instanceId;


	public void Advance(TimeSpan by) => _time.Advance(by);

	/// <summary>The resolver these stubs feed, so a session test can drive the real one.</summary>
	public MusicPlayerViewStateResolver Resolver => _resolver;

	public Task<MusicPlayerViewState> ResolveAsync(
		MusicPlayerViewState? previous = null,
		MusicPlayerWidgetData? config = null)
		=> _resolver.ResolveAsync(config ?? new MusicPlayerWidgetData(),
			previous ?? MusicPlayerViewState.Loading,
			CancellationToken.None);
}

internal sealed class StubRegistry : IMusicPlayerRegistry
{
	internal const string SecondInstanceId = "ytm.1";

	public bool HasSecondInstance { get; set; }

	public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances() =>
	[
		new(MusicPlayerTestHarness.InstanceId,
			"app.macro-deck.spotify",
			LocalizedText.FromLiteral("Spotify"),
			"Spotify",
			false),
		.. HasSecondInstance
			? new MusicPlayerInstanceDescriptor[]
			{
				new(SecondInstanceId, "app.macro-deck.ytm", LocalizedText.FromLiteral("YouTube Music"), "YouTube Music", false),
			}
			: [],
	];

	public IMusicPlayer? GetPlayer(string instanceId) => null;

	public IMusicPlayer? DefaultPlayer => null;
}

internal sealed class StubStateCache : IMusicPlayerStateCache
{
	private readonly Dictionary<string, MusicPlayerStatePayload> _states = new(StringComparer.Ordinal);

	public MusicPlayerStatePayload? GetState(string instanceId)
		=> _states.GetValueOrDefault(instanceId);

	public IReadOnlyList<MusicPlayerStatePayload> GetAll() => _states.Values.ToList();

	public string? ActiveInstanceId { get; set; }

	public void Record(string instanceId, MusicPlayerStatePayload payload) => _states[instanceId] = payload;

	public void Forget(IReadOnlySet<string> keep)
	{
	}
}

internal sealed class StubArtworkService : IMusicPlayerArtworkService
{
	public int Fetches { get; private set; }

	public string GetETag(string artworkId, int? size) => $"\"{artworkId}\"";

	public Task<ArtworkImageResult?> GetImage(string instanceId,
		string artworkId,
		int? size,
		CancellationToken cancellationToken)
	{
		Fetches++;

		return Task.FromResult<ArtworkImageResult?>(new ArtworkImageResult([1, 2, 3, 4],
			"image/webp",
			$"\"{artworkId}\""));
	}
}

internal sealed class StubPaletteExtractor : IArtworkPaletteExtractor
{
	public ArtworkPalette? Extract(byte[] image) => new("#aabbcc", "#112233");
}
