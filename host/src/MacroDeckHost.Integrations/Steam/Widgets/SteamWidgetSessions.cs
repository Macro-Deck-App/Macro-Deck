using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.Steam.State;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal static class SteamWidgetSessions
{
	public const string IconFolder = "widget";

	public const string LogoIcon = "logo";

	public static IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
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

			if (SteamSurfaces.IsConfigFor(surface, SteamWidgetType.FriendsQualifiedId))
			{
				return new SteamViewSession(new UiView(surface,
					SteamWidgetConfigViews.Friends(data, source?.Snapshot.Accounts ?? [])));
			}

			if (SteamSurfaces.IsConfigFor(surface, SteamWidgetType.LibraryQualifiedId))
			{
				return new SteamViewSession(new UiView(surface,
					SteamWidgetConfigViews.Library(data, source?.Snapshot.Accounts ?? [])));
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
					return new SteamViewSession(new UiView(surface,
						SteamPlayingView.Build(sample, radius, background)));
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

			case SteamWidgetType.FriendsQualifiedId:
				if (SteamSurfaces.IsSampleRequested(surface))
				{
					var sample = new UiState<SteamFriendsWidgetState>(new SteamFriendsWidgetState(
						SteamWidgetStatus.Ready,
						3,
						1,
						[
							new SteamFriendRow("friend-1", "Gordon", "Half-Life 2", SteamStyle.InGame, null, "G"),
							new SteamFriendRow("friend-2",
								"Alyx",
								Strings.Persona.Online(),
								SteamStyle.Online,
								null,
								"A"),
							new SteamFriendRow("friend-3", "Barney", Strings.Persona.Away(), SteamStyle.Away, null, "B")
						],
						glyph));
					return new SteamViewSession(new UiView(surface,
						SteamFriendsView.Build(sample, radius, background)));
				}

				var friendsOptions = SteamWidgetSettings.ParseAccount(widgetData);
				return new SteamStateSession<SteamFriendsWidgetState>(surface,
					source,
					artwork,
					() => SteamAccountWidgetStates.Friends(source, friendsOptions, artwork, glyph),
					state => SteamFriendsView.Build(state, radius, background));

			case SteamWidgetType.LibraryQualifiedId:
				if (SteamSurfaces.IsSampleRequested(surface))
				{
					var sample = new UiState<SteamLibraryWidgetState>(new SteamLibraryWidgetState(
						SteamWidgetStatus.Ready,
						true,
						128,
						"1248",
						"12.5",
						[
							new SteamLibraryGame("game-220",
								220,
								"Half-Life 2",
								null,
								Strings.RecentPlaytime("12.5")),
							new SteamLibraryGame("game-620", 620, "Portal 2", null, Strings.TotalPlaytime("38"))
						],
						glyph));
					return new SteamViewSession(new UiView(surface,
						SteamLibraryView.Build(sample, radius, background)));
				}

				var libraryOptions = SteamWidgetSettings.ParseAccount(widgetData);
				return new SteamStateSession<SteamLibraryWidgetState>(surface,
					source,
					artwork,
					() => SteamAccountWidgetStates.Library(source, libraryOptions, artwork, glyph),
					state => SteamLibraryView.Build(state, radius, background));

			default:
				return null;
		}
	}
}
