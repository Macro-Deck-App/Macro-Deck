using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.MusicPlayer;

public sealed class MusicPlayerWidgetUiProvider : IBuiltInWidgetUiProvider
{
	private readonly IMusicPlayerRegistry _registry;
	private readonly IMusicPlayerStateCache _stateCache;
	private readonly IMusicPlayerArtworkService _artworkService;
	private readonly IArtworkPaletteExtractor _paletteExtractor;
	private readonly IMusicPlayerStateNotifier _notifier;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly IIntegrationRegistry _integrations;
	private readonly IUiResourceStore _resources;
	private readonly IWidgetSampleTextResolver _sampleText;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public MusicPlayerWidgetUiProvider(
		IMusicPlayerRegistry registry,
		IMusicPlayerStateCache stateCache,
		IMusicPlayerArtworkService artworkService,
		IArtworkPaletteExtractor paletteExtractor,
		IMusicPlayerStateNotifier notifier,
		IWidgetRenderSignals renderSignals,
		IIntegrationRegistry integrations,
		IUiResourceStore resources,
		IWidgetSampleTextResolver sampleText,
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
		_sampleText = sampleText;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public string WidgetTypeId => WidgetTypeIds.MusicPlayer;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind == UiSurfaceKinds.Config)
		{
			if (!WidgetConfigSurfaces.IsFor(request.Surface, WidgetTypeId))
			{
				return null;
			}

			var configData = WidgetConfigSurfaces.Data(request.Surface);
			var configView = new UiView(request.Surface, MusicPlayerWidgetConfigView.Build(configData, _registry));

			return new WidgetConfigSession(configView);
		}

		if (request.Surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		var config = MusicPlayerWidgetData.Parse(DataElement(request.Surface));
		var icons = MusicPlayerWidgetIcons.EnsureRegistered(_resources);

		if (WidgetSamplePreview.IsRequested(request.Surface))
		{
			var sample = await MusicPlayerWidgetSample.BuildAsync(_sampleText, _timeProvider).ConfigureAwait(false);

			return new StaticWidgetUiSession(new UiView(request.Surface,
				MusicPlayerWidgetView.Build(new UiState<MusicPlayerViewState>(sample),
					new UiState<MusicPlayerWidgetData>(config),
					icons)));
		}

		var resolver = new MusicPlayerViewStateResolver(_registry,
			_stateCache,
			_artworkService,
			_paletteExtractor,
			_integrations,
			_resources,
			_timeProvider,
			_logger);

		// Resolved before the view exists so the very first tree a client receives already shows the
		// player rather than flashing the loading state and correcting itself one patch later.
		var initial = await resolver
			.ResolveAsync(config, MusicPlayerViewState.Loading, cancellationToken)
			.ConfigureAwait(false);

		var state = new UiState<MusicPlayerViewState>(initial);
		var configState = new UiState<MusicPlayerWidgetData>(config);
		var view = new UiView(request.Surface,
			MusicPlayerWidgetView.Build(state, configState, icons, WidgetSafeArea.RadiusOf(request.Surface)));

		return new MusicPlayerWidgetSession(view,
			state,
			configState,
			resolver,
			_notifier,
			_renderSignals,
			WidgetId(request.Surface),
			_logger);
	}

	private static JsonElement DataElement(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ? data : default;

	/// <summary>The stored widget's id, or <c>null</c> on a Preview, which has none.</summary>
	private static string? WidgetId(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.WidgetId, out var id) &&
			id.ValueKind == JsonValueKind.String
				? id.GetString()
				: null;
}
