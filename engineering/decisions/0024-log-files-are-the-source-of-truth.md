# ADR 0024: The log files are the log viewer's source of truth, and their format is a contract

Status: Accepted

## Context

The same log history was kept three times: Serilog's rolling files, a 2,000-entry in-host buffer, and up
to 5,000 entries in the UI process. Both in-memory copies existed only to serve the viewer and were
strictly worse than the files they duplicated — they capped how far back a user could look at a fraction
of the retained history, they were lost on restart, and the bootstrapper's entries had to be read back
out of *its* file into the buffer anyway, because it is a separate process.

The files could not simply be read instead: the line format carried no source, no integration id and no
category, so none of the filtering the viewer offers could be reconstructed from it.

Out-of-process plugins compound this. They cannot write to the host's Serilog pipeline, but their logs
belong in the same viewer and files as an in-process integration's — and plugin-originated log data is
untrusted, so it must not be able to spoof another integration's identity or overload protocol-critical
queues.

## Decision

**The rolling log files are the only log history.** `LogFileReader` pages backwards through them on
demand and `LogTailBackgroundService` tails the active ones for live updates, so the history and the
live stream read the same bytes and cannot disagree. The in-memory buffer, its sink, the broadcast
service and the bootstrapper log tail are gone.

The host's line format gains an attribution segment:

```
2026-08-06 10:11:12.345 +02:00 [WRN] [Integration/app.macro-deck.spotify/SpotifyClient] message
```

Plain text rather than CLEF, because these files are read by people, attached to bug reports and
grepped, and that is worth more than a parser's convenience.

**The format is now a contract between two processes and two languages.** The Rust bootstrapper writes
its own files in its own format and the .NET host parses both sets, so changing either writer breaks the
viewer of the other. `HostLogParser` and `BootstrapperLogParser` are the only readers and both have
round-trip tests against lines the real writers produce.

Several rules exist to keep parsing unambiguous rather than merely usual:

- Origin segments are sanitized to `[A-Za-z0-9._-]`. An integration id is arbitrary third-party text,
  and this is what stops redaction swallowing the closing bracket of an id containing `:` or `=`.
- Newlines inside a rendered message are escaped on the way out. Once the files are authoritative, a
  message containing a newline could otherwise forge a second, fully attributed entry — and this is what
  makes "a line that does not parse as a header continues the entry above it" exactly true.
- Paging cursors and the live stream's read positions are **byte offsets into a named file**, never
  timestamps, and are opaque to the client. In-file timestamps are not monotonic — DST fall-back, a
  clock correction, or two processes sharing a file all break that — so any ordering derived from them
  would silently skip or repeat entries.

Redaction happens once per event, before the event reaches the file sink or any other sink, so what the
viewer shows is what every other output shows. Filtering by level, source, integration and category runs
on the host while it scans: a page of 200 raw lines filtered client-side would show a handful of matches
and hide the rest behind pages the user has to load by hand.

**Plugin logs are re-emitted into the same pipeline.** `MacroDeck.Plugin.Serilog` forwards plugin log
events over the plugin protocol and the host rebuilds accepted events into its existing Serilog pipeline
rather than maintaining a second plugin-specific subsystem.

- The host derives integration identity from the **authenticated plugin session**; plugin-supplied
  properties cannot override attribution or other reserved host properties.
- The rendered message is treated as literal text, never reparsed as a host-side message template.
- Inbound plugin logs use a dedicated bounded lane, so a log flood may be dropped without terminating
  the session or starving capability traffic.
- Rate limiting is keyed by plugin identity rather than by session, so reconnecting is not a bypass.
- Structured properties are flattened to what the host's line format can preserve; the protocol does not
  introduce a richer persistent log format only for plugins.

## Consequences

- The viewer reaches back through every retained day instead of the newest 2,000 entries, and reopening
  it reconstructs from disk.
- An idle install does no log-viewer work at all: with no subscriber, nothing is read, parsed or sent.
- Entries written by a previous version parse without an origin segment and are attributed to the host.
  The alternative is a viewer that looks empty for the fortnight those files are retained.
- Serilog's file sink stops writing at its 1 GB default rather than rolling. That failure mode is now
  the viewer's failure mode too.
- Live entries reach an open viewer within about a second: the tail polls and holds the newest entry
  back one read, so a stack trace is never pushed as a bare header with its detail still being written.
- A plugin cannot attribute its logs to another integration, and reconnecting does not reset its budget.

## References

- [Issue #414](https://github.com/Macro-Deck-App/Macro-Deck/issues/414)
- [Logging guide](https://docs.macro-deck.app/features/logging/)
