---
title: Email inboxes
description: Expose email inboxes with IEmailInboxProvider - inbox ids, status and counts, the newest messages, change notifications, limits, and what Macro Deck builds on top.
---

An integration exposes email inboxes by implementing `IEmailInboxProvider`. It lists the inboxes it can
read, and for each inbox returns a snapshot: whether it is connected, how many messages are unread and in
total, and the newest messages with their sender, subject and a short preview. Macro Deck merges the
inboxes of every provider, so the user's email widget, variables and triggers work with your service next to
the built-in IMAP integration without any further code.

Connecting an account is not part of this contract. Signing in, storing passwords or tokens and asking the
user to sign in again belong to your [setup flow](/features/setup-flows/) and your
[integration issues](/features/integration-issues/). `IEmailInboxProvider` only reads, and never the full
body of a message.

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Email;

public sealed class TeamMailIntegration : IPluginIntegration, IEmailInboxProvider
{
	private Dictionary<string, TeamMailClient> _clients = [];
	private IReadOnlyList<EmailInbox> _inboxes = [];

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public event EventHandler<EmailInboxChangedEventArgs>? Changed;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		var clients = new Dictionary<string, TeamMailClient>();
		var inboxes = new List<EmailInbox>();

		// One inbox per config entry your setup flow created.
		foreach (var entry in await context.Config.GetEntriesAsync())
		{
			var id = entry.Id.ToString("N");
			var client = new TeamMailClient(await context.Config.GetSecretAsync(entry.Id, "token"));
			client.MailboxChanged += (_, _) => Changed?.Invoke(this, new EmailInboxChangedEventArgs(id));
			clients[id] = client;
			inboxes.Add(new EmailInbox { Id = id, DisplayName = entry.Title, Address = client.Address });
		}

