using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Calendar;
using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

internal sealed class FakeCalendarIntegration : IIntegration, ICalendarProvider
{
	private readonly Dictionary<string, List<CalendarInfo>> _calendars = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<CalendarEvent>> _events = new(StringComparer.Ordinal);

	public FakeCalendarIntegration(string id, string providerName = "")
	{
		Id = id;
		ProviderName = providerName;
	}

	public string Id { get; }

	public LocalizedText Name => Id + " integration";

	public string Version => "1.0.0";

	public string ProviderName { get; }

	public bool IsInitialized => true;

	public IReadOnlyList<IActionDefinition> Actions => [];

	public List<CalendarAccount> Accounts { get; } = [];

	public HashSet<string> FailingAccounts { get; } = new(StringComparer.Ordinal);

	public bool IgnoresRange { get; set; }

	public int? MaxEventsPerReply { get; set; }

	public List<(string AccountId, CalendarEventQuery Query)> Queries { get; } = [];

	public Dictionary<string, CalendarEvent?> Details { get; } = new(StringComparer.Ordinal);

	public Exception? DetailsFailure { get; set; }

	public Task ReadsHeldBy { get; set; } = Task.CompletedTask;

	public FakeCalendarIntegration WithAccount(string accountId, params string[] calendarIds)
	{
		Accounts.Add(new CalendarAccount { Id = accountId, DisplayName = accountId + "@example.com" });
		_calendars[accountId] = [.. calendarIds.Select(id => new CalendarInfo { Id = id, Name = "Calendar " + id })];
		_events[accountId] = [];
		return this;
	}

	public FakeCalendarIntegration WithEvent(string accountId, CalendarEvent calendarEvent)
	{
		_events[accountId].Add(calendarEvent);
		return this;
	}

	public void ClearEvents(string accountId) => _events[accountId].Clear();

	public IReadOnlyList<CalendarAccount> GetAccounts() => Accounts;

	public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(string accountId, CancellationToken cancellationToken)
	{
		ThrowIfFailing(accountId);
		return Task.FromResult<IReadOnlyList<CalendarInfo>>(_calendars[accountId]);
	}

	public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
		string accountId,
		CalendarEventQuery query,
		CancellationToken cancellationToken)
	{
		await ReadsHeldBy;
		ThrowIfFailing(accountId);
		Queries.Add((accountId, query));

		var events = IgnoresRange
			? _events[accountId]
			: _events[accountId].Where(e => e.Start < query.To && e.End > query.From);
		IReadOnlyList<CalendarEvent> reply = [.. events.OrderBy(e => e.Start).Take(MaxEventsPerReply ?? int.MaxValue)];
		return reply;
	}

	public Task<CalendarEvent?> GetEventAsync(
		string accountId,
		string calendarId,
		string eventId,
		CancellationToken cancellationToken)
	{
		if (DetailsFailure is not null)
		{
			throw DetailsFailure;
		}

		return Task.FromResult(Details.GetValueOrDefault(eventId));
	}

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	private void ThrowIfFailing(string accountId)
	{
		if (FailingAccounts.Contains(accountId))
		{
			throw new InvalidOperationException("The calendar service is unreachable.");
		}
	}
}

internal sealed class RecordingUrlOpener : IExternalUrlOpener
{
	public List<string> Opened { get; } = [];

	public bool Locked { get; set; }

	public Result<ExternalUrlOpenError> Open(string? url)
	{
		if (!ExternalUrls.TryNormalizeWebUrl(url, out var normalized))
		{
			return Result.Fail(ExternalUrlOpenError.InvalidUrl);
		}

		if (Locked)
		{
			return Result.Fail(ExternalUrlOpenError.HostLocked);
		}

		Opened.Add(normalized);
		return Result.Ok<ExternalUrlOpenError>();
	}
}

internal static class CalendarTesting
{
	public static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	public static CalendarEvent Event(
		string id,
		DateTimeOffset start,
		TimeSpan duration,
		string calendarId = "main",
		string? title = null,
		string? meetingUrl = null)
		=> new()
		{
			Id = id,
			CalendarId = calendarId,
			Title = title ?? "Event " + id,
			Start = start,
			End = start + duration,
			MeetingUrl = meetingUrl
		};

	public static CalendarEventCache Cache(
		FakeTimeProvider time,
		TimeZoneInfo? timeZone = null,
		params IIntegration[] integrations)
		=> new(Registry(integrations), time, Logger(), timeZone ?? TimeZoneInfo.Utc);

	public static CalendarRegistry Registry(params IIntegration[] integrations)
		=> new(new ConfigurableIntegrationRegistry(integrations), Logger());

	public static ILogger Logger() => new LoggerConfiguration().CreateLogger();
}
