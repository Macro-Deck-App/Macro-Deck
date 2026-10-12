---
title: Integration storage
description: Keep small text documents of your own across restarts with IIntegrationContext.Storage - keys, atomic writes, validators, unreadable documents, where the files live, backups, older hosts and testing.
---

`IIntegrationContext.Storage` keeps small text documents an integration owns, such as the devices it has
seen or a cache it wants back after a restart. Each document has a key; you decide its format, usually JSON.
Settings the user enters belong in [configuration entries](/features/setup-flows/) instead, and secrets in
their secret fields.

```csharp
private const string DevicesKey = "known-devices.json";

public async Task InitializeAsync(IIntegrationContext context)
{
	var read = await context.Storage.ReadAsync(DevicesKey, IsDeviceList);
	_canSave = read.Status != IntegrationStorageReadStatus.Unreadable;
	_devices = read.Content is { } json ? JsonSerializer.Deserialize<List<Device>>(json)! : [];
	_storage = context.Storage;
}

private async Task SaveAsync()
{
	if (_canSave)
	{
		await _storage.WriteAsync(DevicesKey, JsonSerializer.Serialize(_devices));
	}
}

private static bool IsDeviceList(string json)
{
	try
	{
		return JsonSerializer.Deserialize<List<Device>>(json) is not null;
	}
	catch (JsonException)
	{
		return false;
	}
}
```

## Keys

A key is 1 to 64 characters of lowercase ASCII letters, digits, `.`, `_` and `-`, starts with a letter or a
digit, and does not end in `.tmp` or `.bak`, which storage uses for its own copies. Check one with
`IntegrationStorageKeys.IsValid`; every operation throws `ArgumentException` for an invalid key. Keys are
private to your integration: another integration's `devices.json` is a different document.

## Reading and writing

| Call | What it does |
| --- | --- |
| `ReadAsync(key, isValid)` | Answers an `IntegrationStorageReadResult`: `Found` with the `Content`, `Missing` when nothing was ever stored, or `Unreadable`. |
| `WriteAsync(key, content)` | Replaces the document. `content` must not be empty; use `DeleteAsync` to remove a document. |
| `DeleteAsync(key)` | Removes the document and its copies. A missing key is not an error. |

A write never leaves a half-written document behind. It writes a temporary copy, flushes it to disk, keeps
the previous document as a backup and only then puts the new one in place. A read tries the document, then
a pending temporary copy, then the backup, and answers the first one `isValid` accepts. Without a validator
every copy that is not empty counts, so pass one whenever you can tell a damaged document apart, and keep it
free of side effects: a validator that throws rejects that copy.

`Unreadable` means copies exist but none is valid, for example after a disk error or an edit by hand. Treat
it as "do not touch": keep working with defaults, but do not write the key again in that session, or the
next write replaces the only copies the user could still recover. Storage itself never deletes or repairs
them.

## Where the documents live

- **Built into Macro Deck**: `integration-data/<integration id>/` in Macro Deck's data directory. The folder
  is part of the **Integrations** component of a [backup](/guide/backups/), so restoring it brings the
  documents back together with the integration's configuration.
- **In a plugin**: `integration-data/<plugin id>/` in the data directory the host gives the plugin
  (`PluginHostOptions.DataDirectory`, see [plugin hosting](/reference/plugin-hosting/#iintegrationcontext)).
  It survives plugin updates and rollbacks. Storage never crosses the plugin protocol, so it works the same
  on every Macro Deck version. A plugin started without a data directory, such as one run outside the
  supervisor without `MACRO_DECK_PLUGIN_DATA_DIRECTORY`, reads every key as `Missing`, and a write throws
  `InvalidOperationException`.

Documents are files, not a database: keep them small, a few hundred kilobytes at most, and write them when
something changed rather than on a timer. Calls on the same integration's storage run one at a time.

## Older hosts and other implementations

`Storage` is a default interface member. A context from a Macro Deck that predates it, and any
`IIntegrationContext` implementation that does not override it, return `UnavailableIntegrationStorage`:
every read is `Missing`, a delete does nothing and a write throws `InvalidOperationException`. Catch that
exception where saving is optional.

## Testing

`FakeIntegrationContext.Storage` is an in-memory `FakeIntegrationStorage` with the same key rules and read
outcomes. Seed a document with `Set`, make a key hold damaged copies with `SetUnreadable`, and read what the
integration wrote with `Get`:

```csharp
await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration<MyIntegration>());
harness.Context.Storage.SetUnreadable("known-devices.json");

await harness.InitializeIntegrationsAsync();

Assert.That(harness.Context.Storage.Get("known-devices.json"), Is.Null);
```

## See also

- [Setup flows](/features/setup-flows/) - settings and secrets the user enters.
- [Plugin hosting](/reference/plugin-hosting/) - the plugin data directory.
- [Capability parity](/reference/capability-parity/) - how storage differs between built-in integrations and plugins.
