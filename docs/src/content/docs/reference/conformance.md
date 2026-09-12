---
title: Conformance suite
description: The fixed, framework-agnostic set of protocol-level checks any plugin can be run against, in any language, without a Macro Deck installation.
---

The conformance suite is a fixed set of protocol-level checks - 49 of them, grouped into 8 areas - that
any plugin can be run against, in any language, without a Macro Deck installation. It lives in
`MacroDeck.Plugin.Testing.Conformance`, built on the same `MacroDeckTestHost` the [Testing plugins guide](/features/testing/)
documents, and produces one structured report with no test-framework types anywhere in it - see
[ADR 0026](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0026-plugin-protocol-and-sdk-boundary.md) for why that is a
deliberate design choice, not an omission.

**Every check id is a stable public contract.** `MDC0305` names the same rule for as long as this suite
exists; a third party who pins `--check MDC0305` in their own CI, or suppresses it with a documented
reason, can rely on that id never being reassigned to a different check. A check's title and internal
behavior may still be clarified over time - the id and what it fundamentally asserts do not change.

## Required, Recommended, and what a check can conclude

Every check declares a `ConformanceRequirement`:

- **Required** - the suite's own conformance verdict (`ConformanceReport.Conformant`) is `false` if any
  Required check reports `Failed`.
- **Recommended** - used where the assertion needs cooperation no protocol rule guarantees a conforming
  plugin provides (an action slow enough to observe a timeout against, log output to observe pausing
  behavior against). A `Recommended` failure is still reported and still counted, but never flips
  `Conformant` to `false` - a subject that simply has nothing suitable to exercise a check with is not
  penalized for it.

Every check resolves to one of four outcomes:

- **Passed** - the subject satisfied the check.
- **Failed** - the subject violated the check. Only a Required check's `Failed` blocks `Conformant`.
- **Skipped** - a precondition the check declared (a live session, a manifest, an external process, a
  controllable clock) was not met, or the check decided at run time it does not apply to this subject -
  weather checks against a plugin that declares no weather station, for instance. `ConformanceRunner`
  evaluates every declared precondition **before** the check body ever runs, so a check never has to guard
  against a missing session itself.
- **Inconclusive** - the suite's own infrastructure could not reach a verdict, for a reason outside the
  subject's control.

Neither `Skipped` nor `Inconclusive` ever blocks `Conformant`, and neither is silent: both carry a
mandatory, non-empty reason (`ConformanceCheckResult.Skip`/`Inconclusive` reject an empty one), so a
report always says *why* a check did not run rather than just that it didn't. "Skipped" is not a synonym
for "passed" - a plugin that declares no weather capability is not thereby proven to handle weather
correctly; MDC0103 simply had nothing to check.

## Regression guards

A little under half the Required checks below are marked **regression guard**. That label means a check
asserts something a plugin built on the real .NET SDK (`MacroDeck.Plugin.Hosting`) structurally cannot
violate - `PluginHostBuilder.Build()` already rejects an invalid id before a session can exist, or
`CapabilityDispatcher` already enforces the single-reply guarantee no handler can see around. These checks
are still real, still run against the wire rather than assumed, and still matter: they are what makes the
suite a genuine contract check for a plugin **not** built on this SDK - a hand-rolled implementation in
another language, or a future second SDK - rather than a check that only ever exercises what the shipped
SDK already guarantees for itself. A check without that label generally has a real, constructible
counterexample, proved by running this repository's own deliberately misbehaving fixture against it and
confirming it fails - MDC0804 is the one documented exception, whose own remarks describe it as a weaker,
fully generic proxy for a trigger that cannot be constructed generically at all.

## The checks

### Manifest and identifiers (`MDC01xx`)

MDC0101 and MDC0102 are regression guards: a malformed plugin id or a malformed *declared* local id cannot
reach a running subject at all, because `PluginHostBuilder.Build()` rejects both first. MDC0103 is what
earns this category its runtime keep - a weather station's instance id is chosen after the plugin has
already started, where nothing at build time can catch a malformed one.

