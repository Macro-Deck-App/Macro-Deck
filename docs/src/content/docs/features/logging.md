---
title: Logging and health
description: Log with Serilog or ILogger and read the lines in the host's log files and log viewer; what the health endpoints answer and what happens at shutdown.
---

A plugin logs the normal .NET way. `UseMacroDeckLogging()` forwards every line to the host, where it
lands in the same log files and log viewer as the host's own entries.

## Quick start

```csharp
// Program.cs
var plugin = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.UseLocalization(Strings.LocalizationCatalog)
	.RegisterIntegration<PluginIntegration>()
	.Build();

await plugin.RunAsync();

// PluginIntegration.cs
using Serilog;

public sealed class PluginIntegration(ILogger logger) : IPluginIntegration
{
	private readonly ILogger _logger = logger.ForContext<PluginIntegration>();

	public IReadOnlyList<IActionDefinition> Actions => [];

	public async Task InitializeAsync(IIntegrationContext context)
	{
		try
		{
			var bridge = await HueBridge.ConnectAsync();
			_logger.Information("Connected to {Bridge} with {LightCount} lights", bridge.Name, bridge.Lights.Count);
		}
		catch (HttpRequestException exception)
		{
			_logger.Warning(exception, "Bridge not reachable, retrying in the background");
		}
	}

	public Task ShutdownAsync() => Task.CompletedTask;
}
```

Both lines appear in the host's log file and in the **Logs** tab of the Developer page, attributed to
your plugin, within a few seconds (the plugin flushes every 2 seconds, the viewer tails the file).

- **Any logger works.** An injected Serilog `ILogger`, `ILogger<T>`, the static `Log` API and
  `MacroDeck.Sdk.Logging.IntegrationLog` all route through the same pipeline. The project template
  already calls `UseMacroDeckLogging()`.
- **The host's log files are the only log history.** There is no separate plugin log file.
- **What a user can send you** is the day's log file (see [where the lines end up](#where-the-lines-end-up))
  or entries copied from the log viewer, filtered to your plugin.
- **Lines also go to the console**, so they show in your IDE's run window and in
  [`macrodeck-plugin run`](/cli/run/). Do not add `WriteTo.Console(...)`: every line would print twice.

## Where the lines end up

```text
2026-08-06 10:11:12.345 +02:00 [WRN] [Integration/app.macro-deck.spotify/SpotifyClient] message
```

The host rebuilds each forwarded event as a Serilog event and writes it into its own pipeline: the same
redaction, the same rolling file, the same viewer filters as an in-process integration. "Filter by
plugin" and "filter by integration" are the same filter.

| | Location |
| --- | --- |
| Host log files | `<data directory>/logs/host-<date>.log`, one file per day, 14 kept |
| Data directory, Windows | `%APPDATA%\MacroDeck` |
| Data directory, macOS | `~/Library/Application Support/MacroDeck` |
| Data directory, Linux | `$XDG_DATA_HOME/MacroDeck`, or `~/.local/share/MacroDeck` |
| Log viewer | Developer page, **Logs** tab: every retained day, filterable by level, source and integration |

Newlines inside a message are escaped, so one entry is always one header line.

## Levels and what goes where

```json
{
  "MacroDeck": {
    "Plugin": {
      "Logging": { "MinimumLevel": "Debug" }
    }
  }
}
```

`MinimumLevel` (in `appsettings.json` or any other configuration source) decides what is forwarded
**to the host**. It is independent of the pipeline's own minimum, so a `Debug` line can reach a local file
sink without reaching the host:

```csharp
.UseMacroDeckLogging(cfg => cfg
	.MinimumLevel.Debug()
	.WriteTo.File("plugin.log"))
```

The callback runs **before** the Macro Deck sink is attached, so every Serilog feature works as usual.
Calling `UseMacroDeckLogging` twice is a no-op.

| Level | Use it for |
| --- | --- |
| `Verbose`, `Debug` | Detail for you while developing. Not forwarded by default. |
| `Information` | Things a user would want to see happened: connected, configured, reloaded. |
| `Warning` | Something failed and you recovered or will retry. |
| `Error`, `Fatal` | An operation failed for good, or the plugin is giving up. |

`Warning` and above are never dropped to make room for informational noise.

## Structured properties

```csharp
_logger.Information("Set {Light} to {Brightness} %", light, brightness);
```

Use message templates, not string interpolation: the host shows the rendered message and a live reader
can still filter on properties. Know the limits:

- Properties travel as a flat string-to-string map and **are not written to the log files**, for host and
  plugin lines alike. Do not build a feature on them.
- The rendered message is literal text. A `{Foo}` inside it is never re-parsed on the host.
- Identity is added by the host from the authenticated session. Nothing identifying your plugin is sent,
  and a plugin-supplied property that would collide with it is filtered out, so you cannot log under
  another integration's name.

## Never log secrets

```csharp
_logger.Information("Authenticated as {User}", account.DisplayName); // not the token
```

The host redacts every event once, before any sink, to `***`: values of sensitive keys (`password`,
`token`, `secret`, `api_key`, `authorization`, `client_secret` and similar) in `key=value` or `"key": value`
form, credentials in URLs (`https://user:pass@`), `Bearer`/`Basic`/`Digest` credentials, JWTs and PEM
private keys. That is a safety net, not a licence: a secret in an unrecognised shape is written as-is into
a file users attach to bug reports.

