# Coding Style

Formatting and mechanically enforceable style belong in the configuration that enforces them. Do not duplicate `.editorconfig`, `MacroDeck.slnx.DotSettings`, `Directory.Build.props`, Angular configuration, or linter rules here.

Authoritative configuration:

- [`.editorconfig`](../../.editorconfig) and [`.gitattributes`](../../.gitattributes) for repository-wide text formatting.
- [`MacroDeck.slnx.DotSettings`](../../MacroDeck.slnx.DotSettings) for C# formatting and Rider/ReSharper inspections.
- [`Directory.Build.props`](../../Directory.Build.props) for shared .NET compiler and analyzer settings.
- [`ui/.editorconfig`](../../ui/.editorconfig) and the Angular workspace configuration for UI formatting.
- [`CLAUDE.md`](../../CLAUDE.md) for repository workflow, comments, compatibility, testing, and documentation rules.

If a formatter or analyzer can express a rule, configure it there instead of adding another paragraph to this document.

## Repository conventions

- Use English for code, comments, documentation, commit messages, and UI copy.
- Use plain ASCII hyphens, not typographic dash characters.
- Keep internal code mostly comment-free. Comments explain non-obvious reasons or constraints, never the code immediately below them.
- Keep public SDK XML documentation concise and focused on contracts that are not obvious from signatures.
- Fix warnings and formatter violations at the source instead of suppressing them without a concrete reason.

## Architecture

The host keeps Domain, Application, Infrastructure, and Host responsibilities separate. Dependencies point inward. Built-in integrations live in `MacroDeckHost.Integrations` and consume host capabilities through the SDK or deliberately narrow ports.

Expected failures use the repository `Result` types. Exceptions are for unexpected failures.

For the larger dependency model and trust boundaries, see [architecture.md](../architecture.md).

## Persistence

Database schema changes require an Evolve SQL migration under `host/src/MacroDeckHost.Infrastructure/Persistence/DatabaseMigrations/`. Changing an EF entity alone does not change the schema.

Use the repository's shared persistence JSON options rather than creating per-store serializer options. This keeps persisted data and package/manifest serialization compatible.

## Angular

The desktop UI is the Angular application, and it is zoneless. Async state that affects the view must update a signal or explicitly mark the view for change detection. Prefer signals for component and server-backed state.

Angular-specific reusable UI belongs in `shared`. Anything the framework-free web client also needs - domain models, grid arithmetic, the UI model, the wire protocol, shared stylesheets - belongs in the runtime package under `ui/runtime/` instead, so neither client reimplements it.

Deck widgets use widget-relative metrics rather than root-font-size-based spacing. Follow the existing widget metric helpers and the tests that enforce scale independence.

Destructive user actions require confirmation before data is changed.

## Testing

Tests verify observable behaviour and contracts, not implementation details. A refactor that preserves behaviour should normally not require test changes.

Use the commands and CI guidance in [building-and-testing.md](building-and-testing.md).