| Id | Title | Requirement | What it asserts |
| --- | --- | --- | --- |
| MDC0101 | The plugin id is a valid reverse-domain package id | Required | The id `/_macrodeck/info` reports validates against the package-id grammar. Regression guard. |
| MDC0102 | Every declared capability's local id is a valid declared-kind identifier | Required | Every declared capability's `LocalId`, read off the wire, validates against the declared-id grammar. Regression guard. |
| MDC0103 | Every weather station instance id is a valid resource-kind identifier | Required | Every id `weather/instances` returns validates against the resource-id grammar. Skipped when weather is not declared. |
| MDC0104 | The manifest declares a supported manifest version and a protocol range this suite satisfies | Required | `manifestVersion` matches what this suite supports, and a declared `compatibility.protocol` overlaps the suite's own protocol range. Skipped when the subject has no manifest (an `Executable` subject with no packed artifact behind it). |
| MDC0105 | The manifest's id equals the id the subject reports at `/_macrodeck/info` | Required | Ordinal-compares the manifest's `id` against the reported id. Skipped when there is no manifest or no reported id. Regression guard - `Build()` already rejects a manifest id that disagrees with a configured one. |
| MDC0106 | The manifest's name and version equal what the subject reports at `/_macrodeck/info` | Required | Ordinal-compares the manifest's `name` and `version` against `PluginHealthReport.Name`/`Version`. Skipped when there is no manifest or `/_macrodeck/info` reported neither. Not a pure regression guard - nothing at build time cross-checks the manifest against what `/_macrodeck/info` ends up answering. |
| MDC0107 | When the manifest declares an icon, `icons/describe` reports the media type its extension implies | Required | Compares `icons/describe`'s `mediaType` against a fixed extension table this check owns independently of `IconMediaTypes` (so the check cannot pass by agreeing with a bug in the code it verifies). Skipped when the manifest declares no icon, or declares one whose extension this suite does not map to a media type (MDP1003's concern at compile time, not this check's at run time). |

### Registration and negotiation (`MDC02xx`)

MDC0201 through MDC0203 are regression guards - the handshake itself is not something a subject built on
this SDK can get wrong on its own; they exist for a subject that is not.

| Id | Title | Requirement | What it asserts |
| --- | --- | --- | --- |
| MDC0201 | `session.hello` asserts the granted protocol version and session id, and `session.welcome` answers exactly once | Required | The recorded `session.hello`/`session.welcome` envelopes match the negotiated version and session id, with exactly one `session.welcome`. Regression guard. |
| MDC0202 | The handshake completes within `ProtocolTimeouts.Handshake` | Required | The gap between `session.hello` and `session.welcome` is within the 10-second handshake timeout. Skipped when no completed handshake was recorded. Regression guard. |
| MDC0203 | A host whose protocol range this subject cannot speak causes it to stop, not retry forever | Required | Connecting to a second host advertising an unreachable protocol version ends in a clean exit, or the subject settling to not-live and staying there. Regression guard, exercised through the client-side pre-check rather than the wire-level rejection path. |
| MDC0204 | A self-registering subject registers once and reuses its persisted credentials on a later start | Required | Starting the same self-registering subject twice against the same host and state directory produces exactly one registration in total. Skipped for a subject that is not in-process and self-registering. |
| MDC0205 | A managed subject never calls the registration endpoint | Required | The host recorded zero registrations for a managed subject. Skipped for a subject that is not managed. |
| MDC0206 | A self-registering subject with no enrollment token pairs interactively and reuses the persisted credential on a later start | Required | Starting a self-registering subject with no enrollment token pairs through `POST /api/plugins/pairing`, and the redeemed verifier hashes to the challenge that was sent - so the subject proves possession rather than sending the same value twice. A second start against the same host and state directory opens a session without creating another pairing request. Skipped for a subject that is not an in-process self-registering plugin, because pairing is not observable for an executable or artifact subject through this suite. |

