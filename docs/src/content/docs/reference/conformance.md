---
title: Conformance suite
description: The fixed set of protocol-level checks any plugin can be run against, in any language, without a Macro Deck installation.
---

The conformance suite is 49 protocol-level checks in 8 categories. It runs against any plugin, in any language, with no Macro Deck installation: it lives in `MacroDeck.Plugin.Testing.Conformance`, on the same `MacroDeckTestHost` as [Testing plugins](/features/testing/), and produces one report with no test-framework types in it.

## Example

The plugin the template generates, run with no test project:

```bash
macrodeck-plugin new --name "Hello Deck" --id com.example.hello-deck --publisher Example -y
cd HelloDeck
macrodeck-plugin test --project src/HelloDeck
```

```text
Macro Deck plugin conformance report (suite 1.2.0)
Plugin: com.example.hello-deck 1.0.0
Started: 2026-09-12T10:27:14.6935280+00:00, duration: 00:00:20.8027253
Passed: 25, Failed: 0, Skipped: 24
Conformant: yes

[PASS] MDC0101 The plugin id is a valid reverse-domain package id (Required)
[PASS] MDC0102 Every declared capability's local id is a valid declared-kind identifier (Required)
[SKIP] MDC0103 Every weather station instance id is a valid resource-kind identifier (Required)
    Reason:   This subject does not declare the weather capability.
[SKIP] MDC0104 The manifest declares a supported manifest version and a protocol range this suite satisfies (Required)
    Reason:   This subject has no manifest - only an artifact subject does.
...
[SKIP] MDC0502 A deadline that elapses produces TIMEOUT, and nothing arrives afterward (Recommended)
    Reason:   No declared action ran long enough, under a 300 ms deadline, to observe deadline enforcement.
...
[PASS] MDC0804 Reconnecting does not replay a burst of previously published events (Required)
```

Skips are normal: a check that has nothing to exercise says so and why. Run a packed artifact (`--artifact`) to also cover the manifest checks.

## Running it

| From | How |
| --- | --- |
| CLI | `macrodeck-plugin test` with `--project`, `--executable` or `--artifact`. Filter with `--category`, `--check` (both repeatable) and `--required-only`; `--list-checks` prints the vocabulary. See [`test`](/cli/test/). |
| Your tests | `ConformanceRunner` plus a `ConformanceSubject`, below. Neither depends on a test framework. |

### In a test project

One NUnit test per check, so each failure is reported under its own id:

```csharp
using MacroDeck.Plugin.Testing.Conformance;
using NUnit.Framework;

[TestFixture]
public sealed class ConformanceTests
{
    private static readonly ConformanceRunner Runner = new();
    private static ConformanceReport? _report;

    public static IEnumerable<IConformanceCheck> Checks => Runner.Checks;

    [OneTimeSetUp]
    public async Task RunSuite()
    {
        await using var subject = ConformanceSubject.InProcess(builder => builder
            .UseLocalization(Strings.LocalizationCatalog)
            .RegisterIntegration<PluginIntegration>());

        _report = await Runner.RunAsync(subject);
    }

    [TestCaseSource(nameof(Checks))]
    public void Check(IConformanceCheck check)
    {
        var result = _report!.Results.Single(r => r.Id == check.Id).Result;

        if (result.Outcome is ConformanceOutcome.Skipped or ConformanceOutcome.Inconclusive)
        {
            Assert.Ignore(result.SkipReason);
        }

        if (check.Requirement == ConformanceRequirement.Required)
        {
            Assert.That(result.Outcome, Is.EqualTo(ConformanceOutcome.Passed),
                $"Expected: {result.Expected}\nActual: {result.Actual}");
        }
    }
}
```

Under xUnit, use `[Theory]`/`[MemberData]` and `Assert.Skip`/`Assert.True`; the runner and subject are unchanged.

| Subject factory | Runs |
| --- | --- |
| `ConformanceSubject.InProcess(Action<PluginHostBuilder> configure, PluginTestManifest? manifest = null)` | The same delegate you give `MacroDeckPlugin.CreatePlugin()`, against source. |
| `ConformanceSubject.Executable(PluginLaunchSpec spec)` | A real built process. |
| `ConformanceSubject.Artifact(string macroDeckPluginPath)` | A packed `.macroDeckPlugin`, closest to what a user installs. |

