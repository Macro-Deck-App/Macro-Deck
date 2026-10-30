# ADR 0113: The MCP server is an untrusted principal

Status: Accepted

## Context

AI clients (Claude, VS Code and others) should be able to read a user's setup, build widgets and flows
and run actions through the Model Context Protocol. The admin REST API cannot be handed to them: it
reveals secrets, exports archives with secrets and exposes backups and the key ring. An AI client acts
on text it reads from web pages, files and tool results, so whatever it can do, a prompt injection can
make it do.

The MCP specification moved in the `2026-07-28` revision to stateless Streamable HTTP, where
elicitation is a multi round-trip result instead of a server-to-client request; clients on earlier
revisions still need a session.

## Decision

- The host serves MCP itself, at `/mcp` on the public listener and on the private loopback listener, as
  a host feature and not as a built-in integration. The public port is stable, so a client configuration
  does not go stale, and a client on another computer in the network can connect. Tools call the existing transport handlers and services; they never call
  the host's own REST API.
- A new scope `mcp` with its own token type, created and revoked by the user. The token opens only
  `/mcp`, and no other credential opens `/mcp`. `Origin` and `Sec-Fetch-Site` are checked on every
  request, and `Host` on the loopback listener, so browser pages cannot reach it. Sessions are bound to the token that created them.
- The tool set is an explicit allowlist and a public contract, `macrodeck-mcp/1`: names, arguments,
  result fields and error codes are additive-only. Titles and descriptions are English contract text.
- No tool returns or decrypts a secret. Secrets are references; a new secret is typed by the user into a
  one-time host-rendered page reached through URL-mode elicitation, so the value never passes the
  client. Writes by the MCP principal go through the secret-approval mechanism as an untrusted writer.
- What a client builds is checked when it is written, not only when it runs: the user's per-provider
  action policy (allow, ask first, off) applies to new and changed blocks of every flow the host may later
  run on its own, and to everything a run would reach through nested scripts and event triggers.
- A value that an approved secret-using block reads (a variable, a script input, an event parameter)
  changes through the MCP only with the user's consent. Writes during a run are attributed to the
  provider whose action runs for the MCP, for the run and a fixed grace period, at the host's single
  variable write sinks; the plugin protocol carries no execution id, so attribution is by provider.
- Every applied batch is journaled with before-images for undo (last 20 batches, outside every backup
  group). Undo restores deleted objects with their original ids and secrets and puts back exactly the
  approvals recorded with them.
- Elicitation is written as a multi round-trip request. The transport runs in the hybrid session mode:
  `2026-07-28` clients are served statelessly, earlier clients get a session and the SDK resolves the
  same request over it.

## Consequences

- An MCP client can never obtain a secret value through a tool; a compromised client is limited to
  what the allowlisted tools do, and the user can cut it off by revoking its token.
- Every new tool is a contract addition, documented in `docs/src/content/docs/reference/mcp.md`, and must
  keep the no-secret rule; a reflection test refuses tools that depend on secret-returning handlers.
- Attribution by provider fails closed: a write the same provider makes for another reason during an MCP
  run is refused, and a write an accepted action makes after the grace period is not attributed.
- Anyone in the local network who holds a token can use it, and over plain HTTP a token or a secret
  typed into the secret page can be read in transit on an untrusted network.
- Sessions for pre-`2026-07-28` clients depend on a deprecated SDK mode that can be dropped once those
  clients are gone.

## References

- `host/src/MacroDeckHost/Mcp/`
- [MCP server reference](../../docs/src/content/docs/reference/mcp.md)
