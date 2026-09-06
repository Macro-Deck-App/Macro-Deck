---
title: Logging and health
description: Forwarding a plugin's Serilog output into the host's log viewer, what the health endpoints answer, and how the two relate to a supervised plugin's lifecycle.
---

Two things a plugin does that a user eventually looks at: it logs, and it answers a health probe. This
page covers both, plus the one shutdown detail that follows from the health probe.

## Logging

`MacroDeck.Plugin.Serilog` forwards a plugin's own logs to the host, so they appear next to the host's
own entries in its log viewer - without changing how you log.

```csharp
var plugin = MacroDeckPlugin.CreatePlugin(args)
    .UseMacroDeckLogging()
    .RegisterIntegration<MyIntegration>()
    .Build();

await plugin.RunAsync();
```

`UseMacroDeckLogging` calls Serilog's own `UseSerilog` under the hood, so an injected `ILogger<T>`, the
static `Log` APIs and `MacroDeck.Sdk.Logging.IntegrationLog` all route through it automatically.
Calling it more than once is safe - the second call is a no-op.

Configure the pipeline the normal way with the optional callback. It runs **before** the Macro Deck
sink is attached, so every other Serilog feature works exactly as it would without this package:

```csharp
.UseMacroDeckLogging(cfg => cfg
    .MinimumLevel.Debug()
    .Enrich.WithMachineName()
    .WriteTo.File("plugin.log"))
```

### Options

Bound from the `MacroDeck:Plugin:Logging` configuration section:

| Option | Default | What it does |
| --- | --- | --- |
| `MinimumLevel` | `Information` | The minimum level forwarded **to the host**. Independent of the pipeline's own minimum level, so a lower pipeline minimum can still reach a local file sink without also reaching the host. |
| `BatchSize` | 64 | How many events one `log.publish` batch tries to carry. Capped again at send time by the protocol's own limit - this only lets you ship smaller batches more often, never larger ones. |
| `FlushInterval` | 2 seconds | How often a partial batch is flushed even when it never fills up. |
| `QueueCapacity` | 2000 | Total capacity across both internal queues combined. |
| `EnableFallbackFile` | `true` | Whether undelivered batches are tee'd to a small local file while the host is unreachable. |
| `FallbackFileMaxBytes` | 1 MiB | Hard cap on that file. |

### Behaviour worth knowing

- **Warnings, errors and fatals are never dropped in favour of informational noise.** Events queue on
  two separate bounded channels - one for `Warning` and above, one for everything else - so a flood of
  `Information` lines cannot push a queued `Error` out. Shipping order is still the order they were
  logged; the split only decides what gets dropped under pressure.
- **Nothing blocks the caller.** A `logger.Information(...)` call returns immediately even when fully
  disconnected from the host. Excess events past the queue capacity are dropped, not buffered without
  bound and not waited on.
