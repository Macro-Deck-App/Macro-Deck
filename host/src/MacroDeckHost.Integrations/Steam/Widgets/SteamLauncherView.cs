using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal static class SteamLauncherView
{
	public const string Root = "steamLauncher";

	private const int RowSize = 3;

	private static readonly SteamInk _onArtwork = new(true);
	private static readonly UiLength _name = UiLength.Capped(0.09, 12);
	private static readonly UiLength _tileRadius = UiLength.Capped(0.06, 8);
	private static readonly UiLength _gap = UiLength.Capped(0.045, 6);

	public static UiElement Build(
		UiState<SteamLauncherState> state,
		Func<int, UiEventOutcome> launch,
		int cornerRadius = SteamSurfaces.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(launch);

		var ink = SteamInk.For(backgroundColor);

		return SteamStyle.Root(Root,
			backgroundColor,
			new UiLayer
			{
				Key = "content",
				Fill = true,
				Children =
				[
					new UiStack
					{
						Key = "body",
						Fill = true,
						Children =
						[
							Gate(state,
								ink,
								cornerRadius,
								"unavailable",
								SteamWidgetStatus.Unavailable,
								Strings.Unavailable()),
							Gate(state, ink, cornerRadius, "notFound", SteamWidgetStatus.NotFound, Strings.NotFound()),
							Gate(state,
								ink,
								cornerRadius,
								"noGameSelected",
								SteamWidgetStatus.NoGameSelected,
								Strings.NoGameSelected()),
							Gate(state, ink, cornerRadius, "noGames", SteamWidgetStatus.NoGames, Strings.NoGames()),
							Gate(state,
								ink,
								cornerRadius,
								"noFavorites",
								SteamWidgetStatus.NoFavorites,
								Strings.NoFavorites()),
							new UiWhen
							{
								Key = "readyGate",
								Condition = () => state.Value.Status == SteamWidgetStatus.Ready,
								Content = () => Layout(state, launch, cornerRadius)
							}
						]
					},
					Failure(state, cornerRadius)
				]
			});
	}

	private static UiWhen Gate(
		UiState<SteamLauncherState> state,
		SteamInk ink,
		int cornerRadius,
		string key,
		SteamWidgetStatus status,
		LocalizedText text)
	{
		return new UiWhen
		{
			Key = key + "Gate",
			Condition = () => state.Value.Status == status,
			Content = () => SteamStyle.Notice(key, text, () => state.Value.Glyph, ink, cornerRadius)
		};
	}

	private static UiWhen Failure(UiState<SteamLauncherState> state, int cornerRadius)
	{
		return new UiWhen
		{
			Key = "failureGate",
			Condition = () => !state.Value.Failure.IsEmpty,
			Content = () => new UiStack
			{
				Key = "failureLayer",
				Fill = true,
				Justify = UiComponentJustify.End,
				Align = UiComponentAlignments.Center,
				Padding = SteamSurfaces.SafeArea(cornerRadius),
				Children =
				[
					new UiModifier
					{
						Key = "failurePill",
						Background = "#000000cc",
						Radius = UiLength.Capped(0.1, 12),
						Padding = UiLength.Capped(0.04, 5),
						Child = new UiTextRun
						{
							Key = "failure",
							Text = UiText.Optional(() => state.Value.Failure),
							Size = _name,
							Color = SteamStyle.Failure,
							Align = UiComponentAlignments.Center,
							Wrap = true,
							MaxLines = 2
						}
					}
				]
			}
		};
	}

	private static UiResponsive Layout(
		UiState<SteamLauncherState> state,
		Func<int, UiEventOutcome> launch,
		int cornerRadius)
	{
		return new UiResponsive
		{
			Key = "layout",
			Fill = true,
			Default = new UiStack
			{
				Key = "single",
				Children =
				[
					new UiRepeat<SteamLauncherTile>
					{
						Key = "singleTiles",
						Items = UiValue.From(() => state.Value.Row(0, 1)),
						KeySelector = tile => tile.Key,
						Template = (tile, _) => Tile(tile, launch) with
						{
							Fill = true, Corner = UiComponentButtonCorners.Tile
						}
					}
				]
			},
			Variants =
			[
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					MinHeight = 1.5,
					Content = Padded("grid",
						cornerRadius,
						UiComponentDirections.Vertical,
						[
							Row(state, launch, "first", 0, UiComponentDirections.Horizontal),
							Row(state, launch, "second", RowSize, UiComponentDirections.Horizontal)
						])
				},
				new UiResponsiveVariant
				{
					MinHeight = 1.5,
					Content = Padded("tallBox",
						cornerRadius,
						UiComponentDirections.Vertical,
						[Row(state, launch, "tall", 0, UiComponentDirections.Vertical)])
				},
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					Content = Padded("wideBox",
						cornerRadius,
						UiComponentDirections.Horizontal,
						[Row(state, launch, "wide", 0, UiComponentDirections.Horizontal)])
				}
			]
		};
	}

	private static UiStack Padded(string key, int cornerRadius, string direction, IReadOnlyList<UiElement> children)
	{
		return new UiStack
		{
			Key = key,
			Direction = direction,
			Padding = SteamSurfaces.SafeArea(cornerRadius),
			Gap = _gap,
			Children = children
		};
	}

	private static UiStack Row(
		UiState<SteamLauncherState> state,
		Func<int, UiEventOutcome> launch,
		string key,
		int skip,
		string direction)
	{
		return new UiStack
		{
			Key = key,
			Direction = direction,
			Fill = true,
			Gap = _gap,
			Children =
			[
				new UiRepeat<SteamLauncherTile>
				{
					Key = key + "Tiles",
					Items = UiValue.From(() => state.Value.Row(skip, RowSize)),
					KeySelector = tile => tile.Key,
					Template = (tile, _) => RoundedTile(tile, launch)
				}
			]
		};
	}

	private static UiModifier RoundedTile(SteamLauncherTile tile, Func<int, UiEventOutcome> launch)
	{
		return new UiModifier
			{ Key = "tile-" + tile.Key, Radius = _tileRadius, Child = Tile(tile, launch) with { Fill = true } };
	}

	private static UiButton Tile(SteamLauncherTile tile, Func<int, UiEventOutcome> launch)
	{
		return new UiButton
		{
			Key = tile.Key,
			Align = UiComponentAlignments.Center,
			Justify = UiComponentJustify.Center,
			Padding = UiLength.Capped(0.05, 6),
			Background = SteamStyle.Placeholder,
			Fit = UiComponentImageFits.Cover,
			Transition = UiComponentImageTransitions.Crossfade,
			Source = tile.Cover is { } cover ? UiValue.Of(cover) : UiValue.None<UiResource>(),
			Events = [UiEventHandler.On(UiComponentEvents.Press, _ => launch(tile.AppId))],
			Children = tile.Cover is null
				?
				[
					SteamStyle.Text("name",
						tile.Name,
						_name,
						_onArtwork,
						weight: UiComponentTextWeights.SemiBold,
						maxLines: 3,
						align: UiComponentAlignments.Center)
				]
				: []
		};
	}
}
