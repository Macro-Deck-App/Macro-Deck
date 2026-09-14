using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.ScreenSavers;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.MusicPlayer;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.ScreenSavers;

public sealed class BuiltInScreenSaverProvider : IScreenSaverProvider, IBuiltInIntegrationUiProvider
{
	private const string ClockId = "clock";
	private const string NowPlayingId = "now-playing";

	private readonly IMusicPlayerRegistry _registry;
	private readonly IMusicPlayerStateCache _stateCache;
	private readonly IMusicPlayerArtworkService _artworkService;
	private readonly IArtworkPaletteExtractor _paletteExtractor;
	private readonly IMusicPlayerStateNotifier _notifier;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly IIntegrationRegistry _integrations;
	private readonly IUiResourceStore _resources;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public BuiltInScreenSaverProvider(
		IMusicPlayerRegistry registry,
		IMusicPlayerStateCache stateCache,
		IMusicPlayerArtworkService artworkService,
		IArtworkPaletteExtractor paletteExtractor,
		IMusicPlayerStateNotifier notifier,
		IWidgetRenderSignals renderSignals,
		IIntegrationRegistry integrations,
		IUiResourceStore resources,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_registry = registry;
		_stateCache = stateCache;
		_artworkService = artworkService;
		_paletteExtractor = paletteExtractor;
		_notifier = notifier;
		_renderSignals = renderSignals;
		_integrations = integrations;
		_resources = resources;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public string IntegrationId => BuiltInScreenSavers.ProviderId;

	public string ProviderName => "Macro Deck";

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.ScreenSaver, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public Task InitializeAsync(IScreenSaverProviderContext context, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	public IReadOnlyList<ScreenSaverDescriptor> GetScreenSavers()
		=>
		[
			new(ClockId,
				AppStrings.ScreenSavers.Clock.Name(),
				AppStrings.ScreenSavers.Clock.Description(),
				HasConfiguration: true),
			new(NowPlayingId,
				AppStrings.ScreenSavers.NowPlaying.Name(),
				AppStrings.ScreenSavers.NowPlaying.Description(),
				HasConfiguration: true),
		];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			if (Attribute(surface, UiConfigSurfaceAttributes.EntryPoint) != UiConfigEntryPoints.ScreenSaverConfig)
			{
				return null;
			}

			var stored = Element(surface, UiConfigSurfaceAttributes.ScreenSaverConfiguration);

			return LocalId(Attribute(surface, UiConfigSurfaceAttributes.ScreenSaverId)) switch
			{
				ClockId => new WidgetConfigSession(new UiView(surface, ClockScreenSaverConfigView.Build(stored))),
				NowPlayingId => new WidgetConfigSession(new UiView(surface,
					NowPlayingScreenSaverConfigView.Build(stored, _registry))),
				_ => null,
			};
		}

		if (surface.Kind != UiSurfaceKinds.ScreenSaver)
		{
			return null;
		}

		var configuration = Element(surface, UiScreenSaverSurfaceAttributes.Configuration);
		var position = new UiState<int>(Random.Shared.Next(ScreenSaverDrift.PositionCount));

		switch (LocalId(Attribute(surface, UiScreenSaverSurfaceAttributes.ScreenSaverId)))
		{
			case ClockId:
			{
				var view = new UiView(surface,
					ClockScreenSaverView.Build(ClockScreenSaverData.Parse(configuration), position));

				return new DriftingScreenSaverSession(new ScreenSaverViewSession(view), position, _timeProvider);
			}

			case NowPlayingId:
			{
				var config = MusicPlayerWidgetData.Parse(NowPlayingWidgetData(configuration));
				var icons = MusicPlayerWidgetIcons.EnsureRegistered(_resources);
				var resolver = new MusicPlayerViewStateResolver(_registry,
					_stateCache,
					_artworkService,
					_paletteExtractor,
					_integrations,
					_resources,
					_timeProvider,
					_logger);
				var initial = await resolver
					.ResolveAsync(config, MusicPlayerViewState.Loading, cancellationToken)
					.ConfigureAwait(false);
				var state = new UiState<MusicPlayerViewState>(initial);
				var configState = new UiState<MusicPlayerWidgetData>(config);
				var view = new UiView(surface,
					NowPlayingScreenSaverView.Build(state, configState, icons, new ClockScreenSaverData(), position));

				var inner = new MusicPlayerWidgetSession(view,
					state,
					configState,
					resolver,
					_notifier,
					_renderSignals,
					null,
					_logger);

				return new DriftingScreenSaverSession(inner, position, _timeProvider);
			}

			default:
				return null;
		}
	}

	private static JsonElement NowPlayingWidgetData(JsonElement configuration)
		=> JsonSerializer.SerializeToElement(new
		{
			instanceId = WidgetConfigJson.ReadString(configuration, "instanceId"),
			coverStyle = MusicPlayerWidgetData.SmallCoverStyle,
			showHeader = false,
			showTitle = true,
			showArtist = true,
			showAlbum = false,
			showTimeline = false,
		});

	private static string? LocalId(string? qualified)
	{
		if (qualified is null)
		{
			return null;
		}

		var separator = qualified.IndexOf("::", StringComparison.Ordinal);

		return separator < 0 ? qualified : qualified[(separator + 2)..];
	}

	private static string? Attribute(UiSurface surface, string key)
		=> surface.Attributes.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static JsonElement Element(UiSurface surface, string key)
		=> surface.Attributes.TryGetValue(key, out var value) ? value : default;
}
