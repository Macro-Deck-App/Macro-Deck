---
title: Integration issues
description: Report problems the user can act on with IIntegrationIssueProvider - severity, localized text, and a resolve button that can reopen setup.
---

An integration reports a problem the user can understand and fix - wrong endpoint, missing permission,
expired credentials - by implementing `IIntegrationIssueProvider`. Macro Deck shows it as a badge on the
integration list and a box in the integration's detail view, optionally with a button that fixes it.

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Issues;

public sealed class StreamStudioIntegration : IPluginIntegration, IIntegrationIssueProvider
{
	private const string WrongEndpointIssueId = "wrong-endpoint";

	private StudioConnection? _connection;

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<IntegrationIssue> issues = _connection?.NeedsSetup == true
			?
			[
				new IntegrationIssue
				{
					Id = WrongEndpointIssueId,
					Title = Strings.Issues.WrongEndpointTitle(),
					Description = Strings.Issues.WrongEndpointDescription(),
					Severity = IntegrationIssueSeverity.Error,
					ActionLabel = Strings.Issues.OpenSetup()
				}
			]
			: [];

		return Task.FromResult(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId == WrongEndpointIssueId
			? IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed(Strings.Issues.UnknownIssue()));

	// IPluginIntegration members omitted.
}
```

While the connection needs setup, the user sees a red badge and an "Open setup" button; clicking it
opens your [configuration flow](/features/setup-flows/). When `NeedsSetup` turns
false, the issue disappears on the next poll.

Things to know:

- **`GetIssuesAsync` is polled** to render badges. Return cached state; never connect, probe or block in
  it. Do the real work in `ResolveIssueAsync`.
- **Issues are for the user, logs are for you.** An issue says what is wrong and what to do; stack
  traces and retries belong in the [log](/features/logging/).
- **Only report what needs the user.** A state that recovers on its own ("the app is not running yet")
  is not an issue - expose it as a variable such as `studio_is_connected` instead.

## What an issue carries

| Property | What it does | Example |
| --- | --- | --- |
| `Id` | Stable id passed back to `ResolveIssueAsync`. Required. | `"wrong-endpoint"` |
| `Title` | Localized headline. Required. | `Strings.Issues.WrongEndpointTitle()` |
| `Description` | Localized detail: what happened and what to do. | `Strings.Issues.WrongEndpointDescription()` |
| `Severity` | `Info`, `Warning` (default) or `Error`. Colours the badge and orders the list. | `IntegrationIssueSeverity.Error` |
| `ActionLabel` | Text of the resolve button. Leave it unset for an informational issue with no button. | `Strings.Issues.OpenSetup()` |

An `Error` issue additionally raises one user notification when it first appears, linking to the
integration. It notifies again only after it has gone away and come back.

## Resolving an issue

```csharp
public async Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
{
	switch (issueId)
	{
		case "reconnect":
			return await _connection.ReconnectAsync(cancellationToken)
				? IssueResolution.Ok()
				: IssueResolution.Failed(Strings.Issues.StillUnreachable());

		case "permission":
			return IssueResolution.Ok(Strings.Issues.GrantInSystemSettings());

		case "credentials-expired":
			return IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow);

		default:
			return IssueResolution.Failed(Strings.Issues.UnknownIssue());
	}
}
```

| Result | What the user sees |
| --- | --- |
| `IssueResolution.Ok()` | The issue list refreshes. |
| `IssueResolution.Ok(message)` | A success toast with the message, then a refresh - for a fix only the user can finish. |
| `Ok(followUp: StartConfigFlow)` | Your configuration flow opens, e.g. to re-enter credentials. A message is not shown. |
| `IssueResolution.Failed(message)` | An error toast with the message, then a refresh. |

The resolve button runs `ResolveIssueAsync` and nothing else - whether the issue is gone is decided by
the next `GetIssuesAsync`, so make the fix change the state that method reads.

## Edge cases

- **Invalid ids are dropped.** An issue id must be usable as a Macro Deck resource id: no `::`,
  whitespace or control characters, at most `MacroDeckId.MaxResourceLocalIdLength` (256). The host logs
  a warning and ignores the issue.
- **The host adds its own.** When an integration fails or times out during start-up, the host shows an
  `Error` issue with a retry button for it, next to yours. Its id is reserved: an issue of yours with the
  same id is hidden.
- **A throwing `GetIssuesAsync` counts as no issues.** The host logs the exception; your badge simply
  vanishes. Return `[]` rather than throwing.
- **Disabled integrations are never asked.**

## Testing

```csharp
var issues = await harness.Issues.GetIssuesAsync();
var resolved = await harness.Issues.ResolveAsync(new IssueResolveArguments { IssueId = "wrong-endpoint" });
```

`PluginTestHarness.Issues` calls `list` and `resolve` the way the host does; the results are
`IssueListResult` and `IssueResolveResult`. See [testing](/features/testing/).

## Over the plugin protocol

Issues are one provider-shaped `issues` capability per plugin. `list` and `resolve` are always live
round trips - there is no snapshot - so an out-of-process `GetIssuesAsync` is called over the WebSocket
on every poll, which is one more reason to keep it cheap. Severity and follow-up travel as their enum
member names. See [capability parity](/reference/capability-parity/) and
[the WebSocket reference](/reference/websocket/#capabilities).

## See also

- [Setup flows](/features/setup-flows/) - where `StartConfigFlow` sends the user.
- [Logging](/features/logging/) - for diagnostic detail.
- [Localization](/features/localization/#the-generated-api) - where `Strings.*` comes from.
