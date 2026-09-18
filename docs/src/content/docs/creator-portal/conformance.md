---
title: Conformance report
description: What the conformance status of a build means, every warning the Creator Portal shows about it, and how to fix each one.
---

The [release workflow](/creator-portal/release-workflow/) runs your plugin on a disposable stub host
with the [conformance suite](/reference/conformance/) and uploads the report with the build. The
Creator Portal shows it under every build in **Builds**, and to the moderator reviewing the version.

- A failed **required** check stops the release before anything is uploaded. Fix it and publish the
  release again.
- Everything else is advisory: a warning never blocks an upload, a release or a review. The moderator
  sees the same warnings you do.
- Each check in the report links to its entry in the [conformance reference](/reference/conformance/),
  which says what it asserts and how to fix it.

## Status

| Status | Meaning |
| --- | --- |
| Conformant | The report is readable and raised no warning. |
| Conformant, with warnings | No required check failed, but something below deserves a look. |
| Not conformant | A required check failed. |
| Not run on a stub host | No report came with the build. |
| Report unreadable | A report came with the build, but the portal could not read it. |

## Warnings

| Warning | Cause | Fix |
| --- | --- | --- |
| <a id="notprovided"></a>No conformance report | The workflow did not run the plugin on a stub host: `run-stub-host` is `false`, the manifest declares no platform a GitHub runner provides (`linux-x64`, `win-x64`, `osx-arm64`, `linux-arm64`, `win-arm64`, `osx-x64`), or the build predates conformance reports. | Remove `run-stub-host: false` from your release workflow, and declare at least one of those runtime identifiers in `manifest.json`. Publish the release again. |
| <a id="unreadable"></a>Report unreadable | The uploaded `conformance.json` is not the suite's JSON report: invalid JSON, over 1 MiB, or a check without a valid id, outcome or requirement. The reason is shown with the warning. | Use the official `publish-plugin.yml` at its current tag, and do not change `conformance.json` in your own steps. Run `macrodeck-plugin test --artifact <package> --report json` locally to see what the suite writes. |
| <a id="nosession"></a>No session with the stub host | `MDC0201`, the session handshake, did not pass. Without a session most checks cannot exercise the plugin. | Run `macrodeck-plugin run --artifact <package> --stub-host` and look for `Session established`. If it never appears, the plugin crashes on start, listens on the wrong address, or does not answer `session.hello`; see [MDC0201](/reference/conformance/#mdc0201), [MDC0704](/reference/conformance/#mdc0704) and [Troubleshooting](/guides/troubleshooting/). |
| <a id="nothingpassed"></a>No check passed | Every check was skipped or failed, so the run most likely never reached the plugin. | As for no session: run the package on a stub host locally and fix what stops it from starting. |
| <a id="notconformant"></a>Required check failed | At least one required check failed. The workflow stops before the upload when this happens, so a build showing it was changed after the suite ran. | Open the failed check's link and apply its fix. Run `macrodeck-plugin test --artifact <package>` until it exits with `0`. |
| <a id="recommendedfailed"></a>Recommended check failed | A recommended check failed. It never makes the plugin non-conformant, but it points at behaviour users notice, such as a late reply or lost log output. | Open the failed check's link and apply its fix. |
| <a id="inconclusive"></a>Check inconclusive | The stub host could not reach a verdict for reasons outside your plugin, such as a slow runner. | Usually nothing. Publish the release again; if the same check stays inconclusive, run it locally with `macrodeck-plugin test --artifact <package> --check <id>`. |
| <a id="otherplugin"></a>Report for another plugin | The report names another plugin id than the build. | Keep one id: the `id` in `manifest.json` must be the id the plugin reports at runtime ([MDC0105](/reference/conformance/#mdc0105)). |
| <a id="otherversion"></a>Report for another version | The report names another version than the build. | Do not override the version at runtime. The workflow writes the release version into `manifest.json` and the assembly version ([MDC0106](/reference/conformance/#mdc0106)). |
| <a id="verdictdisagrees"></a>Verdict contradicts its checks | The report's own `conformant` does not match its checks. | Use the official workflow and do not edit `conformance.json`. |

## Run it before you release

```bash
macrodeck-plugin build --source src/HelloDeck --output ./artifacts
macrodeck-plugin test --artifact ./artifacts/*.macroDeckPlugin
```

This runs the same suite as the release workflow. See [`macrodeck-plugin test`](/cli/test/) for filters
and report formats.
