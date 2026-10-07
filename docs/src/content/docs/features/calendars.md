---
title: Calendars
description: Expose calendar accounts and events with ICalendarProvider - accounts from setup flows, overlap and recurrence rules, all-day events, event details, and what Macro Deck builds on top.
---

An integration exposes calendars by implementing `ICalendarProvider`. It lists the accounts it can read,
and for each account returns its calendars, the events in a time range and the full details of one event.
Macro Deck merges the accounts of every provider, so the user's widgets, triggers and the **Join Meeting**
action work with your service next to the built-in Google Calendar and Outlook Calendar providers without any
further code.

Connecting an account is not part of this contract. Signing in, storing tokens and asking the user to
sign in again belong to your [setup flow](/features/setup-flows/) and your
[integration issues](/features/integration-issues/). `ICalendarProvider` only reads.

## Quick start

```csharp
using MacroDeck.Plugin.Hosting.Integrations.HostApis; // IPluginCatalogNotifier
using MacroDeck.Plugin.Protocol.Handshake;             // CapabilityKinds
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Calendar;

public sealed class TeamCalendarIntegration(IPluginCatalogNotifier catalog)
	: IPluginIntegration, ICalendarProvider
{
	private Dictionary<string, TeamCalendarClient> _clients = [];
	private IReadOnlyList<CalendarAccount> _accounts = [];

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public async Task InitializeAsync(IIntegrationContext context)
	{
		var clients = new Dictionary<string, TeamCalendarClient>();
		var accounts = new List<CalendarAccount>();

		// One account per config entry your setup flow created.
		foreach (var entry in await context.Config.GetEntriesAsync())
		{
			var id = entry.Id.ToString("N");
			var token = await context.Config.GetSecretAsync(entry.Id, "token");
			clients[id] = new TeamCalendarClient(token);
			accounts.Add(new CalendarAccount { Id = id, DisplayName = entry.Title });
		}

		_clients = clients;
		_accounts = accounts;
		catalog.CatalogChanged(CapabilityKinds.Calendar);
	}

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<CalendarAccount> GetAccounts() => _accounts;

	public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(string accountId, CancellationToken ct)
		=> Client(accountId).GetCalendarsAsync(ct);

	public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
		string accountId, CalendarEventQuery query, CancellationToken ct)
		=> Client(accountId).GetEventsAsync(query.From, query.To, query.CalendarIds, ct);

	public Task<CalendarEvent?> GetEventAsync(
		string accountId, string calendarId, string eventId, CancellationToken ct)
		=> Client(accountId).GetEventAsync(calendarId, eventId, ct);

	// An account this provider no longer knows is a failure, not an empty calendar.
	private TeamCalendarClient Client(string accountId)
		=> _clients.TryGetValue(accountId, out var client)
			? client
			: throw new InvalidOperationException($"Unknown calendar account '{accountId}'.");
}
```

Register it like any integration, with `RegisterIntegration<TeamCalendarIntegration>()` in `Program.cs`.
The SDK sees `ICalendarProvider` and declares the `calendar` capability for you. Add an
`IConfigFlowProvider` with `AllowsMultipleConfigurations` so the user can connect one account per entry.

Things to know:

- **Account ids are local and permanent.** Return the id you chose, not a qualified one. The host
  stores the user's calendar choice as `integrationId::accountId` plus the calendar id, so an id that
  changes on restart silently empties every widget and trigger filter that used it. A config entry id is
  a good choice.
- **`GetAccounts` is synchronous and called often.** Return a cached list, as above.
- **Tell the host when the accounts change.** After adding, removing or renaming an account, call
  `CatalogChanged(CapabilityKinds.Calendar)` on the injected `IPluginCatalogNotifier`. The host then reads
  the accounts again and fetches the events of every account anew. Saving or removing a config entry
  reinitializes your integration, so calling it at the end of `InitializeAsync` covers the usual case.
- **You only read.** The host decides when to read, caches the events and drives every widget, trigger
  and action from that cache.

## The contract

`ICalendarProvider` and its records carry the full rules in their XML docs. The ones that decide whether
your provider behaves correctly:

