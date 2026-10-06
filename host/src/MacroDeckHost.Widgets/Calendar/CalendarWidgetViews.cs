using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Calendar;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Widgets.Calendar;

internal static class CalendarAgendaView
{
	public const string Root = "calendarAgenda";

	private static readonly UiLength _rowText = UiLength.Capped(0.1, 12);
	private static readonly UiLength _detailText = UiLength.Capped(0.08, 9.5);
	private static readonly UiLength _headingText = UiLength.Capped(0.085, 10.5);
	private static readonly UiLength _gap = UiLength.Capped(0.025, 3);
	private static readonly UiLength _bar = UiLength.Capped(0.02, 2.5);
	private static readonly UiLength _weekdayText = UiLength.Capped(0.085, 10.5);
	private static readonly UiLength _dayNumberText = UiLength.Capped(0.24, 29);
	private static readonly UiLength _glanceTitleText = UiLength.Capped(0.09, 11);
	private static readonly UiLength _glanceTimeText = UiLength.Capped(0.075, 9);
	private static readonly UiLength _dateColumn = UiLength.Capped(0.4, 48);

	public const int MaxGlanceRows = 2;

	public static UiElement Build(
		UiState<CalendarAgendaState> state,
		CalendarAgendaSettings settings,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(settings);

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = backgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Gap = _gap,
			Children =
			[
				new UiWhen
				{
					Key = "noCalendarsGate",
					Condition = () => state.Value.Status == CalendarWidgetStatus.NoCalendars,
					Content = () => CalendarWidgetParts.Notice("noCalendars", Strings.NoCalendars(), _rowText),
				},
				new UiWhen
				{
					Key = "readyGate",
					Condition = () => state.Value.Status == CalendarWidgetStatus.Ready,
					Content = () => settings.ShowDate ? Dated(state, settings) : Plain(state),
				},
				new UiWhen
				{
					Key = "accountErrorGate",
					Condition = () => state.Value.HasAccountError,
					Content = () => CalendarWidgetParts.AccountError(_detailText, _gap),
				},
			],
		};
	}

	private static UiResponsive Plain(UiState<CalendarAgendaState> state)
		=> new()
		{
			Key = "layout",
			Fill = true,
			Default = Compact(state, stacked: true, Strings.NoEvents()),
			Variants =
			[
				new UiResponsiveVariant { MinWidth = 1.5, MinHeight = 1.5, Content = Sections(state) },
				new UiResponsiveVariant { MinWidth = 1.5, Content = Compact(state, stacked: false, Strings.NoEvents()) },
			],
		};

	private static UiResponsive Dated(UiState<CalendarAgendaState> state, CalendarAgendaSettings settings)
	{
		var empty = settings.Days == CalendarWidgetTypes.MinDays ? Strings.NoMoreEventsToday() : Strings.NoUpcomingEvents();

		return new UiResponsive
		{
			Key = "layout",
			Fill = true,
			Default = Glance(state, empty),
			Variants =
			[
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					MinHeight = 1.5,
					Content = new UiStack
					{
						Key = "dateSections",
						Direction = UiComponentDirections.Vertical,
						Gap = _gap,
						Children = [DateHeader(state, "dateHeader"), Sections(state) with { Fill = true }],
					},
				},
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					Content = new UiStack
					{
						Key = "dateCompact",
						Direction = UiComponentDirections.Horizontal,
						Gap = _gap,
						Children =
						[
							DateHeader(state, "dateColumn") with { MainSize = _dateColumn },
							Compact(state, stacked: false, empty) with { Fill = true },
						],
					},
				},
			],
		};
	}

	private static UiStack DateHeader(UiState<CalendarAgendaState> state, string key)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Gap = _gap,
			Children =
			[
				new UiTextRun
				{
					Key = "weekday",
					Text = UiText.From(() => state.Value.Weekday),
					Size = _weekdayText,
					Weight = UiComponentTextWeights.SemiBold,
					Color = CalendarWidgetParts.DateAccentColor,
					MaxLines = 1,
				},
				new UiTextRun
				{
					Key = "dayNumber",
					Text = UiText.From(() => state.Value.DayOfMonth),
					Size = _dayNumberText,
					Weight = UiComponentTextWeights.Bold,
					Role = UiComponentTextRoles.Primary,
					MaxLines = 1,
				},
			],
		};

	private static UiStack Glance(UiState<CalendarAgendaState> state, LocalizedText empty)
		=> new()
		{
			Key = "glance",
			Direction = UiComponentDirections.Vertical,
			Gap = _gap,
			Children =
			[
				DateHeader(state, "dateHeader"),
				new UiWhen
				{
					Key = "glanceEmptyGate",
					Condition = () => state.Value.Compact.Count == 0,
					Content = () => new UiTextRun
					{
						Key = "glanceEmpty",
						Text = empty,
						Size = _glanceTimeText,
						Role = UiComponentTextRoles.Secondary,
						Wrap = true,
						MaxLines = 2,
					},
				},
				new UiRepeat<CalendarAgendaRow>
				{
					Key = "glanceRows",
					Items = UiValue.From<IReadOnlyList<CalendarAgendaRow>>(() => [.. state.Value.Compact.Take(MaxGlanceRows)]),
					KeySelector = row => row.Key,
					Template = (row, _) => GlanceRow(row),
				},
			],
		};

	private static UiStack GlanceRow(CalendarAgendaRow row)
	{
		var text = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "title",
				Text = row.Title,
				Size = _glanceTitleText,
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Primary,
				MaxLines = 1,
			},
		};

		if (!row.Time.IsEmpty)
		{
			text.Add(new UiTextRun
			{
				Key = "time",
				Text = row.Time,
				Size = _glanceTimeText,
				Role = UiComponentTextRoles.Secondary,
				MaxLines = 1,
			});
		}

		return new UiStack
		{
			Key = row.Key,
			Direction = UiComponentDirections.Horizontal,
			Gap = _gap,
			Children =
			[
				CalendarWidgetParts.Bar(row.Color, _bar),
				new UiStack
				{
					Key = "text",
					Direction = UiComponentDirections.Vertical,
					Fill = true,
					Children = text,
				},
			],
		};
	}

	private static UiStack Compact(UiState<CalendarAgendaState> state, bool stacked, LocalizedText empty)
		=> new()
		{
			Key = stacked ? "compactNarrow" : "compact",
			Direction = UiComponentDirections.Vertical,
			Gap = _gap,
			Children =
			[
				new UiWhen
				{
					Key = "compactEmptyGate",
					Condition = () => state.Value.Compact.Count == 0,
					Content = () => CalendarWidgetParts.Notice("compactEmpty", empty, _rowText),
				},
				new UiRepeat<CalendarAgendaRow>
				{
					Key = "compactRows",
					Items = UiValue.From(() => state.Value.Compact),
					KeySelector = row => row.Key,
					Template = (row, _) => Row(row),
				},
			],
		};

	private static UiStack Sections(UiState<CalendarAgendaState> state)
		=> new()
		{
			Key = "sections",
			Direction = UiComponentDirections.Vertical,
			Gap = _gap,
			Children =
			[
				new UiWhen
				{
					Key = "sectionsEmptyGate",
					Condition = () => !state.Value.HasEvents,
					Content = () => CalendarWidgetParts.Notice("sectionsEmpty", Strings.NoEvents(), _rowText),
				},
				new UiWhen
				{
					Key = "daysGate",
					Condition = () => state.Value.HasEvents,
					Content = () => new UiStack
					{
						Key = "days",
						Direction = UiComponentDirections.Vertical,
						Fill = true,
						Gap = _gap,
						Children =
						[
							new UiRepeat<CalendarAgendaDay>
							{
								Key = "dayItems",
								Items = UiValue.From(() => state.Value.Days),
								KeySelector = day => day.Key,
								Template = (day, _) => Day(day),
							},
						],
					},
				},
			],
		};

	private static UiStack Day(CalendarAgendaDay day)
	{
		var children = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "heading",
				Text = day.Heading,
				Size = _headingText,
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Secondary,
				MaxLines = 1,
			},
		};

		if (day.Rows.Count == 0)
		{
			children.Add(new UiTextRun
			{
				Key = "dayEmpty",
				Text = Strings.NoEvents(),
				Size = _detailText,
				Role = UiComponentTextRoles.Muted,
				MaxLines = 1,
			});
		}
		else
		{
			children.AddRange(day.Rows.Select(row => Row(row)));
		}

		return new UiStack
		{
			Key = day.Key,
			Direction = UiComponentDirections.Vertical,
			Gap = _gap,
			Children = children,
		};
	}

	private static UiStack Row(CalendarAgendaRow row)
	{
		var text = new List<UiElement> { Title(row) };

		var details = new List<UiElement>();

		if (!row.Time.IsEmpty)
		{
			details.Add(new UiTextRun
			{
				Key = "time",
				Text = row.Time,
				Size = _detailText,
				Weight = UiComponentTextWeights.Medium,
				Role = UiComponentTextRoles.Secondary,
				MaxLines = 1,
			});
		}

		if (row.Location is { } location)
		{
			details.Add(Detail("location", location));
		}

		if (row.Calendar is { } calendar)
		{
			details.Add(Detail("calendar", calendar));
		}

		if (details.Count > 0)
		{
			text.Add(new UiStack
			{
				Key = "details",
				Direction = UiComponentDirections.Horizontal,
				Gap = _gap,
				Children = details,
			});
		}

		return new UiStack
		{
			Key = row.Key,
			Direction = UiComponentDirections.Horizontal,
			Gap = _gap,
			Children =
			[
				CalendarWidgetParts.Bar(row.Color, _bar),
				new UiStack
				{
					Key = "text",
					Direction = UiComponentDirections.Vertical,
					Fill = true,
					Children = text,
				},
			],
		};
	}

	private static UiTextRun Title(CalendarAgendaRow row)
		=> new()
		{
			Key = "title",
			Text = row.Title,
			Size = _rowText,
			Role = UiComponentTextRoles.Primary,
			MaxLines = 1,
			Fill = true,
		};

	private static UiTextRun Detail(string key, string text)
		=> new()
		{
			Key = key,
			Text = text,
			Size = _detailText,
			Role = UiComponentTextRoles.Secondary,
			MaxLines = 1,
		};
}

