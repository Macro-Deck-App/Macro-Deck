using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Widgets.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class MusicPlayerWidgetOptionsTests
{
	private const string AnyApp = "system-media::any";
	private const string Spotify = "spotify::alice";

	private readonly OptionsRegistry _registry = new();
	private readonly StubStateCache _stateCache = new();
	private readonly UiResourceStore _resources = new();
	private MusicPlayerVariants _variants = null!;

	[SetUp]
	public void SetUp() => _variants = new MusicPlayerVariants(new MusicPlayerPollNudge(new FakeIntegrationRegistry()));

	[Test]
	public async Task Two_widgets_on_one_instance_each_show_the_state_and_cover_of_their_own_options()
	{
		var every10 = Resolver();
		var every30 = Resolver();
		var config10 = Config(AnyApp, """{"cycleSeconds":10}""");
		var config30 = Config(AnyApp, """{"cycleSeconds":30}""");
		await Resolve(every10, config10);
		await Resolve(every30, config30);

		_variants.Record(Key(10), Playing("App A", "cover-a"));
		_variants.Record(Key(30), Playing("App B", "cover-b"));
		var shown10 = await Resolve(every10, config10);
		var shown30 = await Resolve(every30, config30);

		Assert.Multiple(() =>
		{
			Assert.That(shown10.TrackName, Is.EqualTo("App A"));
			Assert.That(shown30.TrackName, Is.EqualTo("App B"));
			Assert.That(shown10.Artwork!.ResourceId, Is.Not.EqualTo(shown30.Artwork!.ResourceId));
		});
	}

	[Test]
	public async Task A_widget_that_stored_no_values_shows_the_instance_with_its_declared_defaults()
	{
		await Resolve(Resolver(), Config(AnyApp, optionsJson: null));

		Assert.That(_variants.Demanded(), Is.EqualTo(new[] { Variant(10) }));
	}

	[Test]
	public async Task A_new_variant_shows_loading_until_its_first_read_instead_of_the_plain_instance()
	{
		_stateCache.Record(AnyApp, Playing("Current app", artworkId: null));

		var shown = await Resolve(Resolver(), Config(AnyApp, """{"cycleSeconds":10}"""));

		Assert.That(shown.IsLoading, Is.True);
	}

	[Test]
	public async Task The_active_player_and_instances_without_options_keep_showing_the_plain_instance()
	{
		_stateCache.Record(AnyApp, Playing("Current app", artworkId: null));
		_stateCache.Record(Spotify, Playing("Song", artworkId: null));
		_stateCache.ActiveInstanceId = AnyApp;

		var active = await Resolve(Resolver(), Config(instanceId: null, """{"cycleSeconds":10}"""));
		var plain = await Resolve(Resolver(), Config(Spotify, """{"cycleSeconds":10}"""));

		Assert.Multiple(() =>
		{
			Assert.That(active.TrackName, Is.EqualTo("Current app"));
			Assert.That(plain.TrackName, Is.EqualTo("Song"));
			Assert.That(_variants.Demanded(), Is.Empty);
		});
	}

	[Test]
	public async Task A_widget_that_goes_away_stops_asking_for_its_variant()
	{
		var resolver = Resolver();
		await Resolve(resolver, Config(AnyApp, """{"cycleSeconds":10}"""));

		resolver.ReleaseDemand();

		Assert.That(_variants.Demanded(), Is.Empty);
	}

	[Test]
	public async Task Coming_back_to_a_variant_whose_cover_was_dropped_meanwhile_registers_the_cover_again()
	{
		var resolver = Resolver();
		var config10 = Config(AnyApp, """{"cycleSeconds":10}""");
		await Resolve(resolver, config10);
		_variants.Record(Key(10), Playing("App A", "cover-a"));
		var before = await Resolve(resolver, config10);

		await Resolve(resolver, Config(AnyApp, """{"cycleSeconds":30}"""));
		_resources.Remove(before.Artwork!.ResourceId);
		var after = await Resolve(resolver, config10);

		Assert.That(_resources.TryGet(after.Artwork!.ResourceId, out _), Is.True);
	}

	[Test]
	public void An_unrelated_save_parses_to_an_equal_configuration()
	{
		var first = Config(AnyApp, """{"cycleSeconds":10,"order":"newest"}""");
		var reordered = Config(AnyApp, """{ "order": "newest", "cycleSeconds": 10 }""");

		Assert.That(reordered, Is.EqualTo(first));
	}

	private static string Key(double seconds) => Variant(seconds).Key;

	private static MusicPlayerVariant Variant(double seconds)
		=> MusicPlayerVariant.Create(AnyApp, new Dictionary<string, object> { ["cycleSeconds"] = seconds });

	private static MusicPlayerWidgetData Config(string? instanceId, string? optionsJson)
	{
		var instance = instanceId is null ? "null" : $"\"{instanceId}\"";
		var options = optionsJson is null ? string.Empty : $""","instanceOptions":{optionsJson}""";

		return MusicPlayerWidgetData.Parse(MusicPlayerWidgetData.ParseData($$"""{"instanceId":{{instance}}{{options}}}"""));
	}

	private static MusicPlayerStatePayload Playing(string track, string? artworkId)
		=> new()
		{
			InstanceId = AnyApp,
			IsConnected = true,
			IsPlaying = true,
			PlaybackState = "playing",
			TrackName = track,
			ArtworkId = artworkId,
		};

	private MusicPlayerViewStateResolver Resolver()
		=> new(_registry,
			_stateCache,
			_variants,
			new StubArtworkService(),
			new StubPaletteExtractor(),
			new FakeIntegrationRegistry(),
			_resources,
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

	private static Task<MusicPlayerViewState> Resolve(MusicPlayerViewStateResolver resolver, MusicPlayerWidgetData config)
		=> resolver.ResolveAsync(config, MusicPlayerViewState.Loading, CancellationToken.None);

	private sealed class OptionsRegistry : IMusicPlayerRegistry
	{
		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances() =>
		[
			new(AnyApp, "system-media", LocalizedText.FromLiteral("System Media"), "Any app", false)
			{
				Options = [ActionParameter.Number("cycleSeconds", "Cycle every", min: 5, max: 60, defaultValue: 10)],
			},
			new(Spotify, "spotify", LocalizedText.FromLiteral("Spotify"), "Spotify (alice)", false),
		];

		public IMusicPlayer? GetPlayer(string instanceId) => null;

		public IMusicPlayer? GetPlayerWithOptions(string instanceId, IReadOnlyDictionary<string, object> options)
			=> null;

		public IMusicPlayer? DefaultPlayer => null;
	}
}
