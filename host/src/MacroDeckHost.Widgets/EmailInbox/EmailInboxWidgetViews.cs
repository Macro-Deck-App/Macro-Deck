using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Email;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.EmailInbox;

namespace MacroDeckHost.Widgets.EmailInbox;

internal static class EmailInboxWidgetView
{
	public const string Root = "emailInbox";

	public const string AccentColor = "#0a84ff";

	public const int MaxGlanceRows = 5;

	public const int MaxStripRows = 4;

	private const string BadgeTextColor = "#ffffff";

	private const string SeparatorColor = "#8e8e9347";

	// Every length reaches its cap on a one-cell tile, so from there on the layout is fixed in reference
	// pixels and the row thresholds below hold exactly; a smaller box only shrinks rows below them.
	private const double FullSizeBasis = 96;

	private const double Hairline = 1;

	private const int MaxBadgeCount = 999;

	private static readonly MessageStyle _list = new("list",
		EmailWidgetTypes.MaxMessages,
		15,
		false,
		10.5,
		8,
		12.5,
		11.5,
		10.5,
		10.5,
		1,
		1,
		2,
		false,
		7,
		5,
		3,
		6);

	private static readonly MessageStyle _glance = new("glance",
		MaxGlanceRows,
		11,
		true,
		9.5,
		6,
		11.5,
		11,
		9.5,
		9,
		2,
		3,
		1,
		false,
		6,
		3.5,
		3,
		5);

	private static readonly MessageStyle _strip = new("strip",
		MaxStripRows,
		13,
		false,
		9.5,
		6,
		11.5,
		11.5,
		0,
		9.5,
		1,
		1,
		0,
		true,
		6,
		4,
		0,
		4);

	private static readonly UiLength _countText = UiLength.Capped(0.33, 72);
	private static readonly UiLength _wideCountText = UiLength.Capped(0.4, 46);
	private static readonly UiLength _captionText = UiLength.Capped(0.1, 11.5);
	private static readonly UiLength _counterCaptionText = UiLength.Capped(0.1, 15);
	private static readonly UiLength _detailText = UiLength.Capped(0.09, 10.5);
	private static readonly UiLength _counterGap = UiLength.Capped(0.035, 8);
	private static readonly UiLength _wideGap = UiLength.Capped(0.08, 10);

