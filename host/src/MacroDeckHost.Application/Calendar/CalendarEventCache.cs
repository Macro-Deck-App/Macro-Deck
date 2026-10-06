using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Calendar;
using MacroDeckHost.Application.Applications;
using Serilog;

namespace MacroDeckHost.Application.Calendar;

public interface ICalendarEventCache
{
	CalendarSnapshot Snapshot { get; }

	TimeZoneInfo TimeZone { get; }

	event Action? Changed;

	Task SyncAsync(CancellationToken cancellationToken);

	Task<CalendarEventDetails?> GetEventDetailsAsync(
		string calendarKey,
		string eventId,
		CancellationToken cancellationToken);
}

public sealed class CalendarEventCache : ICalendarEventCache, IDisposable
{
	private readonly ICalendarRegistry _registry;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _syncLock = new(1, 1);

	private volatile CalendarSnapshot _snapshot = CalendarSnapshot.Empty;

	public CalendarEventCache(
		ICalendarRegistry registry,
		TimeProvider time,
		ILogger logger,
		TimeZoneInfo? timeZone = null)
	{
		_registry = registry;
		_time = time;
		_logger = logger.ForContext<CalendarEventCache>();
		TimeZone = timeZone ?? TimeZoneInfo.Local;
	}

	public CalendarSnapshot Snapshot => _snapshot;

	public TimeZoneInfo TimeZone { get; }

	public event Action? Changed;

