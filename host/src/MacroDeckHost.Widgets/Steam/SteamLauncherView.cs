using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Widgets.Steam;

internal static class SteamLauncherView
{
	public const string Root = "steamLauncher";

	private const string TilePlaceholder = "#1b2838";
	private const string FailureColor = "#e5534b";
	private const int RowSize = 3;

	private static readonly UiLength _name = UiLength.Capped(0.08, 11);
	private static readonly UiLength _notice = UiLength.Capped(0.09, 12);
	private static readonly UiLength _glyph = UiLength.Capped(0.2, 28);
	private static readonly UiLength _gap = UiLength.Capped(0.035, 4);

	public static UiElement Build(
		UiState<SteamLauncherState> state,
		Func<int, UiEventOutcome> launch,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(launch);

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = backgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Gap = _gap,
			Children =
			[
				Gate(state, "unavailable", SteamWidgetStatus.Unavailable, Strings.Unavailable()),
				Gate(state, "notFound", SteamWidgetStatus.NotFound, Strings.NotFound()),
				Gate(state, "noGameSelected", SteamWidgetStatus.NoGameSelected, Strings.NoGameSelected()),
				Gate(state, "noGames", SteamWidgetStatus.NoGames, Strings.NoGames()),
				new UiWhen
				{
					Key = "readyGate",
					Condition = () => state.Value.Status == SteamWidgetStatus.Ready,
					Content = () => Layout(state, launch),
				},
				new UiWhen
				{
					Key = "failureGate",
					Condition = () => !state.Value.Failure.IsEmpty,
					Content = () => new UiTextRun
					{
						Key = "failure",
						Text = UiText.Optional(() => state.Value.Failure),
						Size = _name,
						Color = FailureColor,
						MaxLines = 2,
					},
				},
			],
		};
	}

	private static UiWhen Gate(UiState<SteamLauncherState> state, string key, SteamWidgetStatus status,
		LocalizedText text)
		=> new()
		{
			Key = key + "Gate",
			Condition = () => state.Value.Status == status,
			Content = () => new UiStack
			{
				Key = key,
				Direction = UiComponentDirections.Vertical,
				Align = UiComponentAlignments.Center,
				Justify = UiComponentJustify.Center,
				Gap = _gap,
				Fill = true,
				Children =
				[
					new UiImage
					{
						Key = "glyph",
						Size = _glyph,
						MainSize = _glyph,
						Source = UiValue.Optional(() => state.Value.Glyph is { } glyph
							? UiValue.Of(glyph)
							: UiValue.None<UiResource>()),
					},
					new UiTextRun
					{
						Key = "text",
						Text = text,
						Size = _notice,
						Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center,
						Wrap = true,
						MaxLines = 3,
					},
				],
			},
		};

	private static UiResponsive Layout(UiState<SteamLauncherState> state, Func<int, UiEventOutcome> launch)
		=> new()
		{
			Key = "layout",
			Fill = true,
			Default = Row(state, launch, "single", 0, 1, UiComponentDirections.Horizontal, fill: false),
			Variants =
			[
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					MinHeight = 1.5,
					Content = new UiStack
					{
						Key = "grid",
						Direction = UiComponentDirections.Vertical,
						Gap = _gap,
						Children =
						[
							Row(state, launch, "first", 0, RowSize, UiComponentDirections.Horizontal, fill: true),
							Row(state, launch, "second", RowSize, RowSize, UiComponentDirections.Horizontal, fill: true),
						],
					},
				},
				new UiResponsiveVariant
				{
					MinHeight = 1.5,
					Content = Row(state, launch, "tall", 0, RowSize, UiComponentDirections.Vertical, fill: false),
				},
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					Content = Row(state, launch, "wide", 0, RowSize, UiComponentDirections.Horizontal, fill: false),
				},
			],
		};

	private static UiStack Row(
		UiState<SteamLauncherState> state,
		Func<int, UiEventOutcome> launch,
		string key,
		int skip,
		int take,
		string direction,
		bool fill)
	{
		var row = new UiStack
		{
			Key = key,
			Direction = direction,
			Gap = _gap,
			Children =
			[
				new UiRepeat<SteamLauncherTile>
				{
					Key = key + "Tiles",
					Items = UiValue.From(() => state.Value.Row(skip, take)),
					KeySelector = tile => tile.Key,
					Template = (tile, _) => Tile(tile, launch),
				},
			],
		};

		return fill ? row with { Fill = true } : row;
	}

	private static UiButton Tile(SteamLauncherTile tile, Func<int, UiEventOutcome> launch)
		=> new()
		{
			Key = tile.Key,
			Fill = true,
			Align = UiComponentAlignments.Center,
			Justify = UiComponentJustify.End,
			Background = TilePlaceholder,
			Events = [UiEventHandler.On(UiComponentEvents.Press, _ => launch(tile.AppId))],
			Children =
			[
				tile.Cover is { } cover
					? new UiImage
					{
						Key = "cover",
						Size = UiLength.OfBasis(1),
						Fill = true,
						Transition = UiComponentImageTransitions.Crossfade,
						Source = cover,
					}
					: new UiTextRun
					{
						Key = "name",
						Text = tile.Name,
						Size = _name,
						Role = UiComponentTextRoles.Primary,
						Align = UiComponentAlignments.Center,
						Wrap = true,
						MaxLines = 3,
					},
			],
		};
}
