---
title: macrodeck-plugin test
description: Run the conformance suite against a project, executable or artifact and write a text, JSON or Markdown report.
---

Runs the [conformance suite](/reference/conformance/) against a project, executable or artifact, and writes a text,
JSON or Markdown report.

| Option | Default | What it does |
| --- | --- | --- |
| `--project <path>` | - | A plugin's `.csproj`, or its directory. |
| `--executable <path>` | - | An already-built executable or framework-dependent `.dll`. |
| `--artifact <path>` | - | A packed `.macroDeckPlugin` artifact. |
| `--category <token>` | - | Restrict to one category. Repeatable. See the token list below. |
| `--check <id>` | - | Restrict to one check id, e.g. `MDC0305`. Repeatable. |
| `--required-only` | off | Run only Required checks. |
| `--report <text\|json\|markdown>` | `text` | The report format. |
| `--output <path>` | stdout | Where to write the report. |
| `--timeout <seconds>` | 60 | Per-check timeout. |
| `--list-checks` | off | List every check id, category and requirement level, then exit. Ignores every other option except `--output` and the global options. |

Exactly one of `--project`/`--executable`/`--artifact` is required unless `--list-checks` is given.
`--category`, `--check` and `--required-only` intersect rather than combine - combining a `--check` id with
a `--category` that would not normally include it selects nothing, without an error, since the two filters
are independent axes rather than an either-or choice.

`--category` accepts these kebab-case tokens, one per conformance area:

`manifest-and-identifiers`, `registration-and-negotiation`, `capability-serialization`, `duplicate-ids`,
`timeout-and-cancellation`, `disconnect-and-reconnect`, `health-endpoint`, `bounded-queues`.

`--check` validates against the full, unfiltered check catalogue - an unrecognized id is a usage error
naming `--list-checks` as the way to see every valid one.

Exit code is `Success` (0) when the resulting report's `conformant` is `true`, `SubjectInvalid` (1) when a
Required check failed, and `InputUnreadable` (3) when the subject itself could not be launched at all -
distinct from a conformance failure, the same way `validate` distinguishes an unreadable manifest from an
invalid one. See [conformance.md: the report](/reference/conformance/#the-report) for the exact shape each format
produces.
