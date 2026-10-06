using System.Globalization;
using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar.Config;

namespace MacroDeckHost.Widgets.Calendar;

internal static class CalendarWidgetConfigViews
{
	private const UiWidgetAppearanceFields _appearance = UiWidgetAppearanceFields.Border |
		UiWidgetAppearanceFields.BackgroundColor | UiWidgetAppearanceFields.TransparentBackground;

	public static UiElement Build(JsonElement data, ICalendarEventCache cache)
	{
		ArgumentNullException.ThrowIfNull(cache);

		var agenda = CalendarAgendaSettings.Parse(data);
		var nextEvent = CalendarNextEventSettings.Parse(data);
		var layout = new UiState<string>(CalendarWidgetData.Layout(data));
		var days = new UiState<double>(agenda.Days);
		var whenStarted = new UiState<string>(CalendarWidgetData.WhenStarted(data));
		var showDate = new UiState<bool>(agenda.ShowDate);
		var showAllDay = new UiState<bool>(CalendarWidgetData.IsNextEvent(data) ? nextEvent.ShowAllDay : agenda.ShowAllDay);
		var showTime = new UiState<bool>(agenda.ShowTime);
		var showLocation = new UiState<bool>(agenda.ShowLocation);
		var showCalendar = new UiState<bool>(agenda.ShowCalendar);
		var leadTime = new UiState<double>(CalendarWidgetData.LeadTimeMinutes(data));
		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "calendar-heading", Text = AppStrings.Integrations.Calendar.Name() },
					new UiChoiceInput
					{
						Key = CalendarWidgetTypes.LayoutKey,
						Label = Strings.Layout(),
						Binding = Bind.To(layout),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							UiOption.Of(CalendarWidgetTypes.LayoutAgenda, AppStrings.Widgets.Calendar.Layouts.Agenda()),
							UiOption.Of(CalendarWidgetTypes.LayoutNextEvent,
								AppStrings.Widgets.Calendar.Layouts.NextEvent()),
						]),
					},
					Calendars(agenda.Calendars, cache),
					new UiNumberInput
					{
						Key = CalendarWidgetTypes.DaysKey,
						Label = Strings.Days(),
						Description = Strings.DaysDescription(),
						Binding = Bind.To(days),
						Min = CalendarWidgetTypes.MinDays,
						Max = CalendarWidgetTypes.MaxDays,
						Step = 1,
						Options = UiValue.Of(DayOptions),
						VisibleWhen = For(layout, CalendarWidgetTypes.LayoutAgenda),
					},
					new UiChoiceInput
					{
						Key = CalendarWidgetTypes.WhenStartedKey,
						Label = Strings.WhenStarted(),
						Binding = Bind.To(whenStarted),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							UiOption.Of(CalendarWidgetTypes.WhenStartedNow, Strings.WhenStartedNow()),
							UiOption.Of(CalendarWidgetTypes.WhenStartedNext, Strings.WhenStartedNext()),
						]),
						VisibleWhen = For(layout, CalendarWidgetTypes.LayoutNextEvent),
					},
					LeadTime(leadTime),
					new UiHeading { Key = "display-heading", Text = AppStrings.Widgets.Editor.Display() },
					new UiBooleanInput
					{
						Key = CalendarWidgetTypes.ShowDateKey,
						Label = Strings.ShowDate(),
						Binding = Bind.To(showDate),
					},
					new UiBooleanInput
					{
						Key = CalendarWidgetTypes.ShowAllDayKey,
						Label = Strings.ShowAllDay(),
						Binding = Bind.To(showAllDay),
					},
					new UiBooleanInput
					{
						Key = CalendarWidgetTypes.ShowTimeKey,
						Label = Strings.ShowTime(),
						Binding = Bind.To(showTime),
						VisibleWhen = For(layout, CalendarWidgetTypes.LayoutAgenda),
					},
					new UiBooleanInput
					{
						Key = CalendarWidgetTypes.ShowLocationKey,
						Label = Strings.ShowLocation(),
						Binding = Bind.To(showLocation),
						VisibleWhen = For(layout, CalendarWidgetTypes.LayoutAgenda),
					},
					new UiBooleanInput
					{
						Key = CalendarWidgetTypes.ShowCalendarKey,
						Label = Strings.ShowCalendar(),
						Binding = Bind.To(showCalendar),
						VisibleWhen = For(layout, CalendarWidgetTypes.LayoutAgenda),
					},
					UiWidgetAppearance.Section(data, _appearance),
				],
			},
			Editor = FlowsEditor(flows),
		};
	}

	private static UiVisibleWhen For(UiState<string> layout, string value)
		=> new()
		{
			ParameterName = CalendarWidgetTypes.LayoutKey,
			Values = [value],
			SiblingValue = () => layout.Value,
		};

	private static readonly IReadOnlyList<UiOption> DayOptions =
	[
		.. Enumerable.Range(CalendarWidgetTypes.MinDays, CalendarWidgetTypes.MaxDays - CalendarWidgetTypes.MinDays + 1)
			.Select(days => UiOption.Of(days.ToString(CultureInfo.InvariantCulture),
				AppStrings.Widgets.Weather.ForecastDaysCount(count: days))),
	];

	private static readonly IReadOnlyList<UiOption> LeadTimeOptions =
	[
		.. CalendarWidgetTypes.LeadTimeOptions.Select(minutes => UiOption.Of(minutes.ToString(CultureInfo.InvariantCulture),
			Strings.LeadTimeMinutes(count: minutes))),
	];

	private static UiNumberInput LeadTime(UiState<double> leadTime)
		=> new()
		{
			Key = CalendarWidgetTypes.LeadTimeKey,
			Label = Strings.LeadTime(),
			Description = Strings.LeadTimeDescription(),
			Binding = Bind.To(leadTime),
			Min = CalendarWidgetTypes.MinLeadTimeMinutes,
			Max = CalendarWidgetTypes.MaxLeadTimeMinutes,
			Step = 1,
			Options = UiValue.Of(LeadTimeOptions),
		};

	private static UiWidgetEditor FlowsEditor(UiState<JsonElement> flows)
		=> new()
		{
			Key = "editor",
			Children =
			[
				new UiActionsListEditor
				{
					Key = "flows",
					Binding = Bind.To(flows),
					CanRun = true,
					Triggers = UiValue.Of(CalendarWidgetSelection.FlowTriggers),
				},
			],
		};

	private static UiMultiSelectInput Calendars(IReadOnlyList<string> selected, ICalendarEventCache cache)
	{
		var calendars = new UiState<IReadOnlyList<string>>(selected);

		return new UiMultiSelectInput
		{
			Key = CalendarWidgetTypes.CalendarsKey,
			Label = Strings.Calendars(),
			Description = cache.Snapshot.Accounts.Count == 0 ? Strings.NoCalendarsHint() : Strings.CalendarsDescription(),
			Binding = Bind.To(calendars),
			Options = UiValue.Of<IReadOnlyList<UiOption>>(CalendarOptions(cache.Snapshot)),
		};
	}

	private static List<UiOption> CalendarOptions(CalendarSnapshot snapshot)
		=>
		[
			.. snapshot.Accounts.SelectMany(state => state.Calendars.Select(calendar => new UiOption
			{
				Value = calendar.Key,
				Label = AppStrings.Widgets.Calendar.Details.Source(calendar: calendar.Name,
					account: state.Account.DisplayName,
					provider: state.Account.ProviderName),
			})),
		];
}
