using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Calendar;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

internal sealed class TestCalendarIntegration(string providerName, params string[] accountIds)
	: IPluginIntegration, ICalendarProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName { get; } = providerName;

	public Dictionary<string, IReadOnlyList<CalendarInfo>> Calendars { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, IReadOnlyList<CalendarEvent>> Events { get; } = new(StringComparer.Ordinal);

	public CalendarEventQuery? LastQuery { get; private set; }

	public IReadOnlyList<CalendarAccount> GetAccounts()
		=> [.. accountIds.Select(id => new CalendarAccount { Id = id, DisplayName = $"{id}@example.com" })];

	public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(string accountId, CancellationToken cancellationToken)
		=> Task.FromResult(Calendars.GetValueOrDefault(accountId) ?? []);

	public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
		string accountId,
		CalendarEventQuery query,
		CancellationToken cancellationToken)
	{
		LastQuery = query;
		return Task.FromResult(Events.GetValueOrDefault(accountId) ?? []);
	}

	public Task<CalendarEvent?> GetEventAsync(
		string accountId,
		string calendarId,
		string eventId,
		CancellationToken cancellationToken)
		=> Task.FromResult((Events.GetValueOrDefault(accountId) ?? []).FirstOrDefault(calendarEvent =>
			calendarEvent.CalendarId == calendarId && calendarEvent.Id == eventId));
}