		_clients = clients;
		_inboxes = inboxes;
		Changed?.Invoke(this, new EmailInboxChangedEventArgs(null));
	}

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<EmailInbox> GetInboxes() => _inboxes;

	public async Task<EmailInboxSnapshot> GetSnapshotAsync(
		string inboxId, EmailMessageQuery query, CancellationToken ct)
	{
		// An inbox this provider no longer knows is a failure, not an empty inbox.
		var client = _clients.TryGetValue(inboxId, out var found)
			? found
			: throw new InvalidOperationException($"Unknown inbox '{inboxId}'.");

		var mailbox = await client.ReadAsync(query.MaxMessages, query.UnreadOnly, ct);

		return new EmailInboxSnapshot
		{
			Status = client.IsConnected ? EmailInboxStatus.Connected : EmailInboxStatus.Disconnected,
			UnreadCount = mailbox.Unread,
			TotalCount = mailbox.Total,
			SyncedAt = mailbox.SyncedAt,
			Messages =
			[
				.. mailbox.Messages.Select(m => new EmailMessage
				{
					Id = m.Id,
					SenderName = m.FromName,
					SenderAddress = m.FromAddress,
					Subject = m.Subject ?? string.Empty,
					Preview = m.Snippet,
					ReceivedAt = m.ReceivedAt,
					IsRead = m.IsRead
				})
			]
		};
	}
}
```

Register it like any integration, with `RegisterIntegration<TeamMailIntegration>()` in `Program.cs`. The
SDK sees `IEmailInboxProvider` and declares the `email-inbox` capability for you. Add an
`IConfigFlowProvider` with `AllowsMultipleConfigurations` so the user can connect one account per entry.

Things to know:

- **Inbox ids are local and permanent.** Return the id you chose, not a qualified one. The host stores the
  user's inbox selection and trigger filters as `integrationId::inboxId`, and names the inbox's variables
  once, the first time it sees the id. An id that changes on restart turns every inbox into a new one: its
  widget selection is lost and it gets new variables. A config entry id is a good choice.
- **`GetInboxes` is synchronous and called often.** Return a cached list, as above.
- **Raise `Changed` when something changed.** With an inbox id when that inbox's messages, counts or status
  changed; with `null` when inboxes were added, removed or renamed. Raise it as often as changes happen:
  bursts are coalesced before anything reads again.
- **You only read.** The host decides when to read, caches the snapshots and drives every widget,
  variable and trigger from that cache. It never marks a message as read or changes the mailbox.

## The contract

`IEmailInboxProvider` and its records carry the full rules in their XML docs. The ones that decide whether
your provider behaves correctly:

| Member | Rules |
| --- | --- |
| `GetInboxes()` | Each `Id` is a valid resource local id (non-empty, at most 256 characters, no whitespace and no `::`), unique within the provider and stable across restarts. An inbox that breaks this is skipped and logged. `DisplayName` is shown to the user; `Address` is optional. No status here: an unreachable inbox stays in the list. |
| `GetSnapshotAsync` | The inbox's `Status`, `UnreadCount` and `TotalCount` for the whole inbox, and at most `query.MaxMessages` messages, **newest first**. `SyncedAt` says when the data was last brought up to date. |
| `EmailMessageQuery` | `MaxMessages` is between 1 and 50 and defaults to 10. With `UnreadOnly`, return the newest **unread** messages wherever they are in the inbox, not the unread ones among the newest. |
| `EmailMessage` | `Id` is unique within the inbox and stable across reads; it is how the host tells new mail from mail it has already seen. `Subject` is empty when the message has none. `Preview` is a short plain-text excerpt or `null`, never the body. |
| `Changed` | `InboxId` set: read that inbox again. `null`: read the inbox list again, then every inbox. May be raised on any thread. |

### Status, not exceptions, for a known state

`GetSnapshotAsync` reports what you know and throws only when you cannot answer at all:

| Situation | Return |
| --- | --- |
| Connected and the read worked. | `Connected` with current counts and messages. |
| Connecting for the first time, or reconnecting after a short drop. | `Connecting` with the last known data, empty if there is none yet. |
| The server cannot be reached for now. | `Disconnected` with the last known data and a `StatusMessage`. |
| The user has to act, for example the sign-in was refused. | `Error` with the last known data and a `StatusMessage` that says what to do, ideally next to an [integration issue](/features/integration-issues/). |
| The read failed and you have nothing to return, or the inbox id is unknown. | Throw. |

The host shows the inbox's status next to its data, and an Email Inbox widget that shows only this inbox
shows its `StatusMessage` while it is not connected. It fires the **Inbox Connected** and **Inbox
Disconnected** triggers when it changes between connected and not connected; `Connecting` changes neither.
After a thrown read it keeps the last data, marks it as out of date and tries again shortly; only a second
failure in a row counts as disconnected. Write `StatusMessage` in the user's language, see
[Localization](/features/localization/). Honour the cancellation token and let
`OperationCanceledException` propagate.

### Message ids

The host decides which messages are new by comparing ids between two `Connected` snapshots, and fires
**Email Received** once per new id. The first `Connected` snapshot of an inbox fires nothing, and a snapshot
with any other status never counts as the inbox's contents, so answering `Connecting` with no data does not
make the mail you report afterwards look new. Make the id stable for as long as the message exists, for
example an IMAP UIDVALIDITY and UID pair:

```csharp
new EmailMessage
{
	Id = $"{folder.UidValidity}-{uid}", // stable while the folder keeps its UIDVALIDITY
	SenderAddress = "bob@example.com",
	Subject = "Lunch?",
	ReceivedAt = internalDate
};
```

An id that changes between reads makes old mail look new. If your service ever renumbers every message at
once, as an IMAP UIDVALIDITY change does, the host recognizes that none of the new ids is newer than what it
had and fires nothing.

### Telling the host about changes

Raising `Changed` is the only push this contract has. In a plugin, `MacroDeck.Plugin.Hosting` forwards it to
the host as a `state.update` for the `email-inbox` kind, after holding it back for 250 ms per inbox so a
burst becomes one message. Calling `CatalogChanged("email-inbox", inboxId)` on the injected
`IPluginCatalogNotifier` yourself has exactly the same effect, and `CatalogChanged("email-inbox")` with no id
is the same as raising `Changed` with `null`.

A provider that never raises `Changed` still works: the host also reads every inbox after a user's
**Refresh Email Inboxes** and on a fallback poll every five minutes. New mail simply shows up that much later.

## What Macro Deck builds on it

Implementing `IEmailInboxProvider` is all it takes for your inboxes to appear in everything Macro Deck
offers for email. You declare none of it:

- The **Email Inbox** widget, which shows the unread count of one or several inboxes, or a list of the
  newest messages with sender, subject, preview and time, and whose settings list your inboxes by name with
  the provider beside them.
- Global variables such as `email_unread_count` and `email_inbox_count`, and per inbox
  `email_<name>_unread_count`, `email_<name>_total_count`, `email_<name>_latest_sender`,
  `email_<name>_latest_subject`, `email_<name>_latest_received_at` and `email_<name>_is_connected`, where
  `<name>` comes from the inbox's display name the first time the host sees it and is kept after a rename.
  At most 40 inboxes get variables. While your integration is disabled, its inboxes keep their variables,
  which read as unavailable.
- The triggers **Email Received**, filterable by inbox and by exact sender and subject, **Unread Count
  Changed**, **Email Read or Unread**, **Inbox Connected** and **Inbox Disconnected**, under the event
  provider `email`. Their payload values are `inbox`, `inboxName`, `sender`, `senderAddress`, `subject`,
  `preview`, `messageId`, `receivedAt`, `unreadCount`, `previousUnreadCount` and `isRead`. **Email
  Received** does not fire for the messages an inbox already has when the host starts or when the inbox first
  appears; mail that arrived while the inbox was unreachable fires once when it is back.
- The **Refresh Email Inboxes** action, which reads every inbox right away instead of waiting for the next
  change.

The [user guide](/guide/concepts/#email-inbox-widget) describes them from the user's side. Do not build your own versions
of these. If your service offers more, such as archiving a message, add [actions](/features/actions/) of
your own.

`ProviderName` is optional. Leave it out and the integration's name (the manifest name for a plugin) is
shown next to the inbox. Set it when one integration exposes a distinctly branded service.

## Edge cases

- **An inbox disappears.** Drop it from `GetInboxes` and raise `Changed` with `null`. It leaves the widgets
  and its variables are removed; widget and trigger filters naming it simply match nothing.
- **Invalid or duplicate inbox ids** are skipped and logged. An id two integrations of one plugin both list
  belongs to the first.
- **A read fails.** The inbox keeps its last data, marked as out of date, until a later read succeeds.
- **The plugin stops or crashes.** Its inboxes keep their last data and show as disconnected until it is
  back.
- **The same mailbox in two providers**, such as one account connected twice, shows twice: the host has no
  way to know they are one inbox.

## Optional abilities later

The contract is read-only on purpose. Abilities such as marking a message as read, archiving or moving it,
reading its full body or listing other folders will be added as separate interfaces a provider may implement
next to `IEmailInboxProvider`, the way `IMusicPlayerDeviceProvider` sits next to `IMusicPlayerProvider`. They
will be optional: a provider that implements none of them keeps working unchanged, and Macro Deck only offers
an ability for the inboxes whose provider has it.

## Over the plugin protocol

Email inboxes are fully supported out of process, as capability kind `email-inbox`, version `1`, declared
once at the local id `provider`. Its operations are `describe`, `inboxes` and `snapshot`; the
[WebSocket reference](/reference/websocket/#email-inbox) lists their arguments and results.

- **The inbox list is a snapshot.** The host serves `GetInboxes` from the last `describe` or `inboxes`
  reply, and reads `inboxes` again after a `state.update` without a local id.
- **A change names the inbox.** A `state.update` whose `localId` is an inbox id makes the host read that
  inbox's `snapshot` again, nothing else: the plugin is not described again and nothing re-registers.
- **The status travels by name.** `status` is the `EmailInboxStatus` member name; a host treats a name it
  does not know as `Disconnected`.
- **Replies are bounded.** `MacroDeck.Plugin.Hosting` keeps at most 32 inboxes, asks your provider for at
  most 50 messages, cuts long subjects, previews, sender names and addresses, and, when a `snapshot` reply
  would still exceed its size limit, drops its oldest messages and marks the reply as truncated. The exact
  limits are in the [WebSocket reference](/reference/websocket/#email-inbox).
- **Failures stay failures.** An unreachable plugin, a timeout or an exception in your provider reaches the
  host as a failed read, never as an empty inbox.
- **Older hosts.** A Macro Deck that predates email inboxes rejects the `email-inbox` declaration
  non-fatally: the session carries on, and your actions, variables and everything else keep working. Your
  inboxes simply do not appear.

See [Capability parity](/reference/capability-parity/).

## Testing

`harness.EmailInbox`, an `EmailInboxTestClient`, drives the `email-inbox` capability the way the host does,
with the protocol's argument records from `MacroDeck.Plugin.Protocol.Capabilities.EmailInbox`:

```csharp
await using var harness = PluginTestHarness.Create(builder =>
	builder.RegisterIntegration<TeamMailIntegration>());
