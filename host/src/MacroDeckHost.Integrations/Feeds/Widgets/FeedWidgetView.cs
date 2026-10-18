using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;

namespace MacroDeckHost.Integrations.Feeds.Widgets;

internal static class FeedWidgetView
{
	public const string Root = "feeds";

	private const string ArtworkInk = "#ffffff";
	private const string ArtworkSecondaryInk = "#ffffffbf";
	private const string ArtworkBackdrop = "#000000";
	private const double ArtworkBrightness = 0.85;
	private const double TallTile = 1.5;

	private static readonly UiLength _headingText = UiLength.Capped(0.095, 11.5);
	private static readonly UiLength _titleText = UiLength.Capped(0.09, 11);
	private static readonly UiLength _featuredTitleText = UiLength.Capped(0.11, 13);
	private static readonly UiLength _text = UiLength.Capped(0.08, 9.5);
	private static readonly UiLength _smallText = UiLength.Capped(0.07, 8.5);
	private static readonly UiLength _icon = UiLength.Capped(0.1, 12);
	private static readonly UiLength _thumbnail = UiLength.Capped(0.2, 24);
	private static readonly UiLength _gap = UiLength.Capped(0.035, 4);

	// The header sits on the artwork too, so the scrim darkens the top edge as well as the bottom.
	private static readonly UiGradient _scrim = UiGradient.Linear(180,
		new UiGradientStop { Offset = 0, Color = "#00000099" },
		new UiGradientStop { Offset = 0.3, Color = "#00000014" },
		new UiGradientStop { Offset = 0.5, Color = "#00000059" },
		new UiGradientStop { Offset = 1, Color = "#000000e0" });