| Member | Rules |
| --- | --- |
| `GetAccounts()` | Each `Id` is a valid resource local id (non-empty, at most 256 characters, no whitespace and no `::`), unique within the provider and stable across restarts. An account that breaks this is skipped and logged. |
| `GetCalendarsAsync` | The account's calendars. `Id` is opaque to the host but must be unique within the account and stable. `Color` is `#RRGGBB` or `null`; any other format is ignored. |
| `GetEventsAsync` | The events **overlapping** `From` up to, not including, `To`: an event counts when it starts before `To` and ends after `From`. An empty `CalendarIds` means every calendar of the account. |
| `GetEventAsync` | One event with every detail, by the ids `GetEventsAsync` returned. `null` only when the event no longer exists. |

### Throw, don't return empty

Every read **throws when it fails** and returns an empty list only when there genuinely is nothing. The
host shows the two differently: an empty result is "no events", a failure keeps the account's last
known events on screen and tells the user that some calendars could not be updated. An account id you no
longer know is a failure too. Honour the cancellation token and let `OperationCanceledException`
propagate.

### Recurring events

Expand recurring events. Return one `CalendarEvent` per occurrence in the range, each with an `Id` of
its own that stays the same across reads and that `GetEventAsync` accepts:

```csharp
new CalendarEvent
{
	Id = $"{series.Id}_{occurrence.Start.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}", // one id per occurrence
	CalendarId = calendarId,
	Title = series.Summary,
	Start = occurrence.Start,
	End = occurrence.End
};
```

The host tells occurrences apart by `CalendarId` and `Id`. Two occurrences with the same id collapse into
one, and a trigger fires once for it.

### All-day events

Set `IsAllDay` and only the dates of `Start` and `End` count, each in its own offset. The event covers
`Start.Date` up to, but not including, `End.Date`, and Macro Deck places those days in the local time zone
of the computer it runs on. A one-day event on 5 October:

```csharp
new CalendarEvent
{
	Id = "offsite",
	CalendarId = "team",
	Title = "Company offsite",
	Start = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
	End = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
	IsAllDay = true
};
```

Include an all-day event in `GetEventsAsync` when its days overlap the days `From` and `To` fall on. The
host filters the result again, so an extra event is harmless while a missing one is not.

### Details

