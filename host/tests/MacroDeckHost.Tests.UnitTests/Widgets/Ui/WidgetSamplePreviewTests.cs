using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Clock;
using MacroDeckHost.Widgets.HistoryGraph;
using MacroDeckHost.Widgets.Slider;
using MacroDeckHost.Widgets.Weather;
using MacroDeck.Sdk.Weather;
using Microsoft.Extensions.DependencyInjection;
using MacroDeckHost.Tests.UnitTests.Triggers;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// What the widget picker draws in each card (issue #758): a widget rendered from representative sample
/// content while nothing is set up yet - no weather station, no music provider, no variable, no bound
/// action. Each case is paired with the ordinary preview of the same empty configuration, which is what
/// the picker used to be able to show: those pairs are what would fail if a sample quietly fell back to
/// the live reading again.
/// </summary>
[TestFixture]
public class WidgetSamplePreviewTests
{
	[Test]
	public async Task Weather_shows_a_reading_where_the_ordinary_preview_reports_no_station()
	{
		var provider = new WeatherWidgetUiProvider(new EmptyWeatherRegistry(),
			new UiResourceStore(),
			new WeatherStateNotifier(),
			TestLocalization.SampleText,
			Serilog.Log.Logger);

		var sample = await Texts(provider, sample: true);
		var live = await Texts(provider, sample: false);

		Assert.Multiple(() =>
		{
			Assert.That(sample, Does.Contain(Resolve(AppStrings.Widgets.SamplePreview.WeatherLocation())));
			Assert.That(sample, Does.Contain("21"), "the sample card must show a temperature");
			Assert.That(live,
				Does.Not.Contain(Resolve(AppStrings.Widgets.SamplePreview.WeatherLocation())),
				"a preview that was not asked for the sample must keep reading the configured station");
		});
	}

	[Test]
	public async Task The_history_graph_plots_a_window_where_the_ordinary_preview_has_none()
	{
		var provider = new HistoryGraphWidgetUiProvider(new VariableRegistry(),
			new UnusedVariableHistory(),
			new VariableChangeNotifier(),
			TestLocalization.SampleText);

		var sample = await Tree(provider, sample: true);
		var live = await Tree(provider, sample: false);

		Assert.Multiple(() =>
		{
			Assert.That(Points(sample), Is.Not.Empty, "the sample card must plot a history");
			Assert.That(Points(live), Is.Empty);
			Assert.That(TextsOf(sample), Does.Contain(Resolve(AppStrings.Widgets.SamplePreview.HistoryGraphTitle())));
			Assert.That(TextsOf(sample), Does.Contain("42"), "the sample card must show a value");
		});
	}

	[Test]
	public async Task The_music_player_shows_a_track_where_the_ordinary_preview_reports_no_connection()
	{
		var provider = new MacroDeckHost.Widgets.MusicPlayer.MusicPlayerWidgetUiProvider(new StubRegistry(),
			new StubStateCache(),
			new StubArtworkService(),
			new StubPaletteExtractor(),
			new MacroDeckHost.Application.MusicPlayer.MusicPlayerStateNotifier(),
			new MacroDeckHost.Application.Rendering.WidgetRenderSignals(),
			new FakeIntegrationRegistry(),
			new UiResourceStore(),
			TestLocalization.SampleText,
			TimeProvider.System,
			Serilog.Log.Logger);

		var sample = await Texts(provider, sample: true);
		var live = await Texts(provider, sample: false);

		Assert.Multiple(() =>
		{
			Assert.That(sample, Does.Contain(Resolve(AppStrings.Widgets.SamplePreview.MusicTrack())));
			Assert.That(sample, Does.Contain(Resolve(AppStrings.Widgets.SamplePreview.MusicArtist())));
			Assert.That(live, Does.Not.Contain(Resolve(AppStrings.Widgets.SamplePreview.MusicTrack())));
		});
	}

