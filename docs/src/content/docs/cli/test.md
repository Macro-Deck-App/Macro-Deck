---
title: macrodeck-plugin test
description: Run the conformance suite against a project, executable or artifact and write a text, JSON or Markdown report.
---

Runs the [conformance suite](/reference/conformance/) against a plugin and writes a report.

## Examples

```bash
macrodeck-plugin test --project src/HelloDeck
```

```text
Macro Deck plugin conformance report (suite 1.2.0)
Plugin: com.example.hello-deck 1.0.0
Started: 2026-09-12T09:36:25.7456760+00:00, duration: 00:00:22.3743497
Passed: 25, Failed: 0, Skipped: 24
Conformant: yes

[PASS] MDC0101 The plugin id is a valid reverse-domain package id (Required)
[PASS] MDC0102 Every declared capability's local id is a valid declared-kind identifier (Required)
...
[SKIP] MDC0403 No two eager variables resolve to the same id (Required)
    Reason:   This subject does not declare the variables capability.
...
```

Run every check against a project. `test` builds it first.

```bash
macrodeck-plugin test --artifact ./artifacts/com.example.hello-deck-1.0.0-osx-arm64.macroDeckPlugin \
  --report markdown --output conformance.md
```

```markdown
# Macro Deck plugin conformance report

Suite version: `1.2.0`
Plugin: `com.example.hello-deck` `1.0.0`
Conformant: **yes**
Passed: 29 - Failed: 0 - Skipped: 20

| Id | Title | Category | Requirement | Outcome | Detail |
|---|---|---|---|---|---|
| MDC0101 | The plugin id is a valid reverse-domain package id | ManifestAndIdentifiers | Required | PASS |  |
| MDC0103 | Every weather station instance id is a valid resource-kind identifier | ManifestAndIdentifiers | Required | SKIP | This subject does not declare the weather capability. |
...
```

Test the artifact you ship, and keep the report as a CI artifact or job summary. The artifact must
contain an entrypoint for the machine running `test`.

```bash
macrodeck-plugin test --project src/HelloDeck --category health-endpoint --category duplicate-ids
macrodeck-plugin test --project src/HelloDeck --check MDC0305
```

Iterate on one area or one failing check.

```bash
macrodeck-plugin test --project src/HelloDeck --required-only --report json --output conformance.json
```

Gate on Required checks only, with a machine-readable report.

```bash
macrodeck-plugin test --list-checks
```

```text
MDC0101	ManifestAndIdentifiers	Required	The plugin id is a valid reverse-domain package id
MDC0102	ManifestAndIdentifiers	Required	Every declared capability's local id is a valid declared-kind identifier
...
MDC0805	BoundedQueues	Recommended	A burst of variables/get invocations beyond MaxConcurrentInvocations never exceeds the reported in-flight bound
```

Every check id, category and requirement level.

## Options

Exactly one of `--project`, `--executable` or `--artifact` is required, unless `--list-checks` is given.

| Option | Default | Description |
| --- | --- | --- |
| `--project <path>` | - | A plugin's `.csproj`, or its directory. |
| `--executable <path>` | - | An already-built executable or framework-dependent `.dll`. |
| `--artifact <path>` | - | A packed `.macroDeckPlugin` artifact. |
| `--category <token>` | all | Restrict to one [category](#categories); repeatable. |
| `--check <id>` | all | Restrict to one check id, e.g. `MDC0305`; repeatable. |
| `--required-only` | off | Run only Required checks. |
| `--report <text\|json\|markdown>` | `text` | The report format. |
| `--output <path>` | stdout | Where to write the report. |
| `--timeout <seconds>` | `60` | Per-check timeout. |
| `--list-checks` | off | List every check and exit; ignores every other option except `--output` and the global options. |

## Filters

`--category`, `--check` and `--required-only` intersect. A `--check` id outside the selected
`--category` selects nothing, without an error.

`--check` is validated against the full catalogue, whatever the other filters select:

```text
$ macrodeck-plugin test --project src/HelloDeck --check MDC9999
error unknown-check-id: 'MDC9999' is not a known check id. Run --list-checks to see every id.
```

### Categories

| Token | Checks |
| --- | --- |
| `manifest-and-identifiers` | `MDC01xx` |
| `registration-and-negotiation` | `MDC02xx` |
| `capability-serialization` | `MDC03xx` |
| `duplicate-ids` | `MDC04xx` |
| `timeout-and-cancellation` | `MDC05xx` |
| `disconnect-and-reconnect` | `MDC06xx` |
| `health-endpoint` | `MDC07xx` |
| `bounded-queues` | `MDC08xx` |

The exact shape of each report format is in [Conformance: the report](/reference/conformance/#the-report).

## Exit codes

| Code | When |
| --- | --- |
| 0 | The report's `conformant` is `true`. |
| 1 | A Required check failed. |
| 2 | Usage error: wrong number of subjects, an unknown `--category` or `--check`. |
| 3 | The subject could not be built or launched at all - not a conformance verdict. |

See the [shared exit codes](/cli/#exit-codes).

## See also

- [Conformance](/reference/conformance/) - what every check asserts, and the report shape.
- [Testing plugins](/features/testing/) - running the same suite from your own test project.
- [CI and automation](/cli/ci/) - using `test` as a pipeline gate.