- **`log.publish` has no acknowledgement.** There is no retry queue and no replay: a batch the host
  never received is gone. What survives a lost connection is a small bounded local file
  (`plugin-fallback.log` under the plugin's state directory) as a diagnostic tail of what could not be
  delivered. It is **never replayed** into the host, so do not expect an outage's logs to reappear when
  the connection comes back. The file wraps rather than freezing when full, so a long outage ends with
  the most recent entries on disk.

### What the host does with a forwarded event

The host rebuilds each event as a real Serilog `LogEvent` and writes it into its own existing pipeline.
From that point on it is indistinguishable from an in-process integration's entry: the same redaction,
the same rolling file sink, the same log viewer filters. There is no separate plugin log file, and
"filter by plugin" and "filter by integration" are the same filter. See
[ADR 0024](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0024-log-files-are-the-source-of-truth.md).

Three consequences a plugin author should know:

- **Identity is session-derived, never plugin-asserted.** The host stamps the integration id from the
  plugin id the WebSocket authenticated as, after filtering every plugin-supplied property that would
  collide with it. Nothing identifying the plugin travels on the wire at all - not the plugin id, not
  the version, not the process id - because the host already has all of it. You cannot forge the
  identity your entries are attributed under, and you do not need to add it yourself.
- **The rendered message is carried as literal text, never re-parsed.** An embedded `{Foo}` in your
  rendered output stays literal on the host side rather than being interpreted as a hole. The original
  template survives verbatim as a separate property, but it is never what the host re-renders.
- **Structured properties do not survive into the log files.** `properties` is a flat string-to-string
  map, pre-rendered by your sink, because the host's on-disk line format never persists structured
  properties for *any* line, host or plugin. Useful to a live reader; not something to design a feature
  around.

### Ingestion limits

The host bounds `log.publish` independently of what a plugin sends. The values today:

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

The rate limiter is keyed **per plugin id, not per session** - deliberately, because a per-session
bucket would be reset by reconnecting, which is the one thing a plugin fully controls. The accepted
trade-off: a plugin that crashes and restarts inherits whatever budget its predecessor left.

The inbound log queue is a separate channel from the capability queue, and it drops excess writes
rather than closing the session. A log burst is usually the plugin trying to say something is wrong;
closing the session over it would destroy the evidence exactly when it is most useful.

## Health

The SDK owns four routes inside your plugin's own web application, under the reserved `/_macrodeck`
prefix:

| Route | Answers |
| --- | --- |
| `GET /_macrodeck/health` | Liveness, as soon as the process serves. |
| `GET /_macrodeck/ready` | 200 once a session is open, 503 before. |
| `GET /_macrodeck/info` | Id, name, version, registration mode, protocol version. |
| `GET /_macrodeck/diagnostics` | Connection state, reconnect attempt, in-flight invocations. |

You do not implement these. Mapping your own route under `/_macrodeck` fails `Build()`, and middleware
that tries to answer one never sees the request - reserving the prefix is what lets the SDK add
endpoints later without breaking a plugin that picked the same path.

`/_macrodeck/health` is what the supervisor probes. It answers from the moment the process serves,
independently of whether the plugin has finished starting up or opened a session - which is why it is
liveness and `/_macrodeck/ready` is the one that waits for a session.

### Tuning the probe

From `manifest.json`, all clamped rather than rejected:

| Setting | Default | Clamped to |
| --- | --- | --- |
| `health.path` | `/_macrodeck/health` | — |
| `health.intervalSeconds` | 15 | 5–120 |
| `health.timeoutSeconds` | 2 | 1–10 |
| `health.unhealthyThreshold` | 3 | 2–10 |

The floor of 2 on `unhealthyThreshold` means a single missed probe can never restart a plugin. That is
a structural property of the manifest format, not something you can configure away. See
[the manifest reference](/reference/manifest/#timing-settings).

### Never override your own listener URL

The supervisor binds a loopback port itself, *before* your process starts, and hands it to you as
`ASPNETCORE_URLS`. Overriding it in your own configuration - `appsettings.json`, a launch profile, or
`UseUrls` in code - makes your plugin listen somewhere the supervisor is not probing, and the health
check then silently and permanently reads as failing. There is no error surfaced for this: from the
supervisor's side, a wrong port and a hung process look identical.

The analyzer catches the statically visible cases as
[MDP4002](/sdk/analyzers/#mdp4002). Let the host choose the port.

### When health matters

Two moments in particular:

- **Activation.** Unless the caller opted out of starting the plugin, activating a version starts it
  and waits for it to reach a healthy running state. If it does not, activation is undone: the previous
  version is restored, the failed version is deleted, and the previously running version is restarted.
- **Steady state.** Consecutive probe failures past `unhealthyThreshold` mark the plugin unhealthy and
  the supervisor restarts it, within a restart budget - a plugin that keeps failing ends up in a
  terminal failed state rather than restarting forever.

## Shutdown

The authoritative account is
[what to expect at shutdown](/sdk/hosting/#what-to-expect-at-shutdown), which covers the sequence -
`session.goodbye`, a `4004` close, the manifest's grace period, then a process-tree kill - and what the
SDK's managed-mode default already does for you. Read it there rather than here.

Two things that section leaves to this page:

**Flush before you exit, or accept the loss.** `log.publish` is fire-and-forget with no
acknowledgement, and the shipper flushes on an interval. Anything logged in the last moments before the
process exits may never reach the host. If a final message matters - why a plugin is giving up, what
state it was in - log it early enough for a flush to catch it, and remember the fallback file is a
diagnostic tail on disk, not something that gets replayed.

**A hard kill takes the logs with it.** If your plugin hangs past the grace period, the process tree is
killed and nothing queued is delivered. That is another reason not to do slow work in
`ShutdownAsync`: stop accepting new work, drain what is bounded, and release.

## See also

- [Plugin hosting](/sdk/hosting/) - the builder, the reserved routes, and the full shutdown
  sequence.
- [Manifest reference](/reference/manifest/) - the `health` and `shutdown` blocks.
- [WebSocket reference](/reference/websocket/#events-logs-and-state) - the `log.publish` payload shape.
- [Analyzers](/sdk/analyzers/) - MDP4002 and the rest.
- [Troubleshooting](/guides/troubleshooting/) - when the health probe or the logs are not doing what
  you expect.