	[Test]
	public async Task The_slider_is_labelled_and_sits_at_a_representative_level()
	{
		var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
		var provider = new SliderWidgetUiProvider(new NoWidgetIcons(),
			new FakeHostLockState(),
			TestLocalization.SampleText,
			TimeProvider.System,
			new VariableRegistry(),
			new VariableChangeNotifier(),
			scopeFactory,
			new SliderWidgetSessionTests.RecordingTriggerService(),
			new StubFolderCache(),
			new SliderWidgetSessionTests.NullUiTransport());

		var sample = await Tree(provider, sample: true);

		Assert.Multiple(() =>
		{
			Assert.That(TextsOf(sample), Does.Contain(Resolve(AppStrings.Widgets.SamplePreview.SliderLabel())));
			Assert.That(Properties(sample, "level"), Does.Contain("0.65"));
		});
	}

	[Test]
	public async Task The_clock_draws_a_time_the_reader_resolves_itself()
	{
		var provider = new ClockWidgetUiProvider();

		var sample = await Tree(provider, sample: true);

		// The Clock reads nothing live to begin with - it carries a time reference the reader resolves -
		// so its sample is the widget itself, and this is what says so.
		Assert.That(AllProperties(sample), Does.Contain("$time"));
	}

	private static string Resolve(MacroDeck.Localization.LocalizedString value)
		=> TestLocalization.Resolve(value, "en");

	private static async Task<UiNode> Tree(IUiProvider provider, bool sample)
	{
		var session = await provider.CreateSessionAsync(Request(sample), CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var root = session!.BuildTree().Root;

		await session.DisposeAsync();

		return root;
	}

	private static async Task<string> Texts(IUiProvider provider, bool sample) => TextsOf(await Tree(provider, sample));

	private static string TextsOf(UiNode root) => string.Join('\n',
		Walk(root)
			.Select(node => node.Properties.TryGetValue("text", out var text) ? text.ToString() : string.Empty));

	/// <summary>Every property on every node, flattened - for an assertion about what the tree carries
	/// rather than about which node carries it.</summary>
	private static string AllProperties(UiNode root) => string.Join('\n',
		Walk(root)
			.SelectMany(node => node.Properties.Values.Select(value => value.ToString())));

	private static string Properties(UiNode root, string name) => string.Join('\n',
		Walk(root)
			.Where(node => node.Properties.ContainsKey(name))
			.Select(node => node.Properties[name].ToString()));

	private static List<double> Points(UiNode root)
	{
		var raw = Walk(root)
			.Where(node => node.Properties.ContainsKey("points"))
			.Select(node => node.Properties["points"])
			.FirstOrDefault();

		return raw.ValueKind == JsonValueKind.Array
			? raw.EnumerateArray().Select(value => value.GetDouble()).ToList()
			: [];
	}

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children)
		{
			foreach (var nested in Walk(child))
			{
				yield return nested;
			}
		}

		if (node.Fallback is not null)
		{
			foreach (var nested in Walk(node.Fallback))
			{
				yield return nested;
			}
		}
	}

	private static UiSessionRequest Request(bool sample)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse("{}").RootElement.Clone(),
		};

		if (sample)
		{
			attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		}

		return new UiSessionRequest
		{
			UiModelVersion = 1,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared, Attributes = attributes,
			},
		};
	}

	private sealed class EmptyWeatherRegistry : IWeatherRegistry
	{
		public IReadOnlyList<WeatherStationDescriptor> GetInstances() => [];

		public IWeatherStation? GetStation(string instanceId) => null;

		public IWeatherStation? DefaultStation => null;
	}

	/// <summary>No variable is chosen either, so opening a history window at all is the failure.</summary>
	private sealed class UnusedVariableHistory : IVariableHistory
	{
		public IVariableHistoryWindow Open(string variableName, int capacity, string? scopeRefId = null) =>
			throw new NotSupportedException();
	}

	private sealed class NoWidgetIcons : MacroDeckHost.Application.Widgets.IWidgetIconResources
	{
		public Task<MacroDeck.Ui.Model.Resources.UiResource?> ResolveAsync(
			MacroDeckHost.Domain.Widgets.WidgetIconReference? reference,
			CancellationToken cancellationToken)
			=> Task.FromResult<MacroDeck.Ui.Model.Resources.UiResource?>(null);

		public void Evict(Guid widgetId)
		{
		}
	}
}