`GetEventsAsync` may return everything, but the host may drop `Description` and `Participants` from that
list and read them through `GetEventAsync` when the user opens one event. Over the plugin protocol it
always does, see [below](#over-the-plugin-protocol). So make `GetEventAsync` return the full event:

| Property | Meaning |
| --- | --- |
| `Title` | Shown everywhere. An empty title shows as "(No title)" in the user's language. |
| `Location` | Free text. Optional. |
| `Description` | Plain text or HTML. The host removes the markup before showing it, so do not rely on markup for meaning. |
| `MeetingUrl` | The link to the online meeting. Only an absolute `http` or `https` URL is used, anything else is ignored. |
| `Participants` | `CalendarParticipant` with `Name`, `Email`, `IsOrganizer` and `Response` (`Unknown`, `NeedsAction`, `Accepted`, `Declined`, `Tentative`). Set at least one of `Name` and `Email`. |

## What Macro Deck builds on it

Implementing `ICalendarProvider` is all it takes for your accounts to appear in everything Macro Deck
offers for calendars. You declare none of it:

- The **Calendar** widget, in its **Agenda** and **Next event** layouts, whose settings list your
  calendars by name, with the account and provider beside them. Pressing it in the Next event layout, or an
  event in the list an Agenda opens, shows a details dialog, filled from `GetEventAsync`, with a **Join meeting** button for its `MeetingUrl`.
- The automation of that widget: press actions, the widget's own **Event Starts Soon**, **Event
  Started** and **Event Ended** triggers for the calendars a widget shows, the **Show Calendar Details**
  action, and widget variables such as `calendar_next_title` and `calendar_next_countdown` for the event
  a widget shows. They work the same for your calendars as for the built-in ones.
- The triggers **Event Starts Soon**, **Event Started** and **Event Ended**, filterable by account and
  calendar, whose event values include the title, times, calendar, account, provider name, location and
  meeting link.
- The **Join Meeting** action, which opens the meeting link of the event that is running or about to start
  on the computer running Macro Deck.
- The **Refresh Calendars** action, which reads every provider's accounts right away instead of waiting
  for the next update.

The [user guide](/guide/concepts/#calendar-widget) describes them from the user's side. Do not
build your own versions of these. If your service offers more, such as accepting an invitation, add
[actions](/features/actions/) of your own.

`ProviderName` is optional. Leave it out and the integration's name (the manifest name for a plugin) is
shown next to the account. Set it when one integration exposes a distinctly branded service.

## Edge cases

- **An account disappears.** Drop it from `GetAccounts` and call `CatalogChanged`. Its events leave the
  widgets with the next refresh; widget and trigger filters naming it simply match nothing.
- **Invalid or duplicate account ids** are skipped and logged. Calendars and events with an empty id are
  ignored, and a repeated id counts once.
- **A read fails.** The account keeps its last events and the widgets say that some calendars could not
  be updated, until a later read succeeds.
- **The same event in two accounts**, such as an invitation seen by two connected accounts, shows once per
  account: the host has no way to know they are one event.
- **A timed event that ends before it starts** is treated as ending when it starts, and an all-day event
  whose end date is not after its start date as lasting one day.

## Over the plugin protocol

Calendars are fully supported out of process, as capability kind `calendar`, version `1`, declared once
at the local id `provider`. Its operations are `describe`, `accounts`, `calendars`, `events` and `event`;
the [WebSocket reference](/reference/websocket/#calendar) lists their arguments and results.

- **The account list is a snapshot.** The host serves `GetAccounts` from the last `describe` or `accounts`
  reply, which is why `CatalogChanged(CapabilityKinds.Calendar)` matters.
- **Events travel as summaries.** An `events` reply carries no `Description` and no `Participants`; the
  details dialog reads them with `event`.
- **Replies are bounded.** `MacroDeck.Plugin.Hosting` cuts long titles, locations and descriptions,
  drops a meeting URL too long to work, keeps at most 100 participants, and, when an `events` reply would
  still exceed its size limit, drops its latest events and marks the reply as truncated. The host logs a
  warning when that happens. The exact limits are in the
  [WebSocket reference](/reference/websocket/#calendar); the host currently asks for one day at a time,
  which keeps an ordinary calendar far below them.
- **Failures stay failures.** An unreachable plugin, a timeout or an exception in your provider reaches
  the host as a failed read, never as an empty calendar.
- **Older hosts.** A Macro Deck that predates calendars rejects the `calendar` declaration non-fatally:
  the session carries on, and your actions, variables and everything else keep working. Your accounts
  simply do not appear.

See [Capability parity](/reference/capability-parity/).

## Testing

`harness.Calendar`, a `CalendarTestClient`, drives the `calendar` capability the way the host does, with
the protocol's argument records from `MacroDeck.Plugin.Protocol.Capabilities.Calendar`:

```csharp
await using var harness = PluginTestHarness.Create(builder =>
	builder.RegisterIntegration<TeamCalendarIntegration>());
var entry = harness.Context.Config.AddEntry("Work");
harness.Context.Config.SeedSecret(entry, "token", "test-token");
await harness.InitializeIntegrationsAsync();

var outcome = await harness.Calendar.GetEventsAsync(new CalendarEventsArguments
{
	AccountId = entry.ToString("N"),
	From = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(2)),
	To = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.FromHours(2))
});

Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
Assert.That(outcome.DataAs<CalendarEventsResult>()!.Events.Select(e => e.Title), Is.EqualTo(["Stand-up"]));
```

Here `TeamCalendarClient` talks to a fake of the team service, the one real external boundary.
`DescribeAsync`, `GetAccountsAsync`, `GetCalendarsAsync` and `GetEventAsync` cover the other operations,
and the session `MacroDeckTestHost` returns has the same `Calendar` client over the real wire. What you
get back is what the host gets: event summaries without details, the size limits applied, and an account
id you do not know as a failed outcome. See [Testing](/features/testing/).

## See also

- [Setup flows](/features/setup-flows/) - connecting accounts, including OAuth.
- [Integration issues](/features/integration-issues/) - asking the user to sign in again.
- [Events](/features/events/) - triggers of your own beside the built-in calendar ones.
- [Localization](/features/localization/)
- [Testing](/features/testing/)
