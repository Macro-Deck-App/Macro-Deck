using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Steam;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.Steam;

public sealed class SteamUiProvider : IBuiltInIntegrationUiProvider
{
	private readonly IIntegrationRegistry _integrations;
	private readonly SteamArtworkCache _artwork;
	private readonly IUiResourceStore _resources;
	private readonly IHostLockState _lockState;
	private readonly IUiTransport _transport;
	private readonly TimeProvider _time;

	public SteamUiProvider(
		IIntegrationRegistry integrations,
		SteamArtworkCache artwork,
		IUiResourceStore resources,
		IHostLockState lockState,
		IUiTransport transport,
		TimeProvider time)
	{
		_integrations = integrations;
		_artwork = artwork;
		_resources = resources;
		_lockState = lockState;
		_transport = transport;
		_time = time;
	}

	public string IntegrationId => SteamWidgetType.OwnerId;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;
		var source = _integrations.Integrations.OfType<ISteamSource>().FirstOrDefault();

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			var data = WidgetConfigSurfaces.Data(surface);

			if (WidgetConfigSurfaces.IsFor(surface, SteamWidgetType.CurrentlyPlayingQualifiedId))
			{
				return Task.FromResult<IUiSession?>(new WidgetConfigSession(
					new UiView(surface, SteamWidgetConfigViews.Playing(data, source?.Snapshot.Accounts ?? []))));
			}

			return Task.FromResult<IUiSession?>(
				WidgetConfigSurfaces.IsFor(surface, SteamWidgetType.GameLauncherQualifiedId)
					? new WidgetConfigSession(new UiView(surface,
						SteamWidgetConfigViews.Launcher(data, source?.Snapshot.Games ?? [])))
					: null);
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var widgetData = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var element) &&
			element.ValueKind == JsonValueKind.Object
				? element
				: default;
		var radius = WidgetSafeArea.RadiusOf(surface);
		var background = SteamWidgetSettings.BackgroundColor(widgetData);
		var glyph = SteamWidgetIcons.Logo(_resources, _integrations);

		switch (ReadString(surface, UiWidgetSurfaceAttributes.WidgetType))
		{
			case SteamWidgetType.CurrentlyPlayingQualifiedId:
				var playing = SteamWidgetSettings.ParsePlaying(widgetData);

				if (WidgetSamplePreview.IsRequested(surface))
				{
					var sample = new UiState<SteamPlayingState>(new SteamPlayingState(SteamWidgetStatus.Ready,
						"Half-Life 2",
						SteamWidgetStates.Elapsed(TimeSpan.FromMinutes(83)),
						null,
						glyph));
					return Task.FromResult<IUiSession?>(new StaticWidgetUiSession(new UiView(surface,
						SteamPlayingView.Build(sample, radius, background))));
				}

				return Task.FromResult<IUiSession?>(new SteamPlayingSession(surface,
					source,
					playing,
					_artwork,
					glyph,
					_time,
					state => SteamPlayingView.Build(state, radius, background)));

			case SteamWidgetType.GameLauncherQualifiedId:
				var launcher = SteamWidgetSettings.ParseLauncher(widgetData);

				if (WidgetSamplePreview.IsRequested(surface))
				{
					var launcherSample = new UiState<SteamLauncherState>(new SteamLauncherState(SteamWidgetStatus.Ready,
						[new SteamLauncherTile("game-220", 220, "Half-Life 2", null)],
						glyph,
						default));
					return Task.FromResult<IUiSession?>(new StaticWidgetUiSession(new UiView(surface,
						SteamLauncherView.Build(launcherSample, _ => UiEventOutcome.Accepted, radius,
							background))));
				}

				var widgetId = Guid.TryParse(ReadString(surface, UiWidgetSurfaceAttributes.WidgetId), out var id)
					? id
					: (Guid?)null;

				return Task.FromResult<IUiSession?>(new SteamLauncherSession(surface,
					widgetId,
					source,
					launcher,
					_artwork,
					glyph,
					_lockState,
					_transport,
					_time,
					(state, launch) => SteamLauncherView.Build(state, launch, radius, background)));

			default:
				return Task.FromResult<IUiSession?>(null);
		}
	}

	private static string? ReadString(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}