### Capability serialization (`MDC03xx`)

MDC0301 and MDC0304 are regression guards read straight from the wire; so is MDC0303 - a capability's kind
and shape are decided by handler code, never by a plugin author. MDC0305 through MDC0315 are what
earn this category its runtime keep.

| Id | Title | Requirement | What it asserts |
| --- | --- | --- | --- |
| MDC0301 | Every declared capability's kind is one of `CapabilityKinds.All` | Required | Every declared capability's `Kind` is a known kind. Regression guard. |
| MDC0302 | Every declared capability's version range is valid and overlaps this host's supported range | Required | Every declared capability's `Minimum <= Maximum`, and none of the negotiated capabilities was rejected. |
| MDC0303 | A provider-shaped kind declares exactly one capability, at its documented local id | Required | Every non-item-shaped kind (everything but actions and variables) declares exactly one capability, at the `provider` local id - except `icons`, which must be `icon`. That `variables` also answers catalog operations naming no declared id - `discover`, `resolve`, `subscribe` - does not make it provider-shaped: those resource ids travel inside operation arguments, while what it *declares* is still one capability per eager variable. Regression guard: only handler code decides a kind's local id. |
| MDC0304 | The declared capability count does not exceed `MaxDeclaredCapabilities` | Required | `Session.Declared.Count <= 512`. Regression guard. |
| MDC0305 | `actions/describe`'s reply stays within `MaxMessageBytes` and deserializes without loss | Required | The recorded `actions/describe` reply, re-serialized, is within the 256 KiB message cap and deserializes into `ActionCatalogPayload`. Skipped when actions is not declared. Not a regression guard - nothing at the protocol level stops an author from letting a description balloon past the cap. |
| MDC0306 | `ui/describe` reports a surface list whose every entry names a kind | Required | `ui/describe`'s result deserializes into `UiDescribePayload`, and no declared surface has an empty `kind`. Skipped when ui is not declared. Not a regression guard - the surface kind vocabulary is deliberately open, so nothing at the protocol level rejects an empty one. |
| MDC0307 | Every surface `ui/describe` declares names a non-empty session mode | Required | No declared surface has an empty `sessionMode`. Skipped when ui is not declared. Not a regression guard - an unrecognised mode is enforced as exclusive rather than rejected, so an empty one would silently take whatever default the host picks. |
| MDC0308 | `ui/describe`'s reply stays within `MaxMessageBytes` and deserializes without loss | Required | The recorded `ui/describe` reply, re-serialized, is within the 256 KiB message cap and deserializes into `UiDescribePayload`. Skipped when ui is not declared. A separate id from MDC0305 rather than a widening of it, because MDC0305's meaning - and its skip for a subject that declares no actions - is a pinned contract. |
| MDC0309 | Every state-provider action's state operation returns a well-formed snapshot | Required | For every declared action reporting `ProvidesState`, the `state` operation's reply deserializes into `ActionStateResult`, and a present snapshot has a non-empty `States` list whose entries carry non-empty, unique ids, with `ActiveStateId` (when set) naming one of them. A `null` snapshot always passes - it is the documented "nothing is known" answer. Skipped when no declared action reports `ProvidesState`. Not a regression guard - nothing at the protocol level constrains what a provider hands back. |
| MDC0310 | Every state a state-provider action returns has an id that is a valid declared-kind identifier | Required | Every id in a present snapshot's `States` passes `MacroDeckId.TryValidateLocalId(_, LocalIdKind.Declared)` - the same grammar and the same call MDC0102 makes for a capability's own local id, because a state id is persisted in a user's profile once a button adopts it. Skipped when no declared action reports `ProvidesState`. Not a regression guard - a state id is chosen at run time, not validated at build time. |
| MDC0311 | A variable provider reporting a catalog answers `discover` with a bounded, well-formed page | Required | `variables/discover` replies within 5 s, and its page carries at most `MaxVariableCatalogPageSize` items, each with a known `VariableType` name and its own resource id, free of the `::` owner-qualifier separator and no longer than `MacroDeckId.MaxResourceLocalIdLength`; a present `ContinuationToken` is non-empty. Skipped when `variables` is not declared, or when `variables/describe` reports `supportsCatalog: false` - the precondition is that flag, not a declared kind, since there is no separate catalog kind to look for and the eager half says nothing about whether a catalog exists. A subject that declares `variables` and then cannot answer `describe` **fails** rather than skipping: "the precondition could not be read" is a broken subject, not an absent feature. Not a regression guard - a subject talking the wire protocol directly is under no obligation from handler code to hold to these bounds itself. |
| MDC0312 | Every icon-provider action's icon snapshot is internally consistent | Required | For every declared action reporting `ProvidesIcon`, the `icon` operation's reply deserializes into `ActionIconResult`, and a present snapshot with `NoIcon` set names no `Reference` and carries an empty `Version`, while one that does not set `NoIcon` has a non-empty `Version` and, when present, a `Reference` with a non-empty `Type` and `Reference`. A snapshot the plugin cannot yet answer (`HasValue: false`) always passes - it is the documented "cannot answer right now" answer. Skipped when no declared action reports `ProvidesIcon`. Not a regression guard - nothing at the protocol level constrains what a provider hands back. |
| MDC0313 | A snapshot naming no reference is answerable by icon.content, with AssetTooLarge the only allowed failure | Required | For every present, non-`NoIcon` icon snapshot naming no `Reference`, the `icon.content` operation either succeeds with a non-empty `ContentHash` and an `image/*` `MediaType`, answers that the requested `Version` has already moved on (also valid), or fails with `AssetTooLarge` specifically. Skipped when no declared action reports `ProvidesIcon`. Not a regression guard - a provider-declared media type is entirely the plugin author's choice. |
| MDC0314 | A variable that declares a write capability answers `set` with something other than `NotWritable` or `NotFound` | Required | For every eager definition carrying `write`, the check reads the variable and writes its own current reading straight back, so a conforming subject ends where it started. `variables/set` must succeed as an invocation - a refused write is a `status`, not a failed operation - and must answer with a status other than `NotWritable` or `NotFound`. Skipped when `variables` is not declared or nothing declares `write`; inconclusive when every writable variable reads unavailable, since the check will not invent a value to write. Not a regression guard - the declaration is the plugin author's, and a client decides whether to offer a control from it alone, so a definition that declares `write` and then refuses leaves a control that silently does nothing. |
| MDC0315 | The eager variable list stays within `MaxEagerVariablesPerProvider` | Required | Neither `variables` nor `declaredVariables` in `variables/describe` carries more than `VariableLimits.MaxEagerVariablesPerProvider` entries whose `materialization` is `eager`. Skipped when `variables` is not declared. The wire carries no grouping by provider, so what is read is the plugin's whole eager list - the stricter reading of a per-provider bound and the only one a describe payload supports. Not a regression guard - a plugin built on this SDK is clamped with an error line rather than refused, and this is what holds a subject talking the wire protocol directly to the same number. |

