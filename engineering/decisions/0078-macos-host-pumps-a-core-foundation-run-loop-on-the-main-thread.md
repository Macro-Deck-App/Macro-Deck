# ADR 0078: The macOS host pumps a CoreFoundation run loop on the main thread

Status: Accepted

## Context

Folder switching on application focus polled the focused application once a second (issue #810), which
users experience as a delay of up to a second after every application switch. The fix is to consume
the platform's own focus notifications instead of polling.

On macOS that notification is `NSWorkspaceDidActivateApplicationNotification`, delivered through
`NSWorkspace`'s notification centre. `MacroDeckHost` is a headless child process of the Tauri
bootstrapper ([ADR 0006](0006-tauri-bootstrapper-is-the-installed-entry-point.md)): it has no `NSApplication`,
no window, and until now nothing that ran a CoreFoundation run loop. Its main thread simply blocked
on the host task.

Measured in a spike against a headless .NET process, notification delivery depends entirely on which
thread runs the run loop:

| CFRunLoop location | activations delivered |
|---|---|
| dedicated background thread, even with a keep-alive `CFRunLoopSource` | 0 of 8 |
| process main thread | 8 of 8 |
| observer registered on a background thread, main thread pumping | 8 of 8, callback on the main thread |

`NSWorkspace` registers its mach port on the main run loop, so a background run loop never sees the
source. Registration may happen anywhere; only the pumping thread matters. There is no supported way
to move that registration, and the alternatives to a run loop — an `AXObserver`, which needs
Accessibility permission that focus rules do not otherwise require, or a private CoreGraphics
notification API — are worse on permissions or on support guarantees.

## Decision

The macOS entry point runs the host on the thread pool and pumps a CoreFoundation run loop on the
process main thread for the lifetime of the host. `Program.Main` is synchronous; the previous
`Main` body is unchanged apart from its name.

- `MacOsMainRunLoop.PumpUntil` loops `CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.25, false)` while
  the host task is incomplete. Bounded slices rather than `CFRunLoopRun()` plus a cross-thread
  `CFRunLoopStop`, because slices cannot race a host task that completes before the loop is entered.
  A finite timeout installs its own timer, so the mode is never empty and the call blocks for the
  full slice: measured at 0.1 % of one core with no source and no observer registered, and 0.4 %
  while delivering activations. A return of `kCFRunLoopRunFinished` still sleeps out the remainder of
  the slice, so a future change in that behaviour cannot turn the pump into a busy loop.
- The pump is marked active on the main thread *before* the host task starts, and the watcher factory
  reads that flag. Marking it from inside the pump would race the host task, and losing that race
  would silently downgrade macOS to polling for the whole process lifetime.
- Any macOS run in which the pump was not started falls back to polling rather than losing focus
  detection, and the chosen watcher is logged.

Nothing in the relocated startup body has thread affinity: there is no `[STAThread]`, no
`Thread.CurrentThread`, `ProcessDpiAwareness.Configure` sets process-wide awareness and is a no-op off
Windows, the staged-restore lock is a `FileStream` rather than a named mutex, `Environment.ExitCode`
is process-global so the restart exit code still reaches the bootstrapper, and the generic host's
console lifetime uses `PosixSignalRegistration`, which does not need a run loop. Everything after the
first `await` already ran on the thread pool.

## Consequences

- The main thread is now a shared resource with a defined owner. Future macOS work needing main-thread
  affinity — a status item, global hotkeys, media-remote or AVFoundation callbacks — has somewhere to
  run, and must schedule onto this run loop rather than starting a second one.
- Anything dispatched to the main run loop now executes. Main-queue blocks that framework code
  (Keychain, IOKit, CoreAudio) previously queued and that never ran will start running.
- Work performed in a run-loop callback blocks focus delivery and every other main-thread source, so
  callbacks must hand off rather than compute. The focus observer writes to a channel and returns.
- Shutdown gains up to one slice (250 ms) of latency after the host task completes.
- The pump exists only on macOS; Windows and Linux keep the previous entry-point behaviour, so this
  is not a general "the host owns a UI thread" decision.

## References

- Issue [#810](https://github.com/Macro-Deck-App/Macro-Deck/issues/810).
- [ADR 0006](0006-tauri-bootstrapper-is-the-installed-entry-point.md) — the bootstrapper owns the window and
  starts the host as a child process.
