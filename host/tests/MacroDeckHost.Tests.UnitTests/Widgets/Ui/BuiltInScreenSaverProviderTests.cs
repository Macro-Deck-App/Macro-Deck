using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Widgets.MusicPlayer;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.ScreenSavers;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
internal sealed class BuiltInScreenSaverProviderTests
{
	private BuiltInScreenSaverProvider _provider = null!;

	[SetUp]
	public void SetUp()
		=> _provider = new BuiltInScreenSaverProvider(new StubRegistry(),
			new StubStateCache(),
			new StubArtworkService(),
			new StubPaletteExtractor(),
			new MusicPlayerStateNotifier(),
			new RecordingRenderSignals(),
			new FakeIntegrationRegistry(),
			new NullResourceStore(),
			TimeProvider.System,
			Serilog.Core.Logger.None);

	[Test]
	public void Offers_the_clock_and_now_playing_under_its_own_id()
	{
		var screenSavers = _provider.GetScreenSavers();

		Assert.Multiple(() =>
		{
			Assert.That(screenSavers.Select(descriptor => descriptor.Id), Is.EquivalentTo(new[] { "clock", "now-playing" }));
			Assert.That(_provider.IntegrationId, Is.EqualTo(BuiltInScreenSavers.ProviderId));
			Assert.That(screenSavers.All(descriptor => descriptor.HasConfiguration), Is.True);
			Assert.That(screenSavers.Any(descriptor => descriptor.Interactive), Is.False);
		});
	}

	[Test]
	public async Task The_clock_screensaver_draws_a_ticking_time_with_the_configured_options()
	{
		var session = await _provider.CreateSessionAsync(
			ScreenSaverRequest(BuiltInScreenSavers.Clock, """{"showSeconds":true,"showDate":false,"hourCycle":"24h"}"""),
			CancellationToken.None);
		var options = ClockScreenSaverData.Parse(
			JsonDocument.Parse("""{"showSeconds":true,"showDate":false,"hourCycle":"24h"}""").RootElement);
		var runs = UiTestHost.Render(ClockScreenSaverView.Build(options, new UiState<int>(0)))
			.ByType(UiMacroDeckComponents.DynamicText);
		var withDate = UiTestHost.Render(ClockScreenSaverView.Build(new ClockScreenSaverData(), new UiState<int>(0)))
			.ByType(UiMacroDeckComponents.DynamicText);

		Assert.Multiple(() =>
		{
			Assert.That(session, Is.TypeOf<DriftingScreenSaverSession>());
			Assert.That(runs, Is.Not.Empty);
			Assert.That(runs.All(run => run.Flag(UiComponentProperties.Seconds) == true), Is.True);
			Assert.That(runs.Select(run => run.Text(UiComponentProperties.Format)), Does.Contain(UiTimeFormats.Time24Hour));
			Assert.That(runs.Select(run => run.Text(UiComponentProperties.Format)), Does.Not.Contain(UiTimeFormats.Date));
			Assert.That(withDate.Select(run => run.Text(UiComponentProperties.Format)), Does.Contain(UiTimeFormats.Date));
		});
	}

	[Test]
	public async Task A_screensaver_it_does_not_provide_is_declined()
	{
		var session = await _provider.CreateSessionAsync(
			ScreenSaverRequest(BuiltInScreenSavers.ProviderId + "::photos", "{}"),
			CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	[Test]
	public async Task Now_playing_shows_the_clock_while_nothing_plays()
	{
		var session = await _provider.CreateSessionAsync(
			ScreenSaverRequest(BuiltInScreenSavers.NowPlaying, "{}"),
			CancellationToken.None);
		var host = UiTestHost.Render(NowPlayingScreenSaverView.Build(
			new UiState<MusicPlayerViewState>(MusicPlayerViewState.Loading),
			new UiState<MusicPlayerWidgetData>(new MusicPlayerWidgetData()),
			MusicPlayerWidgetIcons.EnsureRegistered(new NullResourceStore()),
			new ClockScreenSaverData(),
			new UiState<int>(0)));

		Assert.Multiple(() =>
		{
			Assert.That(session, Is.TypeOf<DriftingScreenSaverSession>());
			Assert.That(host.ByType(UiMacroDeckComponents.DynamicText), Is.Not.Empty);
		});
	}

	[Test]
	public async Task The_clock_configuration_offers_the_three_options()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>
			{
				[UiConfigSurfaceAttributes.EntryPoint] = JsonSerializer.SerializeToElement(UiConfigEntryPoints.ScreenSaverConfig),
				[UiConfigSurfaceAttributes.ScreenSaverId] = JsonSerializer.SerializeToElement(BuiltInScreenSavers.Clock),
				[UiConfigSurfaceAttributes.ScreenSaverConfiguration] = JsonDocument.Parse("""{"showDate":false}""").RootElement,
			}
		};

		var session = await _provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None);
		var host = UiTestHost.Render(ClockScreenSaverConfigView.Build(
			surface.Attributes[UiConfigSurfaceAttributes.ScreenSaverConfiguration]));

		Assert.Multiple(() =>
		{
			Assert.That(session, Is.Not.Null);
			Assert.That(host.FindById("hourCycle"), Is.Not.Null);
			Assert.That(host.FindById("showSeconds"), Is.Not.Null);
			Assert.That(host.FindById("showDate")?.Flag(UiConfigProperties.Value), Is.False);
		});
	}

	private static UiSessionRequest ScreenSaverRequest(string screenSaverId, string configuration)
		=> new() { Surface = ScreenSaverSurface(screenSaverId, configuration), UiModelVersion = 1 };

	private static UiSurface ScreenSaverSurface(string screenSaverId, string configuration)
		=> new()
		{
			Kind = UiSurfaceKinds.ScreenSaver,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>
			{
				[UiScreenSaverSurfaceAttributes.DeviceId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiScreenSaverSurfaceAttributes.ScreenSaverId] = JsonSerializer.SerializeToElement(screenSaverId),
				[UiScreenSaverSurfaceAttributes.Configuration] = JsonDocument.Parse(configuration).RootElement,
			}
		};

	private sealed class NullResourceStore : IUiResourceStore
	{
		public UiResource Register(UiResourceRegistration registration)
			=> new() { ResourceId = registration.OwnerId + "/" + registration.Name, ContentHash = "sha256:00" };

		public bool TryGet(string resourceId, out UiResourceContent content)
		{
			content = null!;
			return false;
		}
	}
}