	public static UiElement Build(
		FeedWidgetKind kind,
		UiState<FeedWidgetState> state,
		UiState<int> index,
		UiState<UiResource?> icon,
		FeedWidgetOptions options,
		Func<string, UiEventOutcome> press,
		int cornerRadius = UiWidgetSafeArea.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(index);
		ArgumentNullException.ThrowIfNull(icon);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(press);

		var builder = new Builder(state, index, icon, options, press, cornerRadius);
		var background = backgroundColor is { } color ? UiValue.Of(color) : UiValue.None<string>();
		if (kind == FeedWidgetKind.List)
		{
			return builder.Plain(Root, background, builder.Rows);
		}

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Background = background,
			Children =
			[
				new UiWhen
				{
					Key = "artworkGate",
					Condition = builder.HasArtwork,
					Content = builder.Artwork
				},
				new UiWhen
				{
					Key = "plainGate",
					Condition = () => !builder.HasArtwork(),
					Content = builder.PlainFeatured
				}
			]
		};
	}

	public static FeedRow? Current(FeedWidgetState state, int index)
	{
		ArgumentNullException.ThrowIfNull(state);

		var count = state.Rows.Count;
		return count == 0 ? null : state.Rows[(index % count + count) % count];
	}

	private static UiValue<UiResource> Image(UiResource? resource)
	{
		return resource is null ? UiValue.None<UiResource>() : resource;
	}

	private sealed class Builder(
		UiState<FeedWidgetState> state,
		UiState<int> index,
		UiState<UiResource?> icon,
		FeedWidgetOptions options,
		Func<string, UiEventOutcome> press,
		int cornerRadius)
	{
		public bool HasArtwork()
		{
			return options.ShowThumbnail
				&& state.Value.Status == FeedWidgetStatus.Ok
				&& Current(state.Value, index.Value)?.Image is not null;
		}

		public UiStack Plain(string key, UiValue<string> background, Func<UiElement> content)
		{
			return new UiStack
			{
				Key = key,
				Direction = UiComponentDirections.Vertical,
				Padding = UiWidgetSafeArea.For(cornerRadius),
				Background = background,
				Gap = _gap,
				Children =
				[
					Header(false),
					new UiWhen
					{
						Key = "messageGate",
						Condition = () => state.Value.Status != FeedWidgetStatus.Ok,
						Content = Message
					},
					new UiWhen
					{
						Key = "contentGate",
						Condition = () => state.Value.Status == FeedWidgetStatus.Ok,
						Content = content
					}
				]
			};
		}

		public UiStack PlainFeatured()
		{
			return Plain("plain", UiValue.None<string>(), Featured) with { Fill = true };
		}

		public UiStack Artwork()
		{
			return new UiStack
			{
				Key = "article",
				Fill = true,
				Events = [UiEventHandler.On(UiComponentEvents.Press, _ => PressCurrent())],
				Children =
				[
					new UiLayer
					{
						Key = "artwork",
						Fill = true,
						Children =
						[
							new UiButton
							{
								Key = "image",
								Fill = true,
								Corner = UiComponentButtonCorners.Tile,
								Background = ArtworkBackdrop,
								Fit = UiComponentImageFits.Cover,
								Brightness = ArtworkBrightness,
								Transition = UiComponentImageTransitions.Crossfade,
								Source = UiValue.Optional(() => Image(Current(state.Value, index.Value)?.Image))
							},
							new UiModifier
							{
								Key = "scrim",
								Background = _scrim,
								Child = new UiStack { Key = "fill", Fill = true }
							},
							new UiResponsive
							{
								Key = "overlay",
								Fill = true,
								Default = Overlay("short", false),
								Variants =
								[
									new UiResponsiveVariant { MinHeight = TallTile, Content = Overlay("tall", true) }
								]
							}
						]
					}
				]
			};
		}

		private UiStack Overlay(string key, bool description)
		{
			var children = new List<UiElement>
			{
				Header(true),
				new UiStack { Key = "spacer", Fill = true },
				FeaturedTitle(true),
				FeaturedMeta(true)
			};
			if (description)
			{
				children.Add(DescriptionGate(FeaturedDescription(true)));
			}

			return new UiStack
			{
				Key = key,
				Direction = UiComponentDirections.Vertical,
				Padding = UiWidgetSafeArea.For(cornerRadius),
				Gap = _gap,
				Children = children
			};
		}

		private UiStack Header(bool onArtwork)
		{
			return new UiStack
			{
				Key = "header",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Gap = _gap,
				Children =
				[
					new UiWhen
					{
						Key = "iconGate",
						Condition = () => icon.Value is not null,
						Content = () => new UiImage
						{
							Key = "icon",
							Source = UiValue.Optional(() => Image(icon.Value)),
							Size = _icon,
							MainSize = _icon
						}
					},
					new UiTextRun
					{
						Key = "heading",
						Text = UiText.Optional(() => state.Value.Heading),
						Size = _headingText,
						Weight = UiComponentTextWeights.SemiBold,
						Role = UiComponentTextRoles.Primary,
						Color = Ink(onArtwork, ArtworkInk),
						MaxLines = 1,
						Fill = true
					}
				]
			};
		}

		public UiStack Message()
		{
			return new UiStack
			{
				Key = "message",
				Direction = UiComponentDirections.Vertical,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Fill = true,
				Children =
				[
					new UiTextRun
					{
						Key = "text",
						Text = UiText.Optional(() => state.Value.Message),
						Size = _text,
						Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center,
						MaxLines = 4
					}
				]
			};
		}

		public UiStack Rows()
		{
			return new UiStack
			{
				Key = "rows",
				Direction = UiComponentDirections.Vertical,
				Gap = _gap,
				Fill = true,
				Children =
				[
					new UiRepeat<FeedRow>
					{
						Key = "items",
						Items = UiValue.From(() => state.Value.Rows),
						KeySelector = row => row.Id,
						Template = (row, _) => Row(row.Id)
					}
				]
			};
		}

		public UiStack Featured()
		{
			return new UiStack
			{
				Key = "article",
				Direction = UiComponentDirections.Vertical,
				Gap = _gap,
				Fill = true,
				Events = [UiEventHandler.On(UiComponentEvents.Press, _ => PressCurrent())],
				Children =
				[
					FeaturedTitle(false),
					FeaturedMeta(false),
					DescriptionGate(FeaturedDescription(false) with { MaxLines = 3 })
				]
			};
		}

		private UiEventOutcome PressCurrent()
		{
			return Current(state.Value, index.Value) is { } row
				? press(row.Id)
				: UiEventOutcome.Rejected("No article is shown.");
		}

		private UiTextRun FeaturedTitle(bool onArtwork)
		{
			return new UiTextRun
			{
				Key = "title",
				Text = UiText.From(() => Current(state.Value, index.Value)?.Title),
				Size = _featuredTitleText,
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Primary,
				Color = Ink(onArtwork, ArtworkInk),
				MaxLines = 2,
				Wrap = true
			};
		}

		private UiTextRun FeaturedMeta(bool onArtwork)
		{
			return new UiTextRun
			{
				Key = "meta",
				Text = UiText.From(() => Current(state.Value, index.Value)?.Meta),
				Size = _smallText,
				Role = UiComponentTextRoles.Muted,
				Color = Ink(onArtwork, ArtworkSecondaryInk),
				MaxLines = 1
			};
		}

		private UiTextRun FeaturedDescription(bool onArtwork)
		{
			return new UiTextRun
			{
				Key = "description",
				Text = UiText.From(() => Current(state.Value, index.Value)?.Description),
				Size = _text,
				Role = UiComponentTextRoles.Secondary,
				Color = Ink(onArtwork, ArtworkSecondaryInk),
				MaxLines = 2,
				Wrap = true
			};
		}

		private UiWhen DescriptionGate(UiTextRun description)
		{
			return new UiWhen
			{
				Key = "descriptionGate",
				Condition = () => options.ShowDescription && Current(state.Value, index.Value)?.Description is not null,
				Content = () => description
			};
		}

		private static UiValue<string> Ink(bool onArtwork, string color)
		{
			return onArtwork ? UiValue.Of(color) : UiValue.None<string>();
		}

		private FeedRow? Find(string id)
		{
			return state.Value.Rows.FirstOrDefault(row => row.Id == id);
		}

		private UiStack Row(string id)
		{
			return new UiStack
			{
				Key = id,
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Gap = _gap,
				Events = [UiEventHandler.On(UiComponentEvents.Press, _ => press(id))],
				Children =
				[
					new UiWhen
					{
						Key = "thumbnailGate",
						Condition = () => options.ShowThumbnail && Find(id)?.Image is not null,
						Content = () => new UiImage
						{
							Key = "thumbnail",
							Source = UiValue.Optional(() => Image(Find(id)?.Image)),
							Size = _thumbnail,
							MainSize = _thumbnail
						}
					},
					new UiStack
					{
						Key = "texts",
						Direction = UiComponentDirections.Vertical,
						Fill = true,
						Children =
						[
							new UiTextRun
							{
								Key = "title",
								Text = UiText.From(() => Find(id)?.Title),
								Size = _titleText,
								Weight = UiValue.From(() => Find(id)?.Unread == true
									? UiComponentTextWeights.SemiBold
									: UiComponentTextWeights.Regular),
								Role = UiComponentTextRoles.Primary,
								MaxLines = options.ShowDescription ? 1 : 2,
								Wrap = true
							},
							new UiTextRun
							{
								Key = "meta",
								Text = UiText.From(() => Find(id)?.Meta),
								Size = _smallText,
								Role = UiComponentTextRoles.Muted,
								MaxLines = 1
							},
							new UiWhen
							{
								Key = "descriptionGate",
								Condition = () => options.ShowDescription && Find(id)?.Description is not null,
								Content = () => new UiTextRun
								{
									Key = "description",
									Text = UiText.From(() => Find(id)?.Description),
									Size = _smallText,
									Role = UiComponentTextRoles.Secondary,
									MaxLines = 1
								}
							}
						]
					}
				]
			};
		}
	}
}