### Duplicate ids (`MDC04xx`)

MDC0401 is a regression guard read from the wire. MDC0402 and MDC0403 are what earn this category its
runtime keep: two provider instances, or two eager variables, both choose their own colliding identity
at run time.

| Id | Title | Requirement | What it asserts |
| --- | --- | --- | --- |
| MDC0401 | No two declared capabilities share the same `(kind, localId)` pair | Required | `Session.Declared` has no repeated `(Kind, LocalId)` pair. Regression guard. |
| MDC0402 | No two weather station instances share an instance id | Required | `weather/instances` has no repeated `Id`. Skipped when weather is not declared. Not a regression guard - two instances can genuinely both claim the same id at run time. |
| MDC0403 | No two eager variables resolve to the same id | Required | No two `eager` entries in `variables/describe` resolve to the same local id (or, where neither declares one, to the same effective identity derived from `Name`). Scoped to the eager half: a catalog resource id is never declared as a capability and is not enumerable from a describe payload at all, so uniqueness across a catalog is not a claim this check can support. Skipped when variables is not declared. Not a regression guard, same reasoning as MDC0402. |

### Timeout and cancellation (`MDC05xx`)

MDC0502 and MDC0504 need a declared action slow enough to observe timing behavior against, which nothing
in the protocol guarantees exists - both are Recommended and skip cleanly when nothing qualifies. MDC0501
and MDC0503 need no such cooperation and are Required. MDC0505's concurrency bound is a regression guard.