	public static UiElement Build(
		UiState<EmailInboxWidgetState> state,
		EmailInboxWidgetSettings settings,
		UiResource icon,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(icon);

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = settings.BackgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Children =
			[
				new UiWhen
				{
					Key = "noInboxesGate",
					Condition = () => state.Value.Status == EmailInboxWidgetStatus.NoInboxes,
					Content = () => NoInboxes(icon)
				},
				new UiWhen
				{
					Key = "readyGate",
					Condition = () => state.Value.Status == EmailInboxWidgetStatus.Ready,
					Content = () => settings.IsList ? ListLayout(state, settings) : CountLayout(state, icon)
				}
			]
		};
	}

	private static UiResponsive CountLayout(UiState<EmailInboxWidgetState> state, UiResource icon)
	{
		return new UiResponsive
		{
			Key = "layout",
			Fill = true,
			Default = Counter(state, icon),
			Variants =
			[
				new UiResponsiveVariant { MinWidth = 1.5, MaxHeight = 1.5, Content = WideCounter(state, icon) }
			]
		};
	}

	private static UiResponsive ListLayout(UiState<EmailInboxWidgetState> state, EmailInboxWidgetSettings settings)
	{
		return new UiResponsive
		{
			Key = "layout",
			Fill = true,
			Default = Messages(state, settings, _glance),
			Variants =
			[
				new UiResponsiveVariant { MinWidth = 1.5, MinHeight = 1.5, Content = Messages(state, settings, _list) },
				new UiResponsiveVariant { MinWidth = 1.5, Content = Messages(state, settings, _strip) }
			]
		};
	}

	private static UiStack Counter(UiState<EmailInboxWidgetState> state, UiResource icon)
	{
		return new UiStack
		{
			Key = "counter",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = _counterGap,
			Children =
			[
				MailIcon(UiSize.Capped(0.25, 56), icon),
				Count("count", state, _countText, UiComponentAlignments.Center),
				Caption(state, _counterCaptionText, UiComponentAlignments.Center),
				ProblemGate(state, true)
			]
		};
	}

	private static UiStack WideCounter(UiState<EmailInboxWidgetState> state, UiResource icon)
	{
		return new UiStack
		{
			Key = "counterWide",
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = _wideGap,
			Children =
			[
				MailIcon(UiSize.Capped(0.42, 50), icon),
				new UiStack
				{
					Key = "text",
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.Center,
					Fill = true,
					Gap = _counterGap,
					Children =
					[
						Count("count", state, _wideCountText, UiComponentAlignments.Start),
						Caption(state, _captionText, UiComponentAlignments.Start),
						ProblemGate(state, true)
					]
				}
			]
		};
	}

	private static UiStack Messages(
		UiState<EmailInboxWidgetState> state,
		EmailInboxWidgetSettings settings,
		MessageStyle style)
	{
		var shape = RowShape.For(settings, style);

		return new UiStack
		{
			Key = style.Key,
			Direction = UiComponentDirections.Vertical,
			Gap = Px(style.HeaderGap),
			Children =
			[
				Header(state, style),
				new UiLayer
				{
					Key = "rows",
					Fill = true,
					Children =
					[
						EmptyGate(state, style),
						new UiRepeat<PlacedRow>
						{
							Key = "rowItems",
							Items = UiValue.From(() => PlacedRows(state.Value, style)),
							KeySelector = placed => placed.Row.Key,
							Template = (placed, _) => Placed(placed, style, shape)
						}
					]
				},
				.. ReferenceEquals(style, _list) ? [ProblemGate(state, false)] : Array.Empty<UiElement>()
			]
		};
	}

	private static UiStack Header(UiState<EmailInboxWidgetState> state, MessageStyle style)
	{
		return new UiStack
		{
			Key = "header",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			MainSize = Px(Math.Max(style.Title, BadgeHeight(style))),
			Gap = Px(style.DotGap),
			Children =
			[
				new UiTextRun
				{
					Key = "title",
					Text = UiText.Optional(() => state.Value.Title),
					Size = Px(style.Title),
					Weight = UiComponentTextWeights.Bold,
					Role = UiComponentTextRoles.Primary,
					Color = style.AccentTitle ? UiValue.Of(AccentColor) : UiValue.None<string>(),
					MaxLines = 1,
					Fill = true
				},
				.. ReferenceEquals(style, _list)
					? Array.Empty<UiElement>()
					:
					[
						new UiWhen
						{
							Key = "problemIconGate",
							Condition = () => state.Value.HasProblem,
							Content = () => new UiIcon
							{
								Key = "problemIcon",
								Icon = UiIcons.AlertTriangle,
								Size = Px(style.Badge * 1.2),
								MainSize = Px(style.Badge * 1.2),
								Role = UiComponentTextRoles.Secondary
							}
						}
					],
				new UiWhen
				{
					Key = "badgeGate",
					Condition = () => state.Value.HasUnread,
					Content = () => Badge(state, style)
				}
			]
		};
	}

	private static UiModifier Badge(UiState<EmailInboxWidgetState> state, MessageStyle style)
	{
		return new UiModifier
		{
			Key = "badgeFace",
			Radius = Px(BadgeHeight(style) / 2),
			AccessibilityLabel = UiText.Optional(() => state.Value.Unread),
			Child = new UiStack
			{
				Key = "badge",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Background = AccentColor,
				MainSize = UiSize.From(() => Px(BadgeWidth(style, BadgeCharacters(state.Value.UnreadCount)))),
				Children =
				[
					new UiTextRun
					{
						Key = "unread",
						Text = UiText.Optional(() => state.Value.Count),
						Size = Px(style.Badge),
						Weight = UiComponentTextWeights.SemiBold,
						Color = BadgeTextColor,
						Align = UiComponentAlignments.Center,
						MaxLines = 1,
						Shadow = false
					}
				]
			}
		};
	}

	private static IReadOnlyList<PlacedRow> PlacedRows(EmailInboxWidgetState state, MessageStyle style)
	{
		var rows = state.Rows.Take(style.MaxRows).ToList();
		var firstUnread = rows.FindIndex(row => row.IsUnread);

		return [.. rows.Select((row, index) => new PlacedRow(row, index, firstUnread))];
	}

	// The unread dot column exists only while an unread row is on screen, and which rows are on screen
	// depends on the box height: below the first unread row's fit the rows carry no column at all.
	private static UiStack Placed(PlacedRow placed, MessageStyle style, RowShape shape)
	{
		var markedLater = placed.FirstUnread > placed.Index;
		var marked = placed.FirstUnread >= 0 && !markedLater;
		var markedFrom = shape.Needs(placed.FirstUnread) / UiLength.Cell;

		if (placed.Index == 0)
		{
			return new UiStack
			{
				Key = placed.Row.Key,
				Direction = UiComponentDirections.Vertical,
				Children = markedLater
					?
					[
						new UiResponsive
						{
							Key = "mark",
							Fill = true,
							Default = First(placed, style, shape, "placed", false),
							Variants =
							[
								new UiResponsiveVariant
								{
									MinHeight = markedFrom,
									Content = First(placed, style, shape, "marked", true)
								}
							]
						}
					]
					: [Row(placed.Row, style, shape, marked)]
			};
		}

		// The responsive sits in a stack: a layer pins only its stack children to the top left.
		return new UiStack
		{
			Key = placed.Row.Key,
			Direction = UiComponentDirections.Vertical,
			Children =
			[
				new UiResponsive
				{
					Key = "fit",
					Fill = true,
					Default = new UiStack { Key = "hidden" },
					Variants =
					[
						.. markedLater
							?
							[
								new UiResponsiveVariant
								{
									MinHeight = markedFrom,
									Content = Following(placed, style, shape, "marked", true)
								}
							]
							: Array.Empty<UiResponsiveVariant>(),
						new UiResponsiveVariant
						{
							MinHeight = shape.Needs(placed.Index) / UiLength.Cell,
							Content = Following(placed, style, shape, "placed", marked)
						}
					]
				}
			]
		};
	}

	private static UiStack First(PlacedRow placed, MessageStyle style, RowShape shape, string key, bool marked)
	{
		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Children = [Row(placed.Row, style, shape, marked)]
		};
	}

	private static UiStack Following(PlacedRow placed, MessageStyle style, RowShape shape, string key, bool marked)
	{
		var separator = 2 * style.SeparatorGap + Hairline;

		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Children =
			[
				new UiStack
				{
					Key = "offset",
					MainSize = Px(placed.Index * shape.Pitch - separator)
				},
				Separator(style, marked),
				Row(placed.Row, style, shape, marked)
			]
		};
	}

	private static UiStack Separator(MessageStyle style, bool marked)
	{
		return new UiStack
		{
			Key = "separator",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			MainSize = Px(2 * style.SeparatorGap + Hairline),
			Children =
			[
				new UiStack
				{
					Key = "rule",
					Direction = UiComponentDirections.Horizontal,
					MainSize = Px(Hairline),
					Children =
					[
						.. marked
							? [new UiStack { Key = "indent", MainSize = Px(style.DotColumn + style.DotGap) }]
							: Array.Empty<UiElement>(),
						new UiModifier
						{
							Key = "lineFace",
							Background = SeparatorColor,
							Child = new UiStack { Key = "line", Fill = true }
						}
					]
				}
			]
		};
	}

	private static UiStack Row(EmailInboxRow row, MessageStyle style, RowShape shape, bool marked)
	{
		IReadOnlyList<UiElement> lead = marked
			?
			[
				new UiStack
				{
					Key = "marker",
					Direction = UiComponentDirections.Vertical,
					MainSize = Px(style.DotColumn),
					Children = row.IsUnread
						?
						[
							new UiShape
							{
								Key = "dot",
								Shape = UiComponentShapes.Circle,
								Color = AccentColor,
								MainSize = Px(shape.Lead)
							}
						]
						: []
				}
			]
			: [];

		return new UiStack
		{
			Key = "row",
			Direction = UiComponentDirections.Horizontal,
			Align = style.SingleLine ? UiValue.Of(UiComponentAlignments.End) : UiValue.None<string>(),
			MainSize = Px(shape.Height),
			Gap = Px(style.DotGap),
			Children = style.SingleLine
				? [.. lead, .. SingleLine(row, style)]
				: [.. lead, Lines(row, style, shape)]
		};
	}

	private static IEnumerable<UiElement> SingleLine(EmailInboxRow row, MessageStyle style)
	{
		if (!row.Title.IsEmpty)
		{
			yield return Title(row, style);
		}

		if (!row.Subtitle.IsEmpty)
		{
			yield return new UiTextRun
			{
				Key = "subject",
				Text = row.Subtitle,
				Size = Px(style.Subject),
				Role = UiComponentTextRoles.Secondary,
				MaxLines = 1,
				Fill = true
			};
		}

		if (!row.Time.IsEmpty)
		{
			yield return Time(row, style);
		}
	}

	private static UiStack Lines(EmailInboxRow row, MessageStyle style, RowShape shape)
	{
		var lines = new List<UiElement>();

		if (shape.FirstLine > 0)
		{
			var firstLine = new List<UiElement>();

			if (!row.Title.IsEmpty)
			{
				firstLine.Add(Title(row, style));
			}
			else
			{
				firstLine.Add(new UiStack { Key = "spacer", Fill = true });
			}

			if (!row.Time.IsEmpty)
			{
				firstLine.Add(Time(row, style));
			}

			lines.Add(new UiStack
			{
				Key = "firstLine",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.End,
				MainSize = Px(shape.FirstLine),
				Gap = Px(style.DotGap),
				Children = firstLine
			});
		}

		if (!row.Subtitle.IsEmpty)
		{
			lines.Add(new UiTextRun
			{
				Key = "subject",
				Text = row.Subtitle,
				Size = Px(style.Subject),
				Role = UiComponentTextRoles.Primary,
				MaxLines = shape.SubjectLines,
				Wrap = shape.SubjectLines > 1
			});
		}

		if (row.Preview is { } preview)
		{
			lines.Add(new UiTextRun
			{
				Key = "preview",
				Text = preview,
				Size = Px(style.Preview),
				Role = UiComponentTextRoles.Secondary,
				MaxLines = style.PreviewLines,
				Wrap = style.PreviewLines > 1
			});
		}

		return new UiStack
		{
			Key = "text",
			Direction = UiComponentDirections.Vertical,
			Fill = true,
			Gap = Px(style.LineGap),
			Children = lines
		};
	}

	private static UiTextRun Title(EmailInboxRow row, MessageStyle style)
	{
		return new UiTextRun
		{
			Key = "title",
			Text = row.Title,
			Size = Px(style.Sender),
			Weight = row.IsUnread ? UiComponentTextWeights.Bold : UiComponentTextWeights.Regular,
			Role = UiComponentTextRoles.Primary,
			MaxLines = 1,
			Fill = true
		};
	}

	// The reserved digits are the time's floor: the sender beside it gives way, the time never ellipsizes.
	private static UiTextRun Time(EmailInboxRow row, MessageStyle style)
	{
		return new UiTextRun
		{
			Key = "time",
			Text = row.Time.Text,
			Size = Px(style.Time),
			Role = UiComponentTextRoles.Secondary,
			Align = UiComponentAlignments.End,
			MaxLines = 1,
			Digits = row.Time.Digits
		};
	}

	private static UiImage MailIcon(UiSize size, UiResource icon)
	{
		return new UiImage { Key = "icon", Source = icon, Size = size, Tint = AccentColor };
	}

	private static UiTextRun Count(string key, UiState<EmailInboxWidgetState> state, UiLength size, string align)
	{
		return new UiTextRun
		{
			Key = key,
			Text = UiText.Optional(() => state.Value.Count),
			Size = size,
			Weight = UiComponentTextWeights.Bold,
			Role = UiComponentTextRoles.Primary,
			Align = align,
			MaxLines = 1
		};
	}

	private static UiTextRun Caption(UiState<EmailInboxWidgetState> state, UiLength size, string align)
	{
		return new UiTextRun
		{
			Key = "caption",
			Text = UiText.Optional(() => state.Value.Title),
			Size = size,
			Weight = UiComponentTextWeights.Medium,
			Role = UiComponentTextRoles.Secondary,
			Align = align,
			MaxLines = 1
		};
	}

	private static UiWhen EmptyGate(UiState<EmailInboxWidgetState> state, MessageStyle style)
	{
		return new UiWhen
		{
			Key = "emptyGate",
			Condition = () => state.Value.ShowsEmpty,
			Content = () => new UiStack
			{
				Key = "emptyBox",
				Direction = UiComponentDirections.Vertical,
				Justify = UiComponentJustify.Center,
				Children =
				[
					new UiTextRun
					{
						Key = "empty",
						Text = UiText.Optional(() => state.Value.EmptyText),
						Size = Px(style.Subject),
						Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center,
						Wrap = true,
						MaxLines = 2
					}
				]
			}
		};
	}

	private static UiWhen ProblemGate(UiState<EmailInboxWidgetState> state, bool offline)
	{
		return new UiWhen
		{
			Key = "problemGate",
			Condition = () => state.Value.HasProblem,
			Content = () => new UiStack
			{
				Key = "problem",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Justify = offline ? UiValue.Of(UiComponentJustify.Center) : UiValue.None<string>(),
				Gap = Px(4),
				Children =
				[
					new UiIcon
					{
						Key = "icon",
						Icon = UiIcons.AlertTriangle,
						Size = _detailText,
						MainSize = _detailText,
						Role = UiComponentTextRoles.Secondary
					},
					new UiTextRun
					{
						Key = "text",
						Text = offline ? (UiText)Strings.Offline() : UiText.Optional(() => state.Value.Problem),
						Size = _detailText,
						Role = UiComponentTextRoles.Secondary,
						MaxLines = offline ? 1 : 2,
						Wrap = !offline,
						Fill = !offline
					}
				]
			}
		};
	}

	private static UiStack NoInboxes(UiResource icon)
	{
		return new UiStack
		{
			Key = "noInboxes",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Fill = true,
			Gap = _counterGap,
			Children =
			[
				new UiImage { Key = "icon", Source = icon, Size = UiSize.Capped(0.24, 40) },
				new UiTextRun
				{
					Key = "text",
					Text = Strings.NoInboxes(),
					Size = _counterCaptionText,
					Weight = UiComponentTextWeights.Medium,
					Role = UiComponentTextRoles.Secondary,
					Align = UiComponentAlignments.Center,
					Wrap = true,
					MaxLines = 3
				}
			]
		};
	}

	private static UiLength Px(double px)
	{
		return UiLength.Capped(px / FullSizeBasis, px);
	}

	private static double BadgeHeight(MessageStyle style)
	{
		return style.Badge * 1.55;
	}

	private static double BadgeWidth(MessageStyle style, int characters)
	{
		return Math.Max(BadgeHeight(style), style.Badge * 0.62 * Math.Max(1, characters) + style.Badge);
	}

	private static int BadgeCharacters(int unread)
	{
		return unread > MaxBadgeCount
			? MaxBadgeCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 1
			: Math.Max(1, unread).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
	}

	private sealed record PlacedRow(EmailInboxRow Row, int Index, int FirstUnread);

	private sealed record MessageStyle(
		string Key,
		int MaxRows,
		double Title,
		bool AccentTitle,
		double Badge,
		double HeaderGap,
		double Sender,
		double Subject,
		double Preview,
		double Time,
		int SubjectLines,
		int SubjectLinesAlone,
		int PreviewLines,
		bool SingleLine,
		double DotColumn,
		double DotGap,
		double LineGap,
		double SeparatorGap);

	private sealed record RowShape(double FirstLine, double Lead, double Height, double Pitch, int SubjectLines)
	{
		private const double FitSlack = 0.5;

		public static RowShape For(EmailInboxWidgetSettings settings, MessageStyle style)
		{
			var hasTitle = settings.ShowSender || settings.ShowSubject;
			var firstLine = Math.Max(hasTitle ? style.Sender : 0, settings.ShowTimestamp ? style.Time : 0);

			if (style.SingleLine)
			{
				var line = Math.Max(firstLine, style.Subject);
				return new RowShape(line, line, line, line + 2 * style.SeparatorGap + Hairline, 1);
			}

			var parts = new List<double>();

			if (firstLine > 0)
			{
				parts.Add(firstLine);
			}

			if (settings is { ShowSender: true, ShowSubject: true })
			{
				parts.Add(style.Subject * LinesOfSubject(settings, style));
			}

			if (settings.ShowPreview)
			{
				parts.Add(style.Preview * style.PreviewLines);
			}

			var height = parts.Sum() + Math.Max(0, parts.Count - 1) * style.LineGap;
			var lead = firstLine > 0 ? firstLine : style.Preview;

			var rowHeight = Math.Max(height, lead);

			return new RowShape(firstLine,
				lead,
				rowHeight,
				rowHeight + 2 * style.SeparatorGap + Hairline,
				LinesOfSubject(settings, style));
		}

		public static int LinesOfSubject(EmailInboxWidgetSettings settings, MessageStyle style)
		{
			return settings.ShowPreview ? style.SubjectLines : style.SubjectLinesAlone;
		}

		public double Needs(int index)
		{
			return index * Pitch + Height - FitSlack;
		}
	}
}