Pass `ConformanceOptions` to the `ConformanceRunner` constructor to filter:

| Option | Default | Meaning |
| --- | --- | --- |
| `Categories` | `[]` | Only these `ConformanceCategory` values; empty means all. |
| `Ids` | `[]` | Only these check ids; empty means all. |
| `RequiredOnly` | `false` | Only Required checks. |
| `PerCheckTimeout` | 60 s | Hard limit per check, enforced even when a check ignores its own cancellation token. |

The three filters intersect.

## Outcomes and requirements

| Outcome | Meaning | Blocks `Conformant`? |
| --- | --- | --- |
| Passed | The subject satisfied the check. | No |
| Failed | The subject violated the check. | Only for a Required check |
| Skipped | A declared precondition (live session, manifest, external process, controllable clock) was not met, or the check does not apply to this subject. | No |
| Inconclusive | The suite's own infrastructure could not reach a verdict, for a reason outside the subject's control. | No |

- **Required** checks decide `ConformanceReport.Conformant`. **Recommended** checks need cooperation no protocol rule guarantees (an action slow enough to time out, log output to observe); their failures are reported and counted but never make a subject non-conformant.
- `ConformanceRunner` evaluates every precondition before the check body runs.
- Skipped and Inconclusive always carry a non-empty reason (`ConformanceCheckResult.Skip`/`Inconclusive` reject an empty one). A skip is not a pass: a plugin with no weather capability is not proven to handle weather correctly.
- **Every check id is a stable public contract.** An id is never reassigned, so `--check MDC0305` in CI or a documented suppression stays valid. A title may be clarified; what the id asserts does not change.

**Guard** below marks a regression guard: a check a plugin built on the .NET SDK (`MacroDeck.Plugin.Hosting`) structurally cannot fail, because `PluginHostBuilder.Build()` or `CapabilityDispatcher` already enforces it. Guards still run against the wire; they make the suite a real contract check for a plugin written in another language or on another SDK. Every other check has a counterexample proven by this repository's own misbehaving fixture, except MDC0804, which is a generic proxy for a trigger that cannot be built generically.

## Checks

Titles are shortened; `macrodeck-plugin test --list-checks` prints the full ones. Req: **R** Required, **Rec** Recommended.

### Manifest and identifiers (MDC01xx)

MDC0104-MDC0107 read the manifest, so they skip for any subject but an artifact.