internal static class CalendarNextEventView
{
	public const string Root = "calendarNextEvent";

	private static readonly UiLength _titleText = UiLength.Capped(0.14, 17);
	private static readonly UiLength _narrowTitleText = UiLength.Capped(0.11, 13);
	private static readonly UiLength _whenText = UiLength.Capped(0.12, 14);
	private static readonly UiLength _detailText = UiLength.Capped(0.09, 11);
	private static readonly UiLength _gap = UiLength.Capped(0.03, 4);
	private static readonly UiLength _bar = UiLength.Capped(0.025, 3);

	public static UiElement Build(
		UiState<CalendarNextEventState> state,
		CalendarNextEventSettings settings,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(settings);

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = backgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Gap = _gap,
			Children =
			[
				new UiWhen
				{
					Key = "dateGate",
					Condition = () => settings.ShowDate && state.Value.Status == CalendarWidgetStatus.Ready,
					Content = () => new UiTextRun
					{
						Key = "dateLine",
						Text = UiText.Optional(() => state.Value.DateLine),
						Size = _detailText,
						Weight = UiComponentTextWeights.SemiBold,
						Color = CalendarWidgetParts.DateAccentColor,
						MaxLines = 1,
					},
				},
				new UiWhen
				{
					Key = "noCalendarsGate",
					Condition = () => state.Value.Status == CalendarWidgetStatus.NoCalendars,
					Content = () => CalendarWidgetParts.Notice("noCalendars", Strings.NoCalendars(), _detailText),
				},
				new UiWhen
				{
					Key = "noEventsGate",
					Condition = () => state.Value.Status == CalendarWidgetStatus.Ready && state.Value.Event is null,
					Content = () => CalendarWidgetParts.Notice("noEvents", Strings.NoUpcomingEvents(), _detailText),
				},
				new UiWhen
				{
					Key = "eventGate",
					Condition = () => state.Value.Status == CalendarWidgetStatus.Ready && state.Value.Event is not null,
					Content = () => new UiResponsive
					{
						Key = "layout",
						Default = Event(state, narrow: true),
						Variants = [new UiResponsiveVariant { MinWidth = 1.5, Content = Event(state, narrow: false) }],
					},
				},
				new UiWhen
				{
					Key = "accountErrorGate",
					Condition = () => state.Value.HasAccountError,
					Content = () => CalendarWidgetParts.AccountError(_detailText, _gap),
				},
			],
		};
	}

	private static UiStack Event(UiState<CalendarNextEventState> state, bool narrow)
		=> new()
		{
			Key = narrow ? "eventNarrow" : "event",
			Direction = UiComponentDirections.Horizontal,
			Gap = _gap,
			Children =
			[
				new UiStack
				{
					Key = "bar",
					MainSize = _bar,
					Background = UiValue.From(() => state.Value.Color ?? CalendarWidgetParts.DefaultBarColor),
				},
				new UiStack
				{
					Key = "text",
					Direction = UiComponentDirections.Vertical,
					Fill = true,
					Gap = _gap,
					Children =
					[
						new UiTextRun
						{
							Key = "title",
							Text = UiText.Optional(() => state.Value.Title),
							Size = narrow ? _narrowTitleText : _titleText,
							Weight = UiComponentTextWeights.SemiBold,
							Role = UiComponentTextRoles.Primary,
							Wrap = true,
							MaxLines = 2,
						},
						new UiTextRun
						{
							Key = "when",
							Text = UiText.Optional(() => state.Value.When),
							Size = narrow ? _detailText : _whenText,
							Weight = UiComponentTextWeights.Medium,
							MaxLines = 1,
						},
						.. narrow ? [] : Details(state),
					],
				},
			],
		};

	private static UiElement[] Details(UiState<CalendarNextEventState> state)
		=>
		[
			new UiTextRun
			{
				Key = "range",
				Text = UiText.Optional(() => state.Value.Range),
				Size = _detailText,
				Role = UiComponentTextRoles.Secondary,
				MaxLines = 1,
			},
			new UiWhen
			{
				Key = "locationGate",
				Condition = () => state.Value.Location is not null,
				Content = () => new UiTextRun
				{
					Key = "location",
					Text = UiText.From(() => state.Value.Location),
					Size = _detailText,
					Role = UiComponentTextRoles.Secondary,
					MaxLines = 1,
				},
			},
		];
}

internal static class CalendarWidgetParts
{
	public const string DefaultBarColor = "#7c8796";

	public const string DateAccentColor = "#ff3b30";

	public static UiTextRun Notice(string key, LocalizedText text, UiLength size)
		=> new()
		{
			Key = key,
			Text = text,
			Size = size,
			Role = UiComponentTextRoles.Secondary,
			Align = UiComponentAlignments.Center,
			Wrap = true,
			MaxLines = 3,
			Fill = true,
		};

	public static UiStack Bar(string? color, UiLength width)
		=> new() { Key = "bar", MainSize = width, Background = color ?? DefaultBarColor };

	public static UiStack AccountError(UiLength size, UiLength gap)
		=> new()
		{
			Key = "accountError",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = gap,
			Children =
			[
				new UiIcon
				{
					Key = "icon",
					Icon = UiIcons.AlertTriangle,
					Size = size,
					MainSize = size,
					Role = UiComponentTextRoles.Secondary,
				},
				new UiTextRun
				{
					Key = "text",
					Text = Strings.AccountError(),
					Size = size,
					Role = UiComponentTextRoles.Secondary,
					MaxLines = 2,
					Wrap = true,
					Fill = true,
				},
			],
		};
}
