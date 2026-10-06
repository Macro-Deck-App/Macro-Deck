using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Calendar;

public static class CalendarEventDialogs
{
	public const string ViewId = "calendar-event";

	public const string AgendaViewId = "calendar-agenda";

	public const string CalendarKey = "calendarKey";

	public const string EventKey = "eventId";

	public const string WidgetKey = "widgetId";

	public static ModalDefinition For(CalendarEventSummary calendarEvent)
	{
		ArgumentNullException.ThrowIfNull(calendarEvent);

		return new ModalDefinition
		{
			ViewId = ViewId,
			Title = CalendarCountdown.Title(calendarEvent.Title),
			Data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[CalendarKey] = JsonSerializer.SerializeToElement(calendarEvent.CalendarKey),
				[EventKey] = JsonSerializer.SerializeToElement(calendarEvent.EventId),
			},
		};
	}

	public static ModalDefinition ForAgenda(Guid widgetId)
		=> new()
		{
			ViewId = AgendaViewId,
			Title = AppStrings.Widgets.Calendar.Layouts.Agenda(),
			Data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[WidgetKey] = JsonSerializer.SerializeToElement(widgetId.ToString()),
			},
		};
}
