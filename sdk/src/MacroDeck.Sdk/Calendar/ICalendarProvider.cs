namespace MacroDeck.Sdk.Calendar;

/// <summary>
/// Implemented by integrations that read calendars from one or more accounts. The host merges the
/// accounts of every enabled provider, so a calendar widget or trigger can show events from several
/// services and accounts at once. Connecting and authorizing an account is not part of this contract:
/// it belongs to the integration's own configuration flow and issues.
/// </summary>
public interface ICalendarProvider
{
	/// <summary>Human-readable provider name, e.g. "Google Calendar".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name - so the one place a name is stated stays
	/// the integration. Stating a name here still wins, which is what an integration exposing one or
	/// more distinctly-branded providers needs.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>The accounts this provider can currently read, typically one per configured account.</summary>
	/// <remarks>
	/// <para>
	/// Called often and synchronously, so return a cached list. Every <see cref="CalendarAccount.Id" /> must
	/// be a valid resource local id (<c>MacroDeckId.IsValidLocalId(id, LocalIdKind.Resource)</c>), unique
	/// within the provider and stable across restarts, because the user's calendar selection is stored
	/// against it. The host skips an account that breaks these rules and logs a warning.
	/// </para>
	/// <para>
	/// When the list changes, a plugin calls <c>IPluginCatalogNotifier.CatalogChanged("calendar")</c>. The
	/// host then reads the accounts again and fetches events for every account anew.
	/// </para>
	/// </remarks>
	IReadOnlyList<CalendarAccount> GetAccounts();

	/// <summary>The calendars of one account.</summary>
	/// <remarks>
	/// <b>Throw when the read fails; return an empty list only when the account genuinely has no
	/// calendars.</b> The host shows the two differently: empty is "nothing here", a failure is "could not
	/// load". An account id this provider no longer knows is a failure too. Honour
	/// <paramref name="cancellationToken" /> and let <see cref="OperationCanceledException" /> propagate.
	/// </remarks>
	Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(string accountId, CancellationToken cancellationToken);

	/// <summary>The events of one account that overlap <see cref="CalendarEventQuery.From" /> up to,
	/// but not including, <see cref="CalendarEventQuery.To" />.</summary>
	/// <remarks>
	/// <para>
	/// An event overlaps the range when it starts before <c>To</c> and ends after <c>From</c>. Expand
	/// recurring events into one <see cref="CalendarEvent" /> per occurrence in the range, each with its
	/// own <see cref="CalendarEvent.Id" /> that stays the same across reads and that
	/// <see cref="GetEventAsync" /> accepts. Include an all-day event when its days overlap the days
	/// <c>From</c> and <c>To</c> fall on; the host filters the result again, so an extra event is harmless
	/// while a missing one is not.
	/// </para>
	/// <para>
	/// The host may drop <see cref="CalendarEvent.Description" /> and
	/// <see cref="CalendarEvent.Participants" /> from this list and read them through
	/// <see cref="GetEventAsync" /> when it shows a single event.
	/// </para>
	/// <para>
	/// <b>Throw when the read fails; return an empty list only when there genuinely are no events.</b> An
	/// account id this provider no longer knows is a failure too. Honour
	/// <paramref name="cancellationToken" /> and let <see cref="OperationCanceledException" /> propagate.
	/// </para>
	/// </remarks>
	Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
		string accountId,
		CalendarEventQuery query,
		CancellationToken cancellationToken);

	/// <summary>
	/// One event with all its details, or <c>null</c> when it no longer exists. <paramref name="eventId" />
	/// is an <see cref="CalendarEvent.Id" /> this provider returned, including the id of a single
	/// occurrence of a recurring event.
	/// </summary>
	/// <remarks>
	/// Return <c>null</c> only when the event is gone; throw when the read fails. Honour
	/// <paramref name="cancellationToken" /> and let <see cref="OperationCanceledException" /> propagate.
	/// </remarks>
	Task<CalendarEvent?> GetEventAsync(
		string accountId,
		string calendarId,
		string eventId,
		CancellationToken cancellationToken);
}