	public async Task SyncAsync(CancellationToken cancellationToken)
	{
		await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var previous = _snapshot;
			var (from, to) = CalendarTime.SyncWindow(_time.GetUtcNow(), TimeZone);
			var accounts = new List<CalendarAccountState>();
			var events = new List<CalendarEventSummary>();

			foreach (var account in _registry.GetAccounts())
			{
				var (state, accountEvents) = await SyncAccountAsync(account, previous, from, to, cancellationToken)
					.ConfigureAwait(false);
				accounts.Add(state);
				events.AddRange(accountEvents);
			}

			var next = new CalendarSnapshot(from, to, accounts, [.. Order(events)]);
			_snapshot = next;

			if (!next.SameContentAs(previous))
			{
				Changed?.Invoke();
			}
		}
		finally
		{
			_syncLock.Release();
		}
	}

	public async Task<CalendarEventDetails?> GetEventDetailsAsync(
		string calendarKey,
		string eventId,
		CancellationToken cancellationToken)
	{
		if (!CalendarKeys.TryParseCalendar(calendarKey, out var accountId, out var calendarId))
		{
			return null;
		}

		var snapshot = _snapshot;
		var cached = snapshot.FindEvent(calendarKey, eventId);
		var resolved = _registry.Resolve(accountId);
		if (resolved is null)
		{
			return Fallback(cached);
		}

		try
		{
			var read = await resolved.Provider
				.GetEventAsync(resolved.Account.LocalAccountId, calendarId, eventId, cancellationToken)
				.ConfigureAwait(false);

			if (read is null)
			{
				return null;
			}

			var calendar = snapshot.Calendars.FirstOrDefault(c => c.Key == calendarKey);
			return new CalendarEventDetails(Normalize(resolved.Account, read, calendar),
				CalendarText.ClipOptional(CalendarText.ToPlainText(read.Description),
					ProtocolLimits.MaxCalendarDescriptionLength),
				Participants(read.Participants));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			_logger.Warning(exception,
				"Reading calendar event {EventId} of account {AccountId} failed; showing the synced summary",
				eventId,
				accountId);
			return Fallback(cached);
		}
	}

	public void Dispose() => _syncLock.Dispose();

	private async Task<(CalendarAccountState State, IReadOnlyList<CalendarEventSummary> Events)> SyncAccountAsync(
		CalendarAccountDescriptor account,
		CalendarSnapshot previous,
		DateTimeOffset from,
		DateTimeOffset to,
		CancellationToken cancellationToken)
	{
		var resolved = _registry.Resolve(account.AccountId);

		try
		{
			if (resolved is null)
			{
				throw new InvalidOperationException($"Calendar account {account.AccountId} is no longer available.");
			}

			var provider = resolved.Provider;
			var calendars = await provider.GetCalendarsAsync(account.LocalAccountId, cancellationToken)
				.ConfigureAwait(false);

			var summaries = new List<CalendarSummary>();
			var seenCalendars = new HashSet<string>(StringComparer.Ordinal);
			foreach (var calendar in calendars)
			{
				if (string.IsNullOrEmpty(calendar.Id) || !seenCalendars.Add(calendar.Id))
				{
					continue;
				}

				summaries.Add(new CalendarSummary(CalendarKeys.Calendar(account.AccountId, calendar.Id),
					account.AccountId,
					calendar.Id,
					CalendarText.Clip(string.IsNullOrWhiteSpace(calendar.Name) ? calendar.Id : calendar.Name.Trim(),
						ProtocolLimits.MaxCalendarTitleLength),
					calendar.Color,
					calendar.IsPrimary));
			}

			var read = new Dictionary<(string CalendarId, string EventId), CalendarEvent>();

			foreach (var (rangeFrom, rangeTo) in QueryRanges(resolved.IsPluginProvided, from, to))
			{
				var rangeEvents = await provider.GetEventsAsync(account.LocalAccountId,
						new CalendarEventQuery { From = rangeFrom, To = rangeTo },
						cancellationToken)
					.ConfigureAwait(false);

				foreach (var calendarEvent in rangeEvents)
				{
					if (!string.IsNullOrEmpty(calendarEvent.Id) && !string.IsNullOrEmpty(calendarEvent.CalendarId))
					{
						read.TryAdd((calendarEvent.CalendarId, calendarEvent.Id), calendarEvent);
					}
				}
			}

			var byKey = summaries.ToDictionary(summary => summary.Key, StringComparer.Ordinal);
			var events = read.Values
				.Select(calendarEvent => Normalize(account,
					calendarEvent,
					byKey.GetValueOrDefault(CalendarKeys.Calendar(account.AccountId, calendarEvent.CalendarId))))
				.Where(summary => summary.Overlaps(from, to))
				.ToList();

			return (new CalendarAccountState(account, CalendarAccountStatus.Ok, summaries), events);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			_logger.Warning(exception,
				"Syncing calendar account {AccountId} failed; keeping its last synced events",
				account.AccountId);

			var lastCalendars = previous.Accounts
				.FirstOrDefault(state => state.Account.AccountId == account.AccountId)
				?.Calendars ?? [];
			var lastEvents = previous.Events
				.Where(e => e.AccountId == account.AccountId && e.Overlaps(from, to))
				.ToList();

			return (new CalendarAccountState(account, CalendarAccountStatus.Error, lastCalendars), lastEvents);
		}
	}

	private CalendarEventSummary Normalize(
		CalendarAccountDescriptor account,
		CalendarEvent calendarEvent,
		CalendarSummary? calendar)
	{
		var calendarKey = CalendarKeys.Calendar(account.AccountId, calendarEvent.CalendarId);
		DateTimeOffset start;
		DateTimeOffset end;

		if (calendarEvent.IsAllDay)
		{
			var startDate = DateOnly.FromDateTime(calendarEvent.Start.Date);
			var endDate = DateOnly.FromDateTime(calendarEvent.End.Date);
			if (endDate <= startDate)
			{
				endDate = startDate.AddDays(1);
			}

			start = CalendarTime.StartOfDay(startDate, TimeZone);
			end = CalendarTime.StartOfDay(endDate, TimeZone);
		}
		else
		{
			start = TimeZoneInfo.ConvertTime(calendarEvent.Start, TimeZone);
			end = TimeZoneInfo.ConvertTime(calendarEvent.End, TimeZone);
			if (end < start)
			{
				end = start;
			}
		}

		return new CalendarEventSummary
		{
			InstanceKey = CalendarKeys.EventInstance(calendarKey, calendarEvent.Id),
			EventId = calendarEvent.Id,
			AccountId = account.AccountId,
			AccountName = account.DisplayName,
			ProviderName = account.ProviderName,
			IntegrationId = account.IntegrationId,
			CalendarKey = calendarKey,
			CalendarId = calendarEvent.CalendarId,
			CalendarName = calendar?.Name ??
				CalendarText.Clip(calendarEvent.CalendarId, ProtocolLimits.MaxCalendarTitleLength),
			CalendarColor = calendar?.Color,
			Title = CalendarText.Clip(calendarEvent.Title?.Trim(), ProtocolLimits.MaxCalendarTitleLength),
			Start = start,
			End = end,
			IsAllDay = calendarEvent.IsAllDay,
			Location = string.IsNullOrWhiteSpace(calendarEvent.Location)
				? null
				: CalendarText.Clip(calendarEvent.Location.Trim(), ProtocolLimits.MaxCalendarLocationLength),
			MeetingUrl = ExternalUrls.TryNormalizeWebUrl(calendarEvent.MeetingUrl, out var url) ? url : null
		};
	}

	private IEnumerable<(DateTimeOffset From, DateTimeOffset To)> QueryRanges(
		bool isPluginProvided,
		DateTimeOffset from,
		DateTimeOffset to)
	{
		if (!isPluginProvided)
		{
			yield return (from, to);
			yield break;
		}

		// A plugin reply is size-budgeted by the protocol, so one request per local day keeps a busy day
		// from truncating the rest of the window.
		for (var day = from; day < to; day = CalendarTime.NextMidnight(day, TimeZone))
		{
			var dayEnd = CalendarTime.NextMidnight(day, TimeZone);
			yield return (day, dayEnd < to ? dayEnd : to);
		}
	}

	private static IReadOnlyList<CalendarParticipant> Participants(IEnumerable<CalendarParticipant> participants)
		=> [.. participants.Take(ProtocolLimits.MaxCalendarParticipants)
			.Select(participant => participant with
			{
				Name = CalendarText.ClipOptional(participant.Name, ProtocolLimits.MaxCalendarTitleLength),
				Email = CalendarText.ClipOptional(participant.Email, ProtocolLimits.MaxCalendarTitleLength)
			})];

	private static CalendarEventDetails? Fallback(CalendarEventSummary? cached)
		=> cached is null ? null : new CalendarEventDetails(cached, null, []);

	private static IEnumerable<CalendarEventSummary> Order(IEnumerable<CalendarEventSummary> events)
		=> events.OrderBy(e => e.Start)
			.ThenBy(e => e.IsAllDay ? 0 : 1)
			.ThenBy(e => e.End)
			.ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(e => e.InstanceKey, StringComparer.Ordinal);
}
