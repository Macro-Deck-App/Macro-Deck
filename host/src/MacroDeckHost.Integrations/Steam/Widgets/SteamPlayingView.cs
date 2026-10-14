using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal static class SteamPlayingView
{
	public const string Root = "steamPlaying";

	private const double BackdropBrightness = 0.5;
	private const double BackdropSaturation = 1.2;

	private static readonly SteamInk _onArtwork = new(true);
	private static readonly UiLength _coverWidth = UiLength.OfBasis(0.64);

	public static UiElement Build(
		UiState<SteamPlayingState> state,
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
					Gate(state, ink, cornerRadius, "idle", SteamWidgetStatus.Idle, Strings.NotPlaying()),
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
		UiState<SteamPlayingState> state,
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

	private static UiResponsive Layout(UiState<SteamPlayingState> state, SteamInk ink, int cornerRadius)
	{
		return new UiResponsive
		{
			Key = "layout",
			Fill = true,
			Default = Hero(state, ink, cornerRadius),
			Variants = [new UiResponsiveVariant { MinWidth = 1.5, Content = Wide(state, ink, cornerRadius) }]
		};
	}

	private static UiStack Hero(UiState<SteamPlayingState> state, SteamInk ink, int cornerRadius)
	{
		return new UiStack
		{
			Key = "small",
			Children =
			[
				new UiWhen
				{
					Key = "coverGate",
					Condition = () => state.Value.Cover is not null,
					Content = () => new UiLayer
					{
						Key = "hero",
						Fill = true,
						Children =
						[
							Backdrop(state, "artwork", 1, 1),
							SteamStyle.Scrim("scrim"),
							SteamStyle.Padded("text", cornerRadius, Spacer(), Details(state, _onArtwork, 2))
						]
					}
				},
				new UiWhen
				{
					Key = "placeholderGate",
					Condition = () => state.Value.Cover is null,
					Content = () => new UiLayer
					{
						Key = "placeholder",
						Fill = true,
						Children =
						[
							SteamStyle.Padded("text",
								cornerRadius,
								SteamStyle.Badge("badge", () => state.Value.Glyph),
								Spacer(),
								Details(state, ink, 2))
						]
					}
				}
			]
		};
	}

	private static UiStack Wide(UiState<SteamPlayingState> state, SteamInk ink, int cornerRadius)
	{
		return new UiStack
		{
			Key = "wide",
			Children =
			[
				new UiWhen
				{
					Key = "coverGate",
					Condition = () => state.Value.Cover is not null,
					Content = () => new UiLayer
					{
						Key = "backdrop",
						Fill = true,
						Children =
						[
							Backdrop(state, "ambient", BackdropBrightness, BackdropSaturation),
							SteamStyle.BackdropScrim("scrim"),
							Row(state, _onArtwork, cornerRadius)
						]
					}
				},
				new UiWhen
				{
					Key = "placeholderGate",
					Condition = () => state.Value.Cover is null,
					Content = () => Row(state, ink, cornerRadius)
				}
			]
		};
	}

	private static UiStack Row(UiState<SteamPlayingState> state, SteamInk ink, int cornerRadius)
	{
		return new UiStack
		{
			Key = "row",
			Direction = UiComponentDirections.Horizontal,
			Fill = true,
			Padding = SteamSurfaces.SafeArea(cornerRadius),
			Gap = UiLength.Capped(0.08, 10),
			Children =
			[
				SteamStyle.Rounded("artworkBox",
					SteamStyle.ArtworkRadius,
					new UiStack
					{
						Key = "artwork",
						Children =
						[
							new UiWhen
							{
								Key = "imageGate",
								Condition = () => state.Value.Cover is not null,
								Content = () => new UiButton
								{
									Key = "cover",
									Fill = true,
									Background = "#000000",
									Fit = UiComponentImageFits.Cover,
									Transition = UiComponentImageTransitions.Crossfade,
									Source = Cover(state)
								}
							},
							new UiWhen
							{
								Key = "placeholderGate",
								Condition = () => state.Value.Cover is null,
								Content = () => SteamStyle.GlyphTile("placeholder",
									UiLength.OfBasis(0.26),
									() => state.Value.Glyph)
							}
						]
					},
					_coverWidth),
				new UiStack
				{
					Key = "details",
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.End,
					Fill = true,
					Children = [Details(state, ink, 3)]
				}
			]
		};
	}

	private static UiStack Details(UiState<SteamPlayingState> state, SteamInk ink, int titleLines)
	{
		return new UiStack
		{
			Key = "texts",
			Direction = UiComponentDirections.Vertical,
			Gap = SteamStyle.SmallGap,
			Children =
			[
				SteamStyle.StatusLabel("eyebrow", Strings.Eyebrow.NowPlaying(), SteamStyle.InGame),
				SteamStyle.Text("title",
					UiText.From(() => state.Value.Title),
					SteamStyle.Title,
					ink,
					weight: UiComponentTextWeights.SemiBold,
					maxLines: titleLines),
				new UiWhen
				{
					Key = "elapsedGate",
					Condition = () => state.Value.Elapsed.Length > 0,
					Content = () => SteamStyle.Text("elapsed",
						UiText.FromLocalized(() => Strings.PlayingFor(state.Value.Elapsed)),
						SteamStyle.Body,
						ink,
						SteamTone.Secondary)
				}
			]
		};
	}

	private static UiButton Backdrop(UiState<SteamPlayingState> state, string key, double brightness, double saturation)
	{
		return new UiButton
		{
			Key = key,
			Fill = true,
			Corner = UiComponentButtonCorners.Tile,
			Background = "#000000",
			Fit = UiComponentImageFits.Cover,
			Brightness = brightness,
			Saturation = saturation,
			Transition = UiComponentImageTransitions.Crossfade,
			Source = Cover(state)
		};
	}

	private static UiStack Spacer()
	{
		return new UiStack { Key = "spacer", Fill = true };
	}

	private static UiValue<UiResource> Cover(UiState<SteamPlayingState> state)
	{
		return UiValue.Optional(() => state.Value.Cover is { } cover ? UiValue.Of(cover) : UiValue.None<UiResource>());
	}

}