| Id | Title | Requirement | What it asserts |
| --- | --- | --- | --- |
| MDC0501 | An invocation receives exactly one reply, never more | Required | Invoking a declared capability once produces exactly one reply for its correlation id. Skipped when nothing is declared to invoke. Regression guard - `CapabilityDispatcher`'s single-reply guarantee cannot be bypassed by a handler. |
| MDC0502 | A deadline that elapses produces `TIMEOUT`, and nothing arrives afterward | Recommended | Invoking a declared action with a short deadline, if it times out, produces exactly one reply. Skipped when no declared action ran long enough to observe deadline enforcement. |
| MDC0503 | Cancelling an unknown or already-answered correlation produces no message at all | Required | Cancelling a never-used correlation id, and separately the correlation of an already-completed invocation, produces no plugin-originated message either time. Regression guard - cancellation rules are applied entirely inside the dispatcher, before a handler ever runs. |
| MDC0504 | Cancelling an in-flight invocation produces exactly one cancelled reply | Recommended | Cancelling an invocation mid-flight, if the outcome reports cancelled, produces exactly one reply. Skipped when no declared action stayed in flight long enough to cancel before completing on its own. |
| MDC0505 | A burst beyond `MaxConcurrentInvocations` never exceeds the reported in-flight bound, and every invocation completes | Recommended | Firing 40 concurrent invocations never pushes the reported in-flight count past 32, and every one eventually completes. Skipped when nothing is declared to invoke. Regression guard - the SDK's own connection gate queues a burst rather than failing it fast. |

### Disconnect and reconnect (`MDC06xx`)

| Id | Title | Requirement | What it asserts |
| --- | --- | --- | --- |
| MDC0601 | After a non-fatal disconnect, the subject reconnects and becomes ready again | Required | Disconnecting with a retryable close code is followed by a new session and by health reporting ready again. |
| MDC0602 | Reconnecting inside the resume window presents `resumeSessionId` and resumes with the same session id | Required | The same reconnect reports `resumed: true`, the same session id, and the correct `resumeSessionId` on the reconnecting `session.hello`. |
| MDC0603 | A reconnect outside the resume window opens a fresh session and the subject becomes ready again after re-initializing | Required | The same disconnect, against a host configured with a zero resume window, produces a fresh session id and eventual readiness. Not a regression guard - exercised by this repository's own misbehaving fixture, whose re-initialization is not idempotent. |
| MDC0604 | A close of `SupervisorShutdown` (4004) stops a managed subject but leaves a self-registering one running | Required | Disconnecting with code 4004 leaves a managed subject not-live and a self-registering one still live. |

### Health endpoint (`MDC07xx`)

| Id | Title | Requirement | What it asserts |
| --- | --- | --- | --- |
| MDC0701 | `/_macrodeck/health` answers before any session exists; `/_macrodeck/ready` does not until one does | Required | While session creation is deliberately held open, health answers and ready does not; releasing it, ready eventually becomes true. |
| MDC0702 | `/_macrodeck/info` and `/_macrodeck/diagnostics` agree with what the host itself observed about this session | Required | The negotiated version and the declared/accepted capability counts these endpoints report match what the host itself recorded. |
| MDC0703 | An unmapped route under `/_macrodeck/` answers 404 | Required | A request to an undefined path under the reserved prefix answers 404. Regression guard. |
| MDC0704 | The subject serves its endpoints at the base address its launcher was told to expect | Required | A health probe against the expected base address succeeds. |