## When the host is unreachable

Logging never blocks. A log call returns immediately, even with no connection.

- `log.publish` has **no acknowledgement and no replay**. A batch the host never received is gone.
- While the host is unreachable, undelivered batches are written to
  `<state directory>/<plugin id>/logs/plugin-fallback.log`. It wraps when full, so a long outage ends with
  the newest entries, and is cleared once the connection works again. It is never sent to the host.
- The default state directory is `%LOCALAPPDATA%\MacroDeck\plugins` on Windows,
  `~/Library/Application Support/MacroDeck/plugins` on macOS and `$XDG_STATE_HOME/macro-deck/plugins`
  (or `~/.local/state/macro-deck/plugins`) on Linux.
- Past the queue capacity, events are dropped, not buffered without bound.

### Options

Bound from `MacroDeck:Plugin:Logging`:

| Option | Default | What it does |
| --- | --- | --- |
| `MinimumLevel` | `Information` | Minimum level forwarded to the host. |
| `BatchSize` | 64 | Events per `log.publish` batch. Capped at the protocol limit, so only smaller works. |
| `FlushInterval` | 2 seconds | How often a partial batch is sent. |
| `QueueCapacity` | 2000 | Total across the two internal queues (warning and above, everything else). |
| `EnableFallbackFile` | `true` | Write undelivered batches to the fallback file. |
| `FallbackFileMaxBytes` | 1 MiB | Hard cap on that file. |

### Ingestion limits

The host bounds `log.publish` whatever the plugin sends:

| Limit | Value |
| --- | --- |
| Events per batch | 64 |
| Message length | 4096 characters |
| Properties per event | 32 |
| Property name / value length | 64 / 512 characters |
| Source context length | 128 characters |
| Exception length / nesting depth | 8192 characters / 5 |
| Inbound log queue depth | 256 |
| Events per second, and burst | 20, with a burst of 500 |

The rate limit is keyed **per plugin id, not per session**: reconnecting does not reset it, and a
plugin that crashes and restarts inherits the budget its predecessor left. Logs use their own inbound
queue, separate from capability traffic; excess events are dropped without closing the session.

## Health

```bash
curl http://127.0.0.1:<port>/_macrodeck/ready
```

The SDK serves four routes in your plugin's web application. You do not implement them.

| Route | Answers |
| --- | --- |
| `GET /_macrodeck/health` | Liveness, from the moment the process serves - before startup finishes or a session opens. |
| `GET /_macrodeck/ready` | 200 once a session is open, 503 before. |
| `GET /_macrodeck/info` | Id, name, version, registration mode, protocol version. |
| `GET /_macrodeck/diagnostics` | Connection state, reconnect attempt, in-flight invocations. |

`/_macrodeck` is reserved: mapping your own route under it fails `Build()`, and middleware that tries to
answer one never sees the request. The supervisor probes `/_macrodeck/health`.

### Tuning the probe

In [`manifest.json`](/reference/manifest/#timing-settings); out-of-range values are clamped, not rejected:

| Setting | Default | Clamped to |
| --- | --- | --- |
| `health.path` | `/_macrodeck/health` | - |
| `health.intervalSeconds` | 15 | 5-120 |
| `health.timeoutSeconds` | 2 | 1-10 |
| `health.unhealthyThreshold` | 3 | 2-10 |

The floor of 2 means a single missed probe never restarts a plugin.

### Never override your own listener URL

The supervisor binds a loopback port **before** your process starts and passes it as `ASPNETCORE_URLS`.
Setting your own URL in `appsettings.json`, a launch profile or `UseUrls` makes the plugin listen where
nobody probes: the health check fails silently and permanently, because a wrong port and a hung process
look the same. [MDP4002](/reference/analyzers/#mdp4002) catches the statically visible cases.

### When health matters

- **Activation.** Unless the caller opted out of starting it, activating a version starts the plugin and
  waits for it to be healthy. If it is not, activation is undone: the previous version is restored, the
  failed one deleted, and the previous one restarted.
- **Steady state.** Consecutive failures past `unhealthyThreshold` mark the plugin unhealthy and the
  supervisor restarts it, within a restart budget. A plugin that keeps failing ends in a terminal failed
  state rather than restarting forever.

## Shutdown

The sequence - `session.goodbye`, a `4004` close, the manifest's grace period, then a process-tree kill -
is in [what to expect at shutdown](/reference/plugin-hosting/#what-to-expect-at-shutdown). For logging it
means:

- **Log final messages early.** The shipper flushes on an interval and `log.publish` has no
  acknowledgement, so a line written just before exit may never arrive. The fallback file is not replayed.
- **A hard kill takes queued logs with it.** Keep `ShutdownAsync` fast: stop accepting work, drain what
  is bounded, release.

## See also

- [Testing](/features/testing/) - assert on log lines with `harness.Logs`.
- [Plugin hosting](/reference/plugin-hosting/) - the builder, the reserved routes, the shutdown sequence.
- [Manifest reference](/reference/manifest/) - the `health` and `shutdown` blocks.
- [WebSocket reference](/reference/websocket/#events-logs-and-state) - the `log.publish` payload.
- [Troubleshooting](/guides/troubleshooting/) - when the probe or the logs do not do what you expect.
