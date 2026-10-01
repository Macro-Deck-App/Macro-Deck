using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.TwitchStats;

internal static class TwitchStatsWidgetView
{
	public const string Purple = "#9146ff";

	private const string _liveColor = "#eb0400";
	private const string _offlineColor = "#8b8b8b";
	private const string _offlineFace = "#5c5c66";
	private const string _onPill = "#ffffff";
	private const string _placeholderColor = "#2c2c30";

	private const string _tileTint = "#8b8b8b";
	private const double _tileOpacity = 0.14;

	public static UiElement Build(
		UiState<TwitchStatsViewState> state,
		TwitchStatsWidgetOptions? options = null,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius,
		UiResource? logo = null,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);

		var chosen = options ?? TwitchStatsWidgetOptions.Default;
		var view = new Builder(state, chosen, WidgetSafeArea.For(cornerRadius), logo);

		UiComponentContainer content = TwitchStatsStyles.Normalize(chosen.Style) switch
		{
			TwitchStatsStyles.StatsRow => view.StatsRow(),
			TwitchStatsStyles.LiveRow => view.LiveRow(),
			TwitchStatsStyles.ValueGraph => view.ValueGraph(),
			TwitchStatsStyles.Value => view.Value(),
			_ => view.Overview(),
		};

		return backgroundColor is { } background
			? new UiStack
			{
				Key = "twitchStatsBackground",
				Background = background,
				Children = [content with { Fill = true }],
			}
			: content;
	}

	internal static LocalizedString Label(string metric)
		=> metric switch
		{
			TwitchStatsMetrics.Chatters => AppStrings.Integrations.Twitch.StatsWidget.Chatters(),
			TwitchStatsMetrics.Followers => AppStrings.Integrations.Twitch.StatsWidget.Followers(),
			TwitchStatsMetrics.Subscribers => AppStrings.Integrations.Twitch.StatsWidget.Subscribers(),
			_ => AppStrings.Integrations.Twitch.StatsWidget.Viewers(),
		};

	private static string Icon(string metric)
		=> metric switch
		{
			TwitchStatsMetrics.Chatters => UiIcons.MessageSquare,
			TwitchStatsMetrics.Followers => UiIcons.Heart,
			TwitchStatsMetrics.Subscribers => UiIcons.Star,
			_ => UiIcons.User,
		};

	private static UiLength Px(double extent, double reference) => UiLength.Capped(extent / reference, extent * 1.25);

	private static UiLength Scaled(UiLength length, double factor)
		=> new() { Basis = length.Basis * factor, MaxOfCell = length.MaxOfCell * factor };

	private sealed class Builder(
		UiState<TwitchStatsViewState> state,
		TwitchStatsWidgetOptions options,
		UiSize padding,
		UiResource? logo)
	{
		private const double _tall = 240;
		private const double _short = 120;

		public UiStack Overview()
		{
			var hasBody = options.ShowThumbnail || options.Details.Count > 0;
			var hasTiles = options.Tiles.Count > 0;
			var children = new List<UiElement>
			{
				new UiStack
				{
					Key = "header",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					Gap = Px(9, _tall),
					Children =
					[
						Logo("logo", Px(28, _tall)),
						new UiTextRun
						{
							Key = "channel",
							Text = UiText.Optional(() => state.Value.ChannelName.Length > 0
								? UiText.Of(state.Value.ChannelName)
								: UiText.Of(AppStrings.Integrations.Twitch.StatsWidget.Heading())),
							Size = Px(17, _tall),
							Weight = UiComponentTextWeights.SemiBold,
							Role = UiComponentTextRoles.Primary,
						},
						Pill("pill", Px(11, _tall)),
					],
				},
			};

			if (hasBody)
			{
				children.Add(new UiStack
				{
					Key = "body",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					MainSize = hasTiles ? Px(80, _tall) : UiSize.None(),
					Fill = !hasTiles,
					Gap = Px(14, _tall),
					Children = options.ShowThumbnail ? [Thumbnail(Px(142, _tall)), Details()] : [Details()],
				});
			}

			if (hasTiles)
			{
				children.Add(new UiStack
				{
					Key = "tiles",
					Direction = UiComponentDirections.Horizontal,
					Fill = true,
					Gap = Px(8, _tall),
					Children = Tiles(_tall, 1),
				});
			}

			return new UiStack
			{
				Key = "overview",
				Direction = UiComponentDirections.Vertical,
				Padding = padding,
				Gap = Px(12, _tall),
				Children = children,
			};
		}

		public UiComponentContainer StatsRow()
		{
			var shown = options.Tiles.Count > 0 ? options.Tiles : TwitchStatsMetrics.DefaultTiles;
			var row = new UiStack
			{
				Key = "statsRow",
				Direction = UiComponentDirections.Horizontal,
				Padding = padding,
				Gap = Px(8, _short),
				Children = Tiles(_short, 1.15, shown),
			};

			if (shown.Count < 4)
			{
				return row;
			}

			// Four tiles only fit side by side in a wide box; a squarer one gets two rows of two instead.
			return new UiResponsive
			{
				Key = "statsLayout",
				Default = new UiGrid
				{
					Key = "statsGrid",
					Columns = 2,
					Padding = padding,
					Gap = Px(8, _short),
					Children = Tiles(_short, 1.15, shown),
				},
				Variants = [new UiResponsiveVariant { MinAspect = 2.4, Content = row }],
			};
		}

		public UiStack LiveRow()
			=> new()
			{
				Key = "liveRow",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Padding = padding,
				Gap = Px(12, _short),
				Children =
				[
					Logo("logo", Px(44, _short)),
					new UiStack
					{
						Key = "info",
						Direction = UiComponentDirections.Vertical,
						Justify = UiComponentJustify.Center,
						Align = UiComponentAlignments.Start,
						Fill = true,
						Gap = Px(8, _short),
						Children =
						[
							Pill("pill", Px(12, _short)),
							new UiTextRun
							{
								Key = "category",
								Text = UiText.Optional(() => state.Value.IsLive
									? UiText.Of(state.Value.Category)
									: UiText.Of(AppStrings.Integrations.Twitch.StatsWidget.OfflineHint())),
								Size = Px(14, _short),
								MinSize = Px(11, _short),
								Role = UiComponentTextRoles.Secondary,
							},
						],
					},
					new UiStack
					{
						Key = "reading",
						Direction = UiComponentDirections.Vertical,
						Justify = UiComponentJustify.Center,
						Align = UiComponentAlignments.End,
						MainSize = Px(84, _short),
						Gap = Px(5, _short),
						Children =
						[
							Number("number", Px(28, _short), UiComponentAlignments.End),
							Caption("label", Label(options.Metric), Px(12, _short), UiComponentAlignments.End, shrinks: false),
						],
					},
				],
			};

		public UiLayer ValueGraph()
		{
			var content = new UiStack
			{
				Key = "content",
				Direction = UiComponentDirections.Vertical,
				Padding = padding,
				Gap = Px(4, _tall),
				Children =
				[
					new UiStack
					{
						Key = "header",
						Direction = UiComponentDirections.Horizontal,
						Align = UiComponentAlignments.Center,
						Justify = UiComponentJustify.SpaceBetween,
						MainSize = Px(36, _tall),
						Children = [Logo("logo", Px(34, _tall)), Pill("pill", Px(13, _tall))],
					},
					new UiStack { Key = "spacer", MainSize = Px(12, _tall) },
					Number("number", Px(48, _tall), UiComponentAlignments.Start, shrinks: true),
					Caption("label", Label(options.Metric), Px(16, _tall), UiComponentAlignments.Start),
				],
			};

			return new UiLayer
			{
				Key = "valueGraph",
				Fallback = content,
				Children =
				[
					new UiWhen
					{
						Key = "chartGate",
						Condition = () => state.Value.HasChart,
						Content = () => new UiChart
						{
							Key = "chart",
							Points = UiValue.From(() => state.Value.Points),
							Color = Purple,
							PlotTop = 0.62,
							Thickness = UiSize.Capped(2d / UiLength.Cell, 2.5),
							Fallback = new UiTextRun { Key = "chartFallback", Text = UiText.Of(string.Empty) },
						},
					},
					content,
				],
			};
		}

		public UiStack Value()
			=> new()
			{
				Key = "value",
				Direction = UiComponentDirections.Vertical,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Padding = padding,
				Gap = Px(5, _short),
				Children =
				[
					Logo("logo", Px(30, _short)),
					new UiStack { Key = "spacer", MainSize = Px(2, _short) },
					new UiStack
					{
						Key = "row",
						Direction = UiComponentDirections.Horizontal,
						Align = UiComponentAlignments.Center,
						Justify = UiComponentJustify.Center,
						Gap = Px(6, _short),
						Children = [Dot("dot", Px(9, _short)), Number("number", Px(26, _short))],
					},
					Caption("label", Label(options.Metric), Px(12, _short), UiComponentAlignments.Center),
				],
			};

		private UiStack Details()
		{
			var lines = new List<UiElement>();

			foreach (var detail in options.Details)
			{
				lines.Add(detail switch
				{
					TwitchStatsDetails.Title => new UiTextRun
					{
						Key = "title",
						Text = UiText.From(() => state.Value.Title),
						Size = Px(15, _tall),
						MinSize = Px(13, _tall),
						Weight = UiComponentTextWeights.SemiBold,
						Role = UiComponentTextRoles.Primary,
						MaxLines = 2,
						Wrap = true,
					},
					TwitchStatsDetails.Category => new UiTextRun
					{
						Key = "category",
						Text = UiText.From(() => state.Value.Category),
						Size = Px(13, _tall),
						Role = UiComponentTextRoles.Secondary,
					},
					_ => new UiStack
					{
						Key = "uptime",
						Direction = UiComponentDirections.Horizontal,
						Align = UiComponentAlignments.Center,
						Gap = Px(6, _tall),
						Children =
						[
							new UiIcon
							{
								Key = "icon",
								Icon = UiIcons.ClockType,
								Size = Px(13, _tall),
								MainSize = Px(13, _tall),
								Role = UiComponentTextRoles.Muted,
							},
							new UiTextRun
							{
								Key = "text",
								Text = UiText.From(() => state.Value.Uptime),
								Size = Px(13, _tall),
								Role = UiComponentTextRoles.Muted,
							},
						],
					},
				});
			}

			return new UiStack
			{
				Key = "details",
				Direction = UiComponentDirections.Vertical,
				Justify = UiComponentJustify.Center,
				Fill = true,
				Children =
				[
					new UiWhen
					{
						Key = "live",
						Condition = () => state.Value.IsLive,
						Content = () => new UiStack
						{
							Key = "lines",
							Direction = UiComponentDirections.Vertical,
							Gap = Px(7, _tall),
							Children = lines,
						},
					},
					new UiWhen
					{
						Key = "offline",
						Condition = () => !state.Value.IsLive,
						Content = () => new UiTextRun
						{
							Key = "hint",
							Text = AppStrings.Integrations.Twitch.StatsWidget.OfflineHint(),
							Size = Px(14, _tall),
							MinSize = Px(12, _tall),
							Role = UiComponentTextRoles.Secondary,
						},
					},
				],
			};
		}

		private UiButton Thumbnail(UiLength width)
			=> new()
			{
				Key = "thumbnail",
				MainSize = width,
				Background = _placeholderColor,
				Source = UiValue.Optional(() => state.Value.Thumbnail is { } thumbnail
					? UiValue.Of(thumbnail)
					: UiValue.None<UiResource>()),
				Fit = UiComponentImageFits.Cover,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Children =
				[
					new UiWhen
					{
						Key = "placeholder",
						Condition = () => state.Value.Thumbnail is null,
						Content = () => logo is null
							? new UiStack { Key = "mark" }
							: new UiImage { Key = "mark", Source = logo, Size = Px(28, _tall), Opacity = 0.4 },
					},
				],
			};

		private List<UiElement> Tiles(double reference, double scale, IReadOnlyList<string>? metrics = null)
			=> [.. (metrics ?? options.Tiles).Select(metric => Tile(metric, reference, scale))];

		// Each run is also held to a share of the tile's own width, so a narrow tile shrinks its type rather
		// than cutting a number off.
		private static UiLength Fitted(double extent, double reference, double ofWidth)
			=> Px(extent, reference) with { MaxOfCross = ofWidth };

		private UiLayer Tile(string metric, double reference, double scale)
			=> new()
			{
				Key = metric,
				Fill = true,
				Children =
				[
					new UiModifier
					{
						Key = "face",
						Opacity = _tileOpacity,
						Child = new UiShape
						{
							Key = "shape",
							Shape = UiComponentShapes.RoundedRectangle,
							CornerRadius = Px(12, reference),
							Color = _tileTint,
						},
					},
					new UiStack
					{
						Key = "content",
						Direction = UiComponentDirections.Vertical,
						Justify = UiComponentJustify.Center,
						Align = UiComponentAlignments.Center,
						Padding = Px(8, reference),
						Gap = Px(3, reference),
						Children =
						[
							new UiIcon
							{
								Key = "icon",
								Icon = Icon(metric),
								Size = Fitted(15 * scale, reference, 0.22),
								MainSize = Fitted(15 * scale, reference, 0.22),
								Role = UiComponentTextRoles.Secondary,
							},
							new UiTextRun
							{
								Key = "value",
								Text = UiText.From(() => state.Value.Value(metric)),
								Size = Fitted(20 * scale, reference, 0.3),
								Weight = UiComponentTextWeights.Bold,
								Role = UiComponentTextRoles.Primary,
								Align = UiComponentAlignments.Center,
							},
							new UiTextRun
							{
								Key = "label",
								Text = Label(metric),
								Size = Fitted(11.5 * scale, reference, 0.145),
								Role = UiComponentTextRoles.Muted,
								Align = UiComponentAlignments.Center,
							},
						],
					},
				],
			};

		// A stack hands every child its full cross extent, so the pill sits in a row inside a column to keep
		// its own width and its own height wherever it is placed.
		private UiStack Pill(string key, UiLength textSize)
			=> new()
			{
				Key = key,
				Direction = UiComponentDirections.Horizontal,
				Children =
				[
					new UiStack
					{
						Key = "column",
						Direction = UiComponentDirections.Vertical,
						Justify = UiComponentJustify.Center,
						Children =
						[
							new UiModifier
							{
								Key = "face",
								Background = UiBackgroundValue.From(() => state.Value.IsLive ? _liveColor : _offlineFace),
								Radius = Scaled(textSize, 2),
								Child = new UiStack
								{
									Key = "content",
									Direction = UiComponentDirections.Horizontal,
									Align = UiComponentAlignments.Center,
									Padding = Scaled(textSize, 0.32),
									Gap = Scaled(textSize, 0.4),
									Children =
									[
										new UiStack { Key = "lead", MainSize = Scaled(textSize, 0.2) },
										Circle("dot", Scaled(textSize, 0.5), _onPill),
										new UiTextRun
										{
											Key = "text",
											Text = UiText.FromLocalized(() => state.Value.IsLive
												? AppStrings.Integrations.Twitch.StatsWidget.Live()
												: AppStrings.Integrations.Twitch.StatsWidget.Offline()),
											Size = textSize,
											Weight = UiComponentTextWeights.SemiBold,
											Color = _onPill,
										},
										new UiStack { Key = "trail", MainSize = Scaled(textSize, 0.3) },
									],
								},
							},
						],
					},
				],
			};

		private UiModifier Dot(string key, UiLength size)
			=> Circle(key, size, UiValue.From(() => state.Value.IsLive ? _liveColor : _offlineColor));

		// A shape takes the whole cross extent of its stack, so it is framed to keep a dot a dot.
		private static UiModifier Circle(string key, UiLength size, UiValue<string> color)
			=> new()
			{
				Key = key,
				Frame = new UiFrame { Width = size, Height = size },
				Child = new UiShape { Key = "shape", Shape = UiComponentShapes.Circle, Color = color },
			};

		private UiTextRun Number(string key, UiLength size, string align = UiComponentAlignments.Center,
			bool shrinks = false)
			=> new()
			{
				Key = key,
				Text = UiText.From(() => state.Value.Value(options.Metric)),
				Size = size,
				MinSize = shrinks ? Scaled(size, 0.7) : UiSize.None(),
				Weight = UiComponentTextWeights.Bold,
				Role = UiComponentTextRoles.Primary,
				Align = align,
			};

		private static UiTextRun Caption(string key, UiText text, UiLength size, string align, bool shrinks = true)
			=> new()
			{
				Key = key,
				Text = text,
				Size = size,
				MinSize = shrinks ? Scaled(size, 0.7) : UiSize.None(),
				Role = UiComponentTextRoles.Muted,
				Align = align,
			};

		private UiElement Logo(string key, UiLength size)
			=> logo is null
				? new UiStack { Key = key, MainSize = size }
				: new UiImage { Key = key, Source = logo, Size = size, MainSize = size };
	}
}
