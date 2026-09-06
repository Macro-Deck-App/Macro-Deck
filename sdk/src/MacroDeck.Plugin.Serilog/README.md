# MacroDeck.Plugin.Serilog

Forwards a [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck) plugin's own logs to the host,
so they show up next to the host's own entries in its log viewer - without changing how you log.

```csharp
var plugin = MacroDeckPlugin.CreatePlugin(args)
    .UseMacroDeckLogging()
    .RegisterIntegration<MyIntegration>()
    .Build();

await plugin.RunAsync();
```

`UseMacroDeckLogging` calls Serilog's own `UseSerilog` under the hood, so an injected Serilog `ILogger`,
the static `Log` APIs and `MacroDeck.Sdk.Logging.IntegrationLog` all route through it automatically. Configure the pipeline the
normal way with the optional callback - it runs before the Macro Deck sink is attached, so every other
Serilog feature still works:

```csharp
.UseMacroDeckLogging(cfg => cfg
    .MinimumLevel.Debug()
    .Enrich.WithMachineName()
    .WriteTo.File("plugin.log"))
```

Calling `UseMacroDeckLogging` more than once is safe - the second call is a no-op.

## Behaviour worth knowing

- **Warnings, errors and fatals are never dropped in favor of informational noise.** Events queue on
  two separate bounded channels, one for `Warning` and above and one for everything else, so a flood of
  `Information` lines cannot push a queued `Error` out. Events are still shipped in the order they were
  logged - the split only decides what gets dropped under pressure, not what order the host sees things
  in.
- **Nothing here blocks the caller.** A `logger.Information(...)` call always returns immediately, even
  fully disconnected from the host - excess events past the queue capacity are dropped, not buffered
  without bound and not waited on.
- **`log.publish` has no acknowledgement**, matching the wire protocol's fire-and-forget contract for
  that message type. There is no retry queue and no replay: a batch the host never received is gone.
  What survives a lost connection is a small bounded local file (`plugin-fallback.log` under the
  plugin's state directory) as a diagnostic tail of what could not be delivered - it is never replayed
  into the host, so do not expect an outage's logs to reappear once the connection comes back.
- Configure batching and the fallback file from the `MacroDeck:Plugin:Logging` configuration section,
  bound to `MacroDeckLoggingOptions`.

Licensed under Apache-2.0.
