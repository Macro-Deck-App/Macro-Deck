using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal static class SteamFriendsView
{
	public const string Root = "steamFriends";

	private static readonly UiLength _row = UiLength.Capped(0.3, 34);
	private static readonly UiLength _avatar = UiLength.Capped(0.22, 26);
	private static readonly UiLength _name = UiLength.Capped(0.1, 12);
	private static readonly UiLength _status = UiLength.Capped(0.085, 10.5);

	public static UiElement Build(
		UiState<SteamFriendsWidgetState> state,
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
					Gate(state, ink, cornerRadius, "needsWebApi", SteamWidgetStatus.NeedsWebApi, Strings.NeedsWebApi()),
					Gate(state,
						ink,
						cornerRadius,
						"accountUnavailable",
						SteamWidgetStatus.AccountUnavailable,
						Strings.AccountUnavailable()),
					Gate(state, ink, cornerRadius, "private", SteamWidgetStatus.Private, Strings.FriendsPrivate()),
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
		UiState<SteamFriendsWidgetState> state,
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

	private static UiResponsive Layout(UiState<SteamFriendsWidgetState> state, SteamInk ink, int cornerRadius)
	{
		return new UiResponsive
		{
			Key = "layout",
			Fill = true,
			Default = SteamStyle.Padded("small",
				cornerRadius,
				Eyebrow(state, ink),
				Spacer(),
				Summary(state, ink)),
			Variants =
			[
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					MinHeight = 1.5,
					Content = SteamStyle.Padded("large", cornerRadius, Header(state, ink), List(state, ink, "rows", 5))
				},
				new UiResponsiveVariant
				{
					MinHeight = 1.5,
					Content = SteamStyle.Padded("tall", cornerRadius, Header(state, ink), List(state, ink, "rows", 4))
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
								MainSize = UiLength.OfBasis(0.75),
								Children = [Eyebrow(state, ink), Spacer(), Summary(state, ink)]
							},
							new UiStack
							{
								Key = "listColumn",
								Direction = UiComponentDirections.Vertical,
								Justify = UiComponentJustify.Center,
								Fill = true,
								Children = [List(state, ink, "rows", 2)]
							}
						]
					}
				}
			]
		};
	}

	private static UiStack Eyebrow(UiState<SteamFriendsWidgetState> state, SteamInk ink)
	{
		return SteamStyle.EyebrowRow("eyebrow", Strings.Eyebrow.Friends(), () => state.Value.Glyph, ink);
	}

	private static UiStack Header(UiState<SteamFriendsWidgetState> state, SteamInk ink)
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
					UiText.FromLocalized(() => Strings.FriendsOnline(state.Value.Online)),
					SteamStyle.Caption,
					ink,
					SteamTone.Secondary,
					UiComponentTextWeights.Medium)
			]
		};
	}

	private static UiStack Summary(UiState<SteamFriendsWidgetState> state, SteamInk ink)
	{
		return new UiStack
		{
			Key = "summary",
			Direction = UiComponentDirections.Vertical,
			Children =
			[
				SteamStyle.Text("online",
					UiText.From(() => state.Value.Online.ToString(CultureInfo.InvariantCulture)),
					SteamStyle.Numeral,
					ink,
					weight: UiComponentTextWeights.Bold),
				SteamStyle.Text("onlineLabel",
					Strings.Persona.Online(),
					SteamStyle.Body,
					ink,
					SteamTone.Secondary,
					UiComponentTextWeights.Medium),
				SteamStyle.StatusLabel("playing",
					UiText.FromLocalized(() => Strings.FriendsPlaying(state.Value.InGame)),
					UiValue.From(() => state.Value.InGame > 0 ? SteamStyle.InGame : SteamStyle.Offline),
					SteamStyle.Caption)
			]
		};
	}

	private static UiStack List(UiState<SteamFriendsWidgetState> state, SteamInk ink, string key, int count)
	{
		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Gap = SteamStyle.Gap,
			Children =
			[
				new UiWhen
				{
					Key = "emptyGate",
					Condition = () => state.Value.Rows.Count == 0,
					Content = () => SteamStyle.Text("empty",
						Strings.NoFriendsOnline(),
						SteamStyle.Body,
						ink,
						SteamTone.Secondary)
				},
				new UiRepeat<SteamFriendRow>
				{
					Key = "friends",
					Items = UiValue.From(() => state.Value.Take(count)),
					KeySelector = row => row.Key,
					Template = (row, _) => Row(row, ink)
				}
			]
		};
	}

	private static UiStack Row(SteamFriendRow row, SteamInk ink)
	{
		return new UiStack
		{
			Key = row.Key,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			MainSize = _row,
			Gap = UiLength.Capped(0.06, 8),
			Children =
			[
				Avatar(row),
				new UiStack
				{
					Key = "texts",
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.Center,
					Fill = true,
					Children =
					[
						SteamStyle.Text("name", row.Name, _name, ink, weight: UiComponentTextWeights.SemiBold),
						new UiStack
						{
							Key = "status",
							Direction = UiComponentDirections.Horizontal,
							Align = UiComponentAlignments.Center,
							Gap = SteamStyle.SmallGap,
							Children =
							[
								SteamStyle.Dot("dot", UiLength.Capped(0.045, 5.5), row.StatusColor),
								new UiTextRun
								{
									Key = "text",
									Text = row.Status,
									Size = _status,
									Role = UiComponentTextRoles.Secondary,
									Color = row.StatusColor == SteamStyle.InGame
										? UiValue.Of(SteamStyle.InGame)
										: ink.Color(SteamTone.Secondary),
									MaxLines = 1
								}
							]
						}
					]
				}
			]
		};
	}

	private static UiModifier Avatar(SteamFriendRow row)
	{
		return new UiModifier
		{
			Key = "avatar",
			Clip = UiComponentClips.Circle,
			Frame = new UiFrame { Width = _avatar, Height = _avatar },
			Child = row.Avatar is { } avatar
				? new UiImage { Key = "image", Size = _avatar, Source = avatar }
				: new UiStack
				{
					Key = "initial",
					Align = UiComponentAlignments.Center,
					Justify = UiComponentJustify.Center,
					Background = SteamStyle.Placeholder,
					Children =
					[
						new UiTextRun
						{
							Key = "letter",
							Text = row.Initial,
							Size = UiLength.Capped(0.1, 12),
							Weight = UiComponentTextWeights.SemiBold,
							Color = SteamStyle.TextPrimary,
							Align = UiComponentAlignments.Center
						}
					]
				}
		};
	}

	private static UiStack Spacer()
	{
		return new UiStack { Key = "spacer", Fill = true };
	}
}