### Bounded queues (`MDC08xx`)

MDC0801 and MDC0802 need a declared action that produces observable log output, so both are Recommended
and skip cleanly when none does. MDC0803 needs no such cooperation. MDC0805 mirrors MDC0505's own
regression-guard reasoning, scoped to `variables/get`.

| Id | Title | Requirement | What it asserts |
| --- | --- | --- | --- |
| MDC0801 | Logging while draining is paused does not block, and queued traffic is not silently lost after resuming | Recommended | An invocation that logs still replies while draining is paused (proving `capability.result` is pause-exempt), and its queued log traffic surfaces once draining resumes. Skipped when no declared action produces observable log output. |
| MDC0802 | Under a logging flood while paused, a trailing Error still survives and `Dropped` is reported honestly | Recommended | A flood of log output produced while paused still lets a trailing Error-level event arrive once draining resumes. Skipped when no declared action produces observable log output. Not a regression guard - a plugin's own sink could bypass the logging pipeline entirely. |
| MDC0803 | Every collected log event respects the protocol's structural field limits | Required | Every already-collected log event's message length, property count, property value length, source-context length and exception-chain depth are each within the protocol's limits. Skipped when no log events have been collected yet. |
| MDC0804 | Reconnecting does not replay a burst of previously published events | Required | The published-event count immediately before and after a disconnect/reconnect cycle is unchanged. Not a regression guard, though described in the source as a weaker, fully generic proxy for the real trigger. |
| MDC0805 | A burst of `variables/get` invocations beyond `MaxConcurrentInvocations` never exceeds the reported in-flight bound | Recommended | The same burst mechanism as MDC0505, scoped to `variables/get` on the first declared variable. Skipped when variables is not declared. Regression guard, same reasoning as MDC0505. |

## Running the suite

### From your own test project

`ConformanceRunner` and `ConformanceSubject` have no test-framework dependency, so wiring them into NUnit,
xUnit or anything else is a few lines. `ConformanceSubject` has one factory per kind of subject:

```csharp
public static ConformanceSubject InProcess(Action<PluginHostBuilder> configure, PluginTestManifest? manifest = null)
public static ConformanceSubject Executable(PluginLaunchSpec spec)
public static ConformanceSubject Artifact(string macroDeckPluginPath)
```

`InProcess` takes the same configuration delegate you would hand to `MacroDeckPlugin.CreatePlugin()` -
useful in the plugin's own repository, where the suite can run against source rather than a build.
`Executable` and `Artifact` run a real process, which is closer to what a user actually installs.

This repository's own conformance tests fan the suite out into one NUnit test per check, so a failing
check is reported as its own named failure rather than one opaque assertion for the whole run:

```csharp
[TestFixture]
public class MyPluginConformanceTests
{
    private static readonly ConformanceRunner _runner = new();
    private static ConformanceReport? _report;

    public static IReadOnlyList<IConformanceCheck> Checks => _runner.Checks;

    [OneTimeSetUp]
    public async Task RunSuiteAsync()
    {
        await using var subject = ConformanceSubject.Executable(PluginLaunchSpec.ForExecutable("bin/Release/net10.0/MyPlugin.exe"));
        _report = await _runner.RunAsync(subject);
    }

    [TestCaseSource(nameof(Checks))]
    public void CheckIsSatisfied(IConformanceCheck check)
    {
        var result = _report!.Results.Single(r => r.Id == check.Id).Result;

        if (result.Outcome is ConformanceOutcome.Skipped or ConformanceOutcome.Inconclusive)
        {
            Assert.Ignore(result.SkipReason);
        }

        if (check.Requirement == ConformanceRequirement.Required)
        {
            Assert.That(result.Outcome, Is.EqualTo(ConformanceOutcome.Passed), $"Expected: {result.Expected}\nActual: {result.Actual}");
        }
    }
}
```

