# Security Policy

## Supported versions

Security fixes are provided for the latest supported Macro Deck 3 release line. During pre-release development, this means the latest published beta or development release where applicable.

Older releases may no longer receive security updates. Users should reproduce an issue against the latest available version before reporting it when practical.

## Reporting a vulnerability

Do not report security vulnerabilities through public GitHub issues, discussions, Discord, or other public channels.

Use GitHub's private vulnerability reporting for this repository instead:

https://github.com/Macro-Deck-App/Macro-Deck/security/advisories/new

Please include enough information to reproduce and assess the issue, such as:

- affected Macro Deck version or commit
- affected platform
- required configuration or permissions
- reproduction steps or a proof of concept
- expected security impact
- any known workarounds or mitigations

Reports involving authentication, remote access, plugin execution, update/signing infrastructure, or privilege boundaries are especially useful when they clearly describe the trust boundary that can be crossed.

## External plugins

This security policy covers Macro Deck itself, including the host, first-party clients, built-in integrations, official SDK and plugin infrastructure maintained in this repository.

Vulnerabilities that exist only in a third-party plugin are not handled in this repository. Report them privately to the plugin author or through the security reporting process of the plugin's source repository when one is available.

If the affected plugin is distributed through the Macro Deck Store and there is no suitable contact or security reporting channel, report the plugin through the Store so it can be reviewed and, when necessary, restricted or removed from distribution.

If a third-party plugin can exploit a vulnerability in Macro Deck or bypass a security boundary that Macro Deck is expected to enforce, report it here. The fact that a plugin is involved does not make an issue out of scope when the vulnerable component is Macro Deck itself.

Please allow maintainers reasonable time to investigate and prepare a fix before disclosing the vulnerability publicly.
