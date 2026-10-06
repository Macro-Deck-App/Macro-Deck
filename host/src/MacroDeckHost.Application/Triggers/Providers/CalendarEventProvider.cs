using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Triggers.Providers;

public sealed class CalendarEventProvider : IHostEventProvider
{
	public const string ProviderIdValue = "calendar";

	public const string StartsSoonEventId = "starts-soon";
	public const string StartedEventId = "started";
	public const string EndedEventId = "ended";

	public const string AccountIdParameter = "accountId";
	public const string CalendarIdParameter = "calendarId";
	public const string LeadTimeParameter = "leadTime";

	public static readonly TimeSpan DefaultLeadTime = TimeSpan.FromMinutes(15);
	public static readonly TimeSpan MaximumLeadTime = TimeSpan.FromDays(1);

	public string ProviderId => ProviderIdValue;

	public LocalizedText ProviderName => AppStrings.Events.Calendar.ProviderName();

	public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
	[
		new()
		{
			Id = StartsSoonEventId,
			Name = AppStrings.Events.Calendar.StartsSoonName(),
			Description = AppStrings.Events.Calendar.StartsSoonDescription(),
			Category = AppStrings.Events.Calendar.Category(),
			ConfigurationParameters =
			[
				ActionParameter.Duration(LeadTimeParameter,
					label: AppStrings.Events.Calendar.LeadTimeLabel(),
					description: AppStrings.Events.Calendar.LeadTimeDescription(),
					min: 0,
					max: MaximumLeadTime.TotalMilliseconds,
					defaultMilliseconds: DefaultLeadTime.TotalMilliseconds),
				AccountFilter(),
				CalendarFilter()
			],
			PayloadParameters = Payload()
		},
		new()
		{
			Id = StartedEventId,
			Name = AppStrings.Events.Calendar.StartedName(),
			Description = AppStrings.Events.Calendar.StartedDescription(),
			Category = AppStrings.Events.Calendar.Category(),
			ConfigurationParameters = [AccountFilter(), CalendarFilter()],
			PayloadParameters = Payload()
		},
		new()
		{
			Id = EndedEventId,
			Name = AppStrings.Events.Calendar.EndedName(),
			Description = AppStrings.Events.Calendar.EndedDescription(),
			Category = AppStrings.Events.Calendar.Category(),
			ConfigurationParameters = [AccountFilter(), CalendarFilter()],
			PayloadParameters = Payload()
		}
	];

	public static string Qualify(string eventId)
		=> QualifiedId.Create(ProviderIdValue, eventId, OwnerIdKind.HostProvider, LocalIdKind.Declared, "Event")
			.ToString();

	public static TimeSpan LeadTime(EventSubscription subscription)
	{
		if (!subscription.Configuration.TryGetValue(LeadTimeParameter, out var configured))
		{
			return DefaultLeadTime;
		}

		var element = configured.Value;
		double? milliseconds = element.ValueKind switch
		{
			JsonValueKind.Number => element.GetDouble(),
			JsonValueKind.String when double.TryParse(element.GetString(),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var parsed) => parsed,
			_ => null
		};

		if (milliseconds is null || double.IsNaN(milliseconds.Value))
		{
			return DefaultLeadTime;
		}

		return TimeSpan.FromMilliseconds(Math.Clamp(milliseconds.Value, 0, MaximumLeadTime.TotalMilliseconds));
	}

	public static IReadOnlyDictionary<string, object?> PayloadOf(CalendarEventSummary calendarEvent, string providerName)
		=> new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["eventId"] = calendarEvent.EventId,
			["title"] = calendarEvent.Title,
			["start"] = calendarEvent.Start.ToString("O", CultureInfo.InvariantCulture),
			["end"] = calendarEvent.End.ToString("O", CultureInfo.InvariantCulture),
			["allDay"] = calendarEvent.IsAllDay,
			["calendar"] = calendarEvent.CalendarName,
			[CalendarIdParameter] = calendarEvent.CalendarKey,
			["account"] = calendarEvent.AccountName,
			[AccountIdParameter] = calendarEvent.AccountId,
			["provider"] = providerName,
			["location"] = calendarEvent.Location ?? string.Empty,
			["meetingUrl"] = calendarEvent.MeetingUrl ?? string.Empty
		};

	private static IReadOnlyList<ActionParameter> Payload() =>
	[
		ActionParameter.Text("eventId", label: AppStrings.Events.Calendar.EventIdLabel()),
		ActionParameter.Text("title", label: AppStrings.Events.Calendar.TitleLabel()),
		ActionParameter.Text("start", label: AppStrings.Events.Calendar.StartLabel()),
		ActionParameter.Text("end", label: AppStrings.Events.Calendar.EndLabel()),
		ActionParameter.Toggle("allDay", label: AppStrings.Events.Calendar.AllDayLabel()),
		ActionParameter.Text("calendar", label: AppStrings.Events.Calendar.CalendarNameLabel()),
		ActionParameter.DynamicChoice(CalendarIdParameter,
			label: AppStrings.Events.Calendar.CalendarLabel(),
			optionsSourceId: CalendarOptionsSourceIds.Calendars),
		ActionParameter.Text("account", label: AppStrings.Events.Calendar.AccountNameLabel()),
		ActionParameter.DynamicChoice(AccountIdParameter,
			label: AppStrings.Events.Calendar.AccountLabel(),
			optionsSourceId: CalendarOptionsSourceIds.Accounts),
		ActionParameter.Text("provider", label: AppStrings.Events.Calendar.ProviderLabel()),
		ActionParameter.Text("location", label: AppStrings.Events.Calendar.LocationLabel()),
		ActionParameter.Text("meetingUrl", label: AppStrings.Events.Calendar.MeetingUrlLabel())
	];

	private static ActionParameter AccountFilter()
		=> ActionParameter.DynamicChoice(AccountIdParameter,
			label: AppStrings.Events.Calendar.AccountLabel(),
			description: AppStrings.Events.Calendar.AccountFilterDescription(),
			optionsSourceId: CalendarOptionsSourceIds.Accounts,
			placeholder: AppStrings.Events.Calendar.AnyAccountPlaceholder());

	private static ActionParameter CalendarFilter()
		=> ActionParameter.DynamicChoice(CalendarIdParameter,
			label: AppStrings.Events.Calendar.CalendarLabel(),
			description: AppStrings.Events.Calendar.CalendarFilterDescription(),
			optionsSourceId: CalendarOptionsSourceIds.Calendars,
			placeholder: AppStrings.Events.Calendar.AnyCalendarPlaceholder());
}