| Id | Req | Checks | How to fix |
| --- | --- | --- | --- |
| <a id="mdc0101"></a>MDC0101 | R, guard | The id `/_macrodeck/info` reports matches the reverse-domain package-id grammar. | Use an id like `com.example.my-plugin`. |
| <a id="mdc0102"></a>MDC0102 | R, guard | Every declared capability `LocalId` on the wire matches the declared-id grammar. | Use lowercase kebab-case local ids. |
| <a id="mdc0103"></a>MDC0103 | R | Every id `weather/instances` returns matches the resource-id grammar; skips without weather. | Validate instance ids chosen at runtime. |
| <a id="mdc0104"></a>MDC0104 | R | `manifestVersion` is supported and a declared `compatibility.protocol` overlaps the suite's protocol range. | Set a supported manifest version and protocol range. |
| <a id="mdc0105"></a>MDC0105 | R, guard | The manifest `id` equals the reported id (ordinal); also skips with no reported id. | Keep one id. |
| <a id="mdc0106"></a>MDC0106 | R | The manifest `name` and `version` equal `PluginHealthReport.Name`/`Version` (ordinal); also skips when neither is reported. | Do not override name or version at runtime. |
| <a id="mdc0107"></a>MDC0107 | R | `icons/describe` reports the media type the manifest icon's extension implies, from the check's own extension table; skips with no icon or an unmapped extension ([MDP1003](/reference/analyzers/#mdp1003)). | Serve the icon with its real media type. |

### Registration and negotiation (MDC02xx)

| Id | Req | Checks | How to fix |
| --- | --- | --- | --- |
| <a id="mdc0201"></a>MDC0201 | R, guard | `session.hello` carries the negotiated version and session id, and `session.welcome` arrives exactly once. | Follow the [handshake](/reference/protocol/). |
| <a id="mdc0202"></a>MDC0202 | R, guard | `session.hello` to `session.welcome` takes at most `ProtocolTimeouts.Handshake` (10 s); skips with no completed handshake. | Answer the handshake promptly. |
| <a id="mdc0203"></a>MDC0203 | R, guard | Against a host with an unreachable protocol range, the subject exits cleanly or settles not-live; tested through the client-side pre-check. | Stop rather than retry forever. |
| <a id="mdc0204"></a>MDC0204 | R | Two starts of a self-registering subject against the same host and state directory register exactly once; skips unless in-process and self-registering. | Persist and reuse the credentials. |
| <a id="mdc0205"></a>MDC0205 | R | A managed subject makes zero registration calls; skips unless managed. | Never register when managed. |
| <a id="mdc0206"></a>MDC0206 | R | With no enrollment token, a self-registering subject pairs through `POST /api/plugins/pairing`, the redeemed verifier hashes to the sent challenge, and a second start opens a session without pairing again; skips unless in-process and self-registering. | Prove possession of the verifier, and persist the credential. |

### Capability serialization (MDC03xx)

| Id | Req | Checks | How to fix |
| --- | --- | --- | --- |
| <a id="mdc0301"></a>MDC0301 | R, guard | Every declared kind is in `CapabilityKinds.All`. | Declare only known kinds. |
| <a id="mdc0302"></a>MDC0302 | R | Every declared version range has `Minimum <= Maximum`, and no negotiated capability was rejected. | Declare a range the host supports. |
| <a id="mdc0303"></a>MDC0303 | R, guard | Every kind but actions and variables declares exactly one capability, at local id `provider` (`icon` for `icons`); `variables` catalog operations do not make it provider-shaped. | Declare provider-shaped kinds once, at the documented id. |
| <a id="mdc0304"></a>MDC0304 | R, guard | At most `MaxDeclaredCapabilities` (512) capabilities are declared. | Declare fewer. |
| <a id="mdc0305"></a>MDC0305 | R | The `actions/describe` reply is within `MaxMessageBytes` (256 KiB) and deserializes into `ActionCatalogPayload`; skips without actions. | Keep action metadata small. |
| <a id="mdc0306"></a>MDC0306 | R | `ui/describe` deserializes into `UiDescribePayload` and no surface has an empty `kind`; skips without ui. | Name every surface's kind. |
| <a id="mdc0307"></a>MDC0307 | R | No `ui/describe` surface has an empty `sessionMode`; skips without ui. | Set a session mode, since an unknown one is treated as exclusive. |
| <a id="mdc0308"></a>MDC0308 | R | The `ui/describe` reply is within 256 KiB and deserializes into `UiDescribePayload`; skips without ui. | Keep the surface list small. |
| <a id="mdc0309"></a>MDC0309 | R | For each action reporting `ProvidesState`, `state` returns an `ActionStateResult` whose present snapshot has a non-empty `States` list with non-empty unique ids and an `ActiveStateId` naming one of them; `null` passes. | Return a consistent snapshot, or `null` when nothing is known. |
| <a id="mdc0310"></a>MDC0310 | R | Every returned state id passes `MacroDeckId.TryValidateLocalId(_, LocalIdKind.Declared)`, as a button persists it; skips without `ProvidesState`. | Use declared-id grammar for state ids. |
| <a id="mdc0311"></a>MDC0311 | R | `variables/discover` answers within 5 s with at most `MaxVariableCatalogPageSize` items, each with a known `VariableType` and a resource id without `::` and within `MacroDeckId.MaxResourceLocalIdLength`, and any `ContinuationToken` non-empty; skips without variables or with `supportsCatalog: false`, and **fails** if `describe` cannot be answered. | Page and bound the catalog. |
| <a id="mdc0312"></a>MDC0312 | R | For each action reporting `ProvidesIcon`, a `NoIcon` snapshot has no `Reference` and an empty `Version`, and any other has a non-empty `Version` and a `Reference` with non-empty `Type` and `Reference`; `HasValue: false` passes. | Return an internally consistent icon snapshot. |
| <a id="mdc0313"></a>MDC0313 | R | A non-`NoIcon` snapshot with no `Reference` answers `icon.content` with a non-empty `ContentHash` and an `image/*` `MediaType`, reports its `Version` has moved on, or fails only with `AssetTooLarge`; skips without `ProvidesIcon`. | Serve the icon bytes you advertise. |
| <a id="mdc0314"></a>MDC0314 | R | For each eager variable declaring `write`, writing its current reading back through `variables/set` succeeds as an invocation with a status other than `NotWritable` or `NotFound`; skips when nothing declares `write`, inconclusive when every writable variable reads unavailable. | Declare `write` only on variables you accept writes for. |
| <a id="mdc0315"></a>MDC0315 | R | `variables` and `declaredVariables` together hold at most `VariableLimits.MaxEagerVariablesPerProvider` eager entries, counted across the whole plugin; skips without variables. | Move the rest into a catalog. |

### Duplicate ids (MDC04xx)

| Id | Req | Checks | How to fix |
| --- | --- | --- | --- |
| <a id="mdc0401"></a>MDC0401 | R, guard | No `(Kind, LocalId)` pair repeats among declared capabilities. | Rename one. |
| <a id="mdc0402"></a>MDC0402 | R | No `Id` repeats in `weather/instances`; skips without weather. | Keep instance ids unique. |
| <a id="mdc0403"></a>MDC0403 | R | No two eager variables resolve to the same local id, or to the same identity derived from `Name` when neither declares one; catalog ids are out of scope; skips without variables. | Give each eager variable its own id. |

### Timeout and cancellation (MDC05xx)

| Id | Req | Checks | How to fix |
| --- | --- | --- | --- |
| <a id="mdc0501"></a>MDC0501 | R, guard | One invocation gets exactly one reply for its correlation id; skips with nothing to invoke. | Reply once. |
| <a id="mdc0502"></a>MDC0502 | Rec | An action invoked with a short deadline (300 ms) that times out produces exactly one reply; skips when no action runs that long. | Stop after `TIMEOUT`. |
| <a id="mdc0503"></a>MDC0503 | R, guard | Cancelling an unknown correlation id, or an already answered one, produces no plugin message. | Ignore such cancels. |
| <a id="mdc0504"></a>MDC0504 | Rec | Cancelling an in-flight invocation that reports cancelled produces exactly one reply; skips when nothing stays in flight long enough. | Reply once to a cancel. |
| <a id="mdc0505"></a>MDC0505 | Rec, guard | 40 concurrent invocations never push the reported in-flight count past `MaxConcurrentInvocations` (32), and all complete; skips with nothing to invoke. | Queue a burst, do not fail it. |

### Disconnect and reconnect (MDC06xx)

| Id | Req | Checks | How to fix |
| --- | --- | --- | --- |
| <a id="mdc0601"></a>MDC0601 | R | After a retryable close, a new session opens and health reports ready again. | Reconnect after non-fatal closes. |
| <a id="mdc0602"></a>MDC0602 | R | That reconnect sends the right `resumeSessionId` in `session.hello` and gets `resumed: true` with the same session id. | Resume inside the window. |
| <a id="mdc0603"></a>MDC0603 | R | With a zero resume window, the reconnect gets a fresh session id and eventually becomes ready. | Make re-initialization idempotent. |
| <a id="mdc0604"></a>MDC0604 | R | A close with `SupervisorShutdown` (4004) leaves a managed subject not-live and a self-registering one live. | Exit on 4004 only when managed. |

### Health endpoint (MDC07xx)

| Id | Req | Checks | How to fix |
| --- | --- | --- | --- |
| <a id="mdc0701"></a>MDC0701 | R | While session creation is held, `/_macrodeck/health` answers and `/_macrodeck/ready` does not; ready turns true once released. | Report ready only with a session. |
| <a id="mdc0702"></a>MDC0702 | R | `/_macrodeck/info` and `/_macrodeck/diagnostics` report the negotiated version and declared/accepted capability counts the host recorded. | Report the real session. |
| <a id="mdc0703"></a>MDC0703 | R, guard | An unmapped path under `/_macrodeck/` answers 404. | Map nothing under the prefix ([MDP2005](/reference/analyzers/#mdp2005)). |
| <a id="mdc0704"></a>MDC0704 | R | A health probe at the base address the launcher expects succeeds. | Listen where you are told ([MDP4002](/reference/analyzers/#mdp4002)). |

### Bounded queues (MDC08xx)

| Id | Req | Checks | How to fix |
| --- | --- | --- | --- |
| <a id="mdc0801"></a>MDC0801 | Rec | An invocation that logs still replies while draining is paused, so `capability.result` is pause-exempt, and its logs arrive after resuming; skips without log output. | Do not block on logging. |
| <a id="mdc0802"></a>MDC0802 | Rec | After a log flood while paused, a trailing Error still arrives and `Dropped` is honest; skips without enough log output. | Log through the SDK pipeline, not a private sink. |
| <a id="mdc0803"></a>MDC0803 | R | Every collected log event is within the protocol limits for message length, property count, property value length, source-context length and exception-chain depth; skips before any logs. | Trim oversized log events. |
| <a id="mdc0804"></a>MDC0804 | R | The published-event count is the same just before and just after a disconnect/reconnect cycle. | Do not replay events on reconnect. |
| <a id="mdc0805"></a>MDC0805 | Rec, guard | The MDC0505 burst, against `variables/get` on the first declared variable; skips without variables. | Queue a burst, do not fail it. |

## The report

`ConformanceRunner.RunAsync` returns a `ConformanceReport`; `ConformanceReportWriter.ToText`, `ToJson` and `ToMarkdown` render it, and the CLI's `--report Text|Json|Markdown` selects the same writers.

| Member | Type |
| --- | --- |
| `SuiteVersion` | `string` (currently `1.2.0`) |
| `PluginId`, `PluginVersion` | `string?` |
| `StartedAt` | `DateTimeOffset` |
| `Duration` | `TimeSpan` |
| `Results` | `IReadOnlyList<ConformanceCheckOutcome>`, one per selected check, in id order |
| `Passed`, `Failed`, `Skipped` | `int`; `Skipped` counts Skipped and Inconclusive together |
| `Conformant` | `bool` |

Each `ConformanceCheckOutcome` carries `Id`, `Title`, category, requirement and a `ConformanceCheckResult`: `Outcome`, `Expected`/`Actual` for a failure, `SkipReason` for a skip or inconclusive, `Duration`, and any `Observations`.

JSON is camelCase, indented, with string enums (trimmed):

```json
{
  "suiteVersion": "1.2.0",
  "pluginId": "com.example.hello-deck",
  "pluginVersion": "1.0.0",
  "startedAt": "2026-09-12T10:27:59.604661+00:00",
  "duration": "00:00:21.5385029",
  "results": [
    {
      "id": "MDC0101",
      "title": "The plugin id is a valid reverse-domain package id",
      "category": "manifestAndIdentifiers",
      "requirement": "required",
      "result": {
        "outcome": "passed",
        "expected": null,
        "actual": null,
        "skipReason": null,
        "duration": "00:00:00.0055956",
        "observations": [{ "label": "plugin id", "detail": "com.example.hello-deck" }]
      }
    }
  ],
  "passed": 25,
  "failed": 0,
  "skipped": 24,
  "conformant": true
}
```

Markdown (trimmed):

```md
# Macro Deck plugin conformance report

Suite version: `1.2.0`
Plugin: `com.example.hello-deck` `1.0.0`
Conformant: **yes**
Passed: 25 - Failed: 0 - Skipped: 24

| Id | Title | Category | Requirement | Outcome | Detail |
|---|---|---|---|---|---|
| MDC0101 | The plugin id is a valid reverse-domain package id | ManifestAndIdentifiers | Required | PASS |  |
| MDC0103 | Every weather station instance id is a valid resource-kind identifier | ManifestAndIdentifiers | Required | SKIP | This subject does not declare the weather capability. |
```

## See also

- [Testing plugins](/features/testing/) - `MacroDeck.Plugin.Testing`, which the suite is built on.
- [`macrodeck-plugin test`](/cli/test/) - options and report formats.
- [ADR 0026](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0026-plugin-protocol-and-sdk-boundary.md) - why the suite is framework-agnostic and its ids are a stable contract.
- [Plugin protocol](/reference/protocol/) - the wire contract the checks assert.
- [Analyzers](/reference/analyzers/) - the compile-time counterparts.
