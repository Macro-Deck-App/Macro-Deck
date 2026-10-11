using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal static class SteamWidgetSessions
{
	public static IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public static IUiSession? Create(
		UiSessionRequest request,
		ISteamSource? source,
		SteamArtworkCache artwork,
		UiResource? glyph,
		TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			var data = SteamSurfaces.ConfigData(surface);

			if (SteamSurfaces.IsConfigFor(surface, SteamWidgetType.CurrentlyPlayingQualifiedId))
			{
				return new SteamViewSession(new UiView(surface,
					SteamWidgetConfigViews.Playing(data, source?.Snapshot.Accounts ?? [])));
			}

			return SteamSurfaces.IsConfigFor(surface, SteamWidgetType.GameLauncherQualifiedId)
				? new SteamViewSession(new UiView(surface,
					SteamWidgetConfigViews.Launcher(data, source?.Snapshot.Games ?? [])))
				: null;
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		var widgetData = SteamSurfaces.WidgetData(surface);
		var radius = SteamSurfaces.CornerRadius(surface);
		var background = SteamWidgetSettings.BackgroundColor(widgetData);

		switch (SteamSurfaces.WidgetType(surface))
		{
			case SteamWidgetType.CurrentlyPlayingQualifiedId:
				if (SteamSurfaces.IsSampleRequested(surface))
				{
					var sample = new UiState<SteamPlayingState>(new SteamPlayingState(SteamWidgetStatus.Ready,
						"Half-Life 2",
						SteamWidgetStates.Elapsed(TimeSpan.FromMinutes(83)),
						null,
						glyph));
					return new SteamViewSession(new UiView(surface, SteamPlayingView.Build(sample, radius, background)));
				}

				return new SteamPlayingSession(surface,
					source,
					SteamWidgetSettings.ParsePlaying(widgetData),
					artwork,
					glyph,
					time,
					state => SteamPlayingView.Build(state, radius, background));

			case SteamWidgetType.GameLauncherQualifiedId:
				if (SteamSurfaces.IsSampleRequested(surface))
				{
					var sample = new UiState<SteamLauncherState>(new SteamLauncherState(SteamWidgetStatus.Ready,
						[new SteamLauncherTile("game-220", 220, "Half-Life 2", null)],
						glyph,
						default));
					return new SteamViewSession(new UiView(surface,
						SteamLauncherView.Build(sample, _ => UiEventOutcome.Accepted, radius, background)));
				}

				return new SteamLauncherSession(surface,
					source,
					SteamWidgetSettings.ParseLauncher(widgetData),
					artwork,
					glyph,
					time,
					(state, launch) => SteamLauncherView.Build(state, launch, radius, background));

			default:
				return null;
		}
	}
}