var entry = harness.Context.Config.AddEntry("Work");
harness.Context.Config.SeedSecret(entry, "token", "test-token");
await harness.InitializeIntegrationsAsync();

var outcome = await harness.EmailInbox.GetSnapshotAsync(new EmailInboxSnapshotArguments
{
	InboxId = entry.ToString("N"),
	MaxMessages = 5,
	UnreadOnly = true
});

Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
Assert.That(outcome.DataAs<EmailInboxSnapshotResult>()!.Messages.Select(m => m.Subject), Is.EqualTo(["Lunch?"]));
```

Here `TeamMailClient` talks to a fake of the mail service, the one real external boundary.
`DescribeAsync` and `GetInboxesAsync` cover the other operations, and the session `MacroDeckTestHost`
returns has the same `EmailInbox` client over the real wire, where raising `Changed` shows up in
`host.Messages` as a `state.update`. What you get back is what the host gets: the status as a name, the
field limits applied, and an inbox id you do not know as a failed outcome. In the harness, the 250 ms hold on
`Changed` runs on `harness.Clock`. See [Testing](/features/testing/).

## See also

- [Setup flows](/features/setup-flows/) - connecting accounts.
- [Integration issues](/features/integration-issues/) - asking the user to sign in again.
- [Events](/features/events/) - triggers of your own beside the built-in email ones.
- [Localization](/features/localization/)
- [Testing](/features/testing/)
