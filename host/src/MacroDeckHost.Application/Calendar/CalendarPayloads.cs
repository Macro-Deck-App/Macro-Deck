using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers.Providers;

namespace MacroDeckHost.Application.Calendar;

public static class CalendarPayloads
{
	public static async Task<IReadOnlyDictionary<string, object?>> BuildAsync(
		IServiceProvider services,
		CalendarEventSummary calendarEvent)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(calendarEvent);

		var providerName = calendarEvent.ProviderName.IsLocalized
			? await ActiveLocalization.Resolve(services, calendarEvent.ProviderName).ConfigureAwait(false)
			: calendarEvent.ProviderName.Literal ?? string.Empty;

		return CalendarEventProvider.PayloadOf(calendarEvent, providerName);
	}
}
