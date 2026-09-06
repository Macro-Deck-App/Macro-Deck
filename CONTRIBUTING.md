# Contributing

Contributions are welcome. Keep changes focused and avoid unrelated refactoring or formatting.

## Before opening a pull request

- Build affected projects without warnings.
- Run tests for changed observable behaviour or public contracts.
- Add a regression test for a bug fix when practical.
- Verify user-facing changes in the running application when practical.
- Do not break a non-obsolete public SDK, plugin protocol, package, manifest, analyzer, or conformance contract without an explicitly approved breaking change.

Commands and CI guidance are in [engineering/development/building-and-testing.md](engineering/development/building-and-testing.md).

## Compatibility

Plugins are released independently from the host. Existing compiled plugins must continue to work against newer compatible host releases.

Prefer additive changes: new overloads, optional DTO fields, new message types, opt-in capability interfaces, and default interface implementations. Follow the published [deprecation policy](https://docs.macro-deck.app/policies/deprecations/) before removal.

When a change cannot be made without breaking a non-obsolete contract, raise that explicitly before implementation.

## Tests

Test requirements and observable behaviour rather than private implementation structure. Do not add tests purely for coverage, duplicate production algorithms in tests, or mock internal collaborators without an external boundary.

A behaviour-preserving refactor should normally not require test changes.

## Style

Repository formatting and analyzers are authoritative. See [engineering/development/coding-style.md](engineering/development/coding-style.md) and `CLAUDE.md` for conventions that are not mechanically enforced.

## Branches and commits

Create branches from `main`. Use `<type>/<issue-nr>-<short-kebab-case-name>` when an issue exists and omit the issue number otherwise. Common prefixes are `feature/`, `fix/`, `chore/`, `refactor/`, `docs/`, and `ci/`.

Keep commits focused on one logical change.