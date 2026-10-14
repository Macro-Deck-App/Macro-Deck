using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal static class SteamLibraryView
{
	public const string Root = "steamLibrary";

	private static readonly UiLength _row = UiLength.Capped(0.34, 40);
	private static readonly UiLength _thumb = UiLength.Capped(0.23, 27);
	private static readonly UiLength _statValue = UiLength.Capped(0.17, 21);
	private static readonly UiLength _coverWidth = UiLength.OfBasis(0.5);

	public static UiElement Build(
		UiState<SteamLibraryWidgetState> state,
		int cornerRadius = SteamSurfaces.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);

		var ink = SteamInk.For(backgroundColor);

		return SteamStyle.Root(Root,
			backgroundColor,
			new UiStack
			{
				Key = "content",
				Direction = UiComponentDirections.Vertical,
				Fill = true,
				Children =
				[
					Gate(state, ink, cornerRadius, "unavailable", SteamWidgetStatus.Unavailable, Strings.Unavailable()),
					Gate(state, ink, cornerRadius, "notFound", SteamWidgetStatus.NotFound, Strings.NotFound()),
					Gate(state,
						ink,
						cornerRadius,
						"accountUnavailable",
						SteamWidgetStatus.AccountUnavailable,
						Strings.AccountUnavailable()),
					Gate(state, ink, cornerRadius, "private", SteamWidgetStatus.Private, Strings.GamesPrivate()),
					Gate(state, ink, cornerRadius, "noGames", SteamWidgetStatus.NoGames, Strings.NoGames()),
					new UiWhen
					{
						Key = "readyGate",
						Condition = () => state.Value.Status == SteamWidgetStatus.Ready,
						Content = () => Layout(state, ink, cornerRadius)
					}
				]
			});
	}

	private static UiWhen Gate(
		UiState<SteamLibraryWidgetState> state,
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

	private static UiResponsive Layout(UiState<SteamLibraryWidgetState> state, SteamInk ink, int cornerRadius)
	{
		return new UiResponsive
		{
			Key = "layout",
			Fill = true,
			Default = SteamStyle.Padded("small", cornerRadius, Eyebrow(state, ink), Spacer(), Summary(state, ink)),
			Variants =
			[
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					MinHeight = 1.5,
					Content = SteamStyle.Padded("large",
						cornerRadius,
						Header(state, ink),
						Stats(state, ink),
						List(state, ink, 3))
				},
				new UiResponsiveVariant
				{
					MinHeight = 1.5,
					Content = SteamStyle.Padded("tall",
						cornerRadius,
						Eyebrow(state, ink),
						Summary(state, ink),
						List(state, ink, 2))
				},
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					Content = new UiStack
					{
						Key = "wide",
						Direction = UiComponentDirections.Horizontal,
						Padding = SteamSurfaces.SafeArea(cornerRadius),
						Gap = UiLength.Capped(0.1, 12),
						Children =
						[
							new UiStack
							{
								Key = "summaryColumn",
								Direction = UiComponentDirections.Vertical,
								MainSize = UiLength.OfBasis(0.8),
								Children = [Eyebrow(state, ink), Spacer(), Summary(state, ink)]
							},
							Covers(state)
						]
					}
				}
			]
		};
	}

	private static UiStack Eyebrow(UiState<SteamLibraryWidgetState> state, SteamInk ink)
	{
		return SteamStyle.EyebrowRow("eyebrow", Strings.Eyebrow.Library(), () => state.Value.Glyph, ink);
	}

	private static UiStack Summary(UiState<SteamLibraryWidgetState> state, SteamInk ink)
	{
		return new UiStack
		{
			Key = "summary",
			Direction = UiComponentDirections.Vertical,
			Children =
			[
				SteamStyle.Text("value",
					UiText.From(() => state.Value.FromAccount
						? state.Value.TotalHours
						: state.Value.GameCount.ToString(CultureInfo.InvariantCulture)),
					SteamStyle.Numeral,
					ink,
					weight: UiComponentTextWeights.Bold),
				SteamStyle.Text("caption",
					UiText.FromLocalized(() => state.Value.FromAccount ? Strings.HoursPlayed() : Strings.Installed()),
					SteamStyle.Body,
					ink,
					SteamTone.Secondary,
					UiComponentTextWeights.Medium),
				new UiWhen
				{
					Key = "countGate",
					Condition = () => state.Value.FromAccount,
					Content = () => SteamStyle.Text("count",
						UiText.FromLocalized(() => Strings.GamesCount(state.Value.GameCount)),
						SteamStyle.Caption,
						ink,
						SteamTone.Muted)
				}
			]
		};
	}

	private static UiStack Header(UiState<SteamLibraryWidgetState> state, SteamInk ink)
	{
		return new UiStack
		{
			Key = "header",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Justify = UiComponentJustify.SpaceBetween,
			Children =
			[
				Eyebrow(state, ink),
				SteamStyle.Text("count",
					UiText.FromLocalized(() => Strings.GamesCount(state.Value.GameCount)),
					SteamStyle.Caption,
					ink,
					SteamTone.Secondary,
					UiComponentTextWeights.Medium)
			]
		};
	}

	private static UiWhen Stats(UiState<SteamLibraryWidgetState> state, SteamInk ink)
	{
		return new UiWhen
		{
			Key = "statsGate",
			Condition = () => state.Value.FromAccount,
			Content = () => new UiStack
			{
				Key = "stats",
				Direction = UiComponentDirections.Horizontal,
				Gap = SteamStyle.Gap,
				Children =
				[
					Stat("total",
						ink,
						UiText.FromLocalized(() => Strings.HoursValue(state.Value.TotalHours)),
						Strings.HoursPlayed()),
					Stat("recent",
						ink,
						UiText.FromLocalized(() => Strings.HoursValue(state.Value.RecentHours)),
						Strings.LastTwoWeeks())
				]
			}
		};
	}

	private static UiModifier Stat(string key, SteamInk ink, UiText value, UiText caption)
	{
		return new UiModifier
		{
			Key = key,
			Background = "#ffffff14",
			Radius = SteamStyle.ArtworkRadius,
			Padding = UiLength.Capped(0.05, 7),
			Fill = true,
			Child = new UiStack
			{
				Key = "stat",
				Direction = UiComponentDirections.Vertical,
				Children =
				[
					SteamStyle.Text("value", value, _statValue, ink, weight: UiComponentTextWeights.Bold),
					SteamStyle.Text("caption", caption, SteamStyle.Caption, ink, SteamTone.Secondary)
				]
			}
		};
	}

	private static UiStack List(UiState<SteamLibraryWidgetState> state, SteamInk ink, int count)
	{
		return new UiStack
		{
			Key = "recent",
			Direction = UiComponentDirections.Vertical,
			Gap = SteamStyle.Gap,
			Children =
			[
				new UiRepeat<SteamLibraryGame>
				{
					Key = "games",
					Items = UiValue.From(() => state.Value.Take(count)),
					KeySelector = game => game.Key,
					Template = (game, _) => Row(game, ink)
				}
			]
		};
	}

	private static UiStack Row(SteamLibraryGame game, SteamInk ink)
	{
		return new UiStack
		{
			Key = game.Key,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			MainSize = _row,
			Gap = UiLength.Capped(0.06, 8),
			Children =
			[
				SteamStyle.Rounded("thumb", UiLength.Capped(0.04, 5), Artwork(game), _thumb),
				new UiStack
				{
					Key = "texts",
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.Center,
					Fill = true,
					Children =
					[
						SteamStyle.Text("name",
							game.Name,
							UiLength.Capped(0.1, 12),
							ink,
							weight: UiComponentTextWeights.SemiBold),
						.. game.Detail.IsEmpty
							? Array.Empty<UiElement>()
							:
							[
								SteamStyle.Text("detail",
									game.Detail,
									UiLength.Capped(0.085, 10.5),
									ink,
									SteamTone.Secondary)
							]
					]
				}
			]
		};
	}

	private static UiStack Covers(UiState<SteamLibraryWidgetState> state)
	{
		return new UiStack
		{
			Key = "covers",
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.End,
			Fill = true,
			Gap = SteamStyle.Gap,
			Children =
			[
				new UiRepeat<SteamLibraryGame>
				{
					Key = "games",
					Items = UiValue.From(() => state.Value.Take(2)),
					KeySelector = game => game.Key,
					Template = (game, _) => SteamStyle.Rounded(game.Key,
						SteamStyle.ArtworkRadius,
						Artwork(game),
						_coverWidth)
				}
			]
		};
	}

	private static UiElement Artwork(SteamLibraryGame game)
	{
		return game.Cover is { } cover
			? new UiButton
			{
				Key = "cover",
				Background = "#000000",
				Fit = UiComponentImageFits.Cover,
				Transition = UiComponentImageTransitions.Crossfade,
				Source = cover
			}
			: new UiStack
			{
				Key = "placeholder",
				Align = UiComponentAlignments.Center,
				Justify = UiComponentJustify.Center,
				Background = SteamStyle.Placeholder
			};
	}

	private static UiStack Spacer()
	{
		return new UiStack { Key = "spacer", Fill = true };
	}
}