The same shape works under xUnit with `[Theory]`/`[MemberData]` in place of `[TestCaseSource]` and
`Assert.Skip`/`Assert.True` in place of `Assert.Ignore`/`Assert.That` - nothing about `ConformanceRunner`
or `ConformanceSubject` changes. Filter which checks run by passing a `ConformanceOptions` to the
`ConformanceRunner` constructor:

```csharp
public sealed class ConformanceOptions
{
    public IReadOnlyCollection<ConformanceCategory> Categories { get; init; } = [];
    public IReadOnlyCollection<string> Ids { get; init; } = [];
    public bool RequiredOnly { get; init; }
    public TimeSpan PerCheckTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
```

`Categories`, `Ids` and `RequiredOnly` intersect - an empty `Categories`/`Ids` means no restriction on that
axis. Every check runs under `PerCheckTimeout` regardless of whether the check itself observes
cancellation, specifically so a check that ignores its own cancellation token - the exact kind of plugin
bug the suite exists to catch - cannot hang the whole run.

### From the CLI

`macrodeck-plugin test` runs the same `ConformanceRunner` against a project, a built executable or a
packed artifact, with no .NET test project required - see the CLI's [`test` command](/cli/test/). It is the faster path for
a CI pipeline that only wants a pass/fail gate and a report, without writing an adapter of its own.

## The report

`ConformanceRunner.RunAsync` returns a `ConformanceReport`:

```csharp
public required string SuiteVersion { get; init; }
public string? PluginId { get; init; }
public string? PluginVersion { get; init; }
public required DateTimeOffset StartedAt { get; init; }
public required TimeSpan Duration { get; init; }
public required IReadOnlyList<ConformanceCheckOutcome> Results { get; init; }
public required int Passed { get; init; }
public required int Failed { get; init; }
public required int Skipped { get; init; }
public required bool Conformant { get; init; }
```

`Skipped` counts both `Skipped` and `Inconclusive` outcomes together. `Results` has one entry per selected
check, in id order, each carrying its category, its requirement, and the underlying
`ConformanceCheckResult` (`Outcome`, and for a failure `Expected`/`Actual`, or for a skip/inconclusive a
`SkipReason`, plus `Duration` and any `Observations` the check recorded along the way).

`ConformanceReportWriter` renders a report three ways - `ToText`, `ToJson`, `ToMarkdown` - which is also
exactly what the CLI's `--report` option selects between. The JSON shape (camelCase, indented, string
enums):

```json
{
  "suiteVersion": "1.2.0",
  "pluginId": "com.example.my-plugin",
  "pluginVersion": "1.0.0",
  "startedAt": "2026-01-15T12:00:00.0000000+00:00",
  "duration": "00:00:12.3456789",
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
        "duration": "00:00:00.0123456",
        "observations": [
          { "label": "plugin id", "detail": "com.example.my-plugin" }
        ]
      }
    },
    {
      "id": "MDC0103",
      "title": "Every weather station instance id is a valid resource-kind identifier",
      "category": "manifestAndIdentifiers",
      "requirement": "required",
      "result": {
        "outcome": "skipped",
        "expected": null,
        "actual": null,
        "skipReason": "This subject does not declare the weather capability.",
        "duration": "00:00:00.0012345",
        "observations": []
      }
    }
  ],
  "passed": 33,
  "failed": 0,
  "skipped": 3,
  "conformant": true
}
```

## See also

- [Testing plugins](/features/testing/) - `MacroDeck.Plugin.Testing`, the package this suite is built on.
- [`macrodeck-plugin test`](/cli/test/) - its options and report formats.
- [ADR 0026](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0026-plugin-protocol-and-sdk-boundary.md) - why the suite is a
  framework-agnostic core with thin adapters, and why check ids are a stable contract.
- [Plugin protocol](/reference/protocol/) - the wire contract these checks assert against.
