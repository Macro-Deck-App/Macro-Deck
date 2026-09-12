# ADR 0026: The plugin boundary is a versioned JSON protocol with a DI-first .NET SDK over it

Status: Accepted

## Context

Moving plugins out of process turns the SDK boundary into a public wire protocol. That protocol must be
implementable outside .NET, tolerate newer peers where possible, and stay independent of first-party UI
transport details. At the same time .NET plugin authors should not have to implement session framing,
reconnection, heartbeats, dispatch or authentication themselves.

The plugin→host direction needed a shape of its own: eight host APIs with roughly twenty operations
between them ([#413](https://github.com/Macro-Deck-App/Macro-Deck/issues/413)), and an answer to what
travels inside them — the SDK's own rich types being the obvious first candidate.

Testing a plugin against the contract is only useful if a plugin author's own test project and a CI
pipeline with no .NET test code get the same answer, and if a check's identity outlives whichever tool
runs it ([#416](https://github.com/Macro-Deck-App/Macro-Deck/issues/416)).

## Decision

### Standard HTTP and WebSocket, JSON only

Bootstrap and request/response use ordinary HTTP; the long-lived session is an RFC 6455 WebSocket. JSON
is the only wire encoding. SignalR is deliberately not used on this boundary: a plugin author should
need only common HTTP, WebSocket and JSON libraries in their language.

The protocol negotiates **one integer major version** through the session handshake, which is the single
version-negotiation channel — the WebSocket sub-protocol string names the protocol *family*, not the
major, so it cannot disagree with the handshake. Additive fields, message types and capability kinds
stay compatible within a major; a breaking change requires a new major while the previous one remains
negotiable under the compatibility policy.

Unknown optional fields are ignored. An unknown message type produces a protocol error without
terminating an otherwise valid session, and malformed input is a protocol error rather than an escaping
parser exception. Capability support negotiates independently, so an unsupported capability degrades the
session instead of making the whole plugin incompatible.

Delivery is at-most-once: there is no replay log and no sequence window, so a requester uses idempotency
keys when retrying an operation that must not execute twice.

`MacroDeck.Plugin.Protocol` is a host-independent contract package that may depend on shared SDK
identity contracts but never on host implementation assemblies. The machine-readable OpenAPI and
AsyncAPI specifications and the protocol constants are compatibility artifacts kept synchronized by
tests. Plugin WebSocket authentication uses explicit protocol credentials, never browser cookies or
query-string conveniences.

### Host callbacks are one generic invoke pair carrying protocol DTOs

All host APIs share one `host.invoke` / `host.result` pair, mirroring how `capability.invoke` is already
generic over every capability kind. Adding a host API costs an `api` string, an `operation` string and a
routing branch — no version bump, no new message type, no new pinned literal. The alternative, one pair
per API, would have meant a permanent message-type entry, a direction row, an AsyncAPI schema and a
pinned literal each, every one carrying its own correlation and timeout handling to keep in step with
the others by hand. What varies between a `deck` call and a `notifications` call is the strings and the
argument shape, not anything the envelope, correlation or backpressure layers need to know.

**The payload is a hand-written protocol DTO, never a serialized SDK type**, for three reasons that
converge:

- Some SDK types are not deserializable at all — `ConfigFlowResult`'s constructor is private, factory-only.
- The shared serializer registers no string enum converter, so every SDK enum would serialize by ordinal
  and its *declaration order in the source file* would silently become part of the wire contract.
  Reordering a case for readability would be a wire break no compiler warns about.
- The SDK is a NuGet package under ordinary semver; the wire is append-only within a major. Serializing
  an SDK type directly fuses those two evolution rules, so a source-compatible refactor could change what
  goes over an already-shipped wire version.

`host.invoke`/`host.result`/`host.cancel` inherit the same backpressure exemption the capability pair
has, and for the mirrored reason: a plugin's capability handler is often synchronously blocked on
`host.result` before it can produce its own reply, so pausing either side would live-lock it.

### A plugin is a DI-first ASP.NET Core application

`MacroDeck.Plugin.Hosting` wraps `WebApplicationBuilder` rather than replacing ASP.NET Core, so a plugin
configures services, registers integrations, builds and runs, with protocol details behind the SDK and
the underlying builder still reachable for advanced scenarios.

Capability execution uses generic handlers and a common dispatcher instead of transport code that knows
every capability kind; each invocation gets its own DI scope and produces exactly one correlated reply.
Identity and metadata come from `manifest.json`, never duplicated builder properties, and local
configuration problems — invalid identity, duplicate capability ids, reserved routes, invalid service
graphs — fail during build, before a protocol session starts. `/_macrodeck/*` is reserved for
hosting and runtime endpoints. Managed and self-registering modes are explicit, never inferred.

### Conformance is a framework-agnostic suite, and check ids are frozen

The suite's core is a plain library producing a `ConformanceReport` and **referencing no test-framework
assembly** — an invariant asserted by a test over the assembly's own references, not merely described
here. Every check derives from a common base and returns one of four result factories, never an
`Assert`. This repository's NUnit fixture and the CLI's `test` verb are both thin consumers of one
runner, and a third party's xUnit project is a third such consumer this package never had to write.

That is what makes a check id a stable public contract: an id is a plain constructor argument that
exists independently of whatever discovers it, so the class behind it can be renamed freely — where an
NUnit test name, discovered by reflection, promises nothing across a rename or a merge into a
parameterized test. A shipped id is never reused for a different rule and never changes meaning.

`Required` versus `Recommended` is a property of the check, not of who runs it, so an NUnit run, a CLI
run and a third party's run cannot disagree about whether the same subject conforms. A subject is
started for real in all three forms — in-process, launched executable, extracted artifact — and a test
asserts all three produce the same check ids and the same verdict.

## Consequences

- Plugins can implement the protocol in any language with ordinary HTTP, WebSocket and JSON support, and
  Macro Deck intentionally keeps separate real-time transports for first-party UI and third-party plugins.
- Error codes, message types, existing field meanings and conformance check ids are append-only within a
  major. Renumbering a check id carries the same breaking consequences as renaming a wire message type.
- Messages lost during a disconnect are not replayed. A future envelope or sequencing redesign is a
  protocol-major change.
- Every conversion between an SDK type and its wire DTO is a named mapper a reviewer can read end to end,
  rather than attributes quietly steering reflection-based serialization of the SDK type itself. A plugin
  author never sees a DTO: the remote context and its host-API proxies convert internally, so
  `IIntegrationContext` looks identical in-process and remote.
- Each conformance check pays for framework independence in ceremony — no `[Test]` shortcut, no fluent
  assertions — which is the deliberate cost of a portable verdict.
- Adding a check costs nothing beyond registering it: every existing adapter, including one a third party
  wrote, discovers it automatically.

## Alternatives considered

- **One message-type pair per host API.** Eight permanent entries in four pinned catalogues, each with
  its own correlation and timeout handling, and a ninth API later would need a version bump.
- **REST, one endpoint per operation, instead of a WebSocket callback.** A plugin's handler is already
  inside an in-flight invoke on the WebSocket; a second transport mid-handler adds a second auth path and
  a second failure mode for no benefit.
- **Serialize SDK types directly and work around the edge cases.** Would mean weakening an SDK type's API
  for a reason SDK consumers never see, or duplicating the DTO mapping with an extra converter in between.
- **A hybrid: SDK types where they deserialize, DTOs elsewhere.** A contract that is "usually the SDK
  type, except where it isn't" is one a non-.NET implementation cannot read off a single rule.
- **Write the conformance suite as NUnit tests.** Cannot satisfy the xUnit requirement without a second
  parallel implementation of every check.
- **Let the CLI shell out to `dotnet test`.** Packaging a throwaway project and parsing TRX to reach a
  report the CLI can build in-process by calling the runner.

## References

- [Issue #409](https://github.com/Macro-Deck-App/Macro-Deck/issues/409),
  [Issue #410](https://github.com/Macro-Deck-App/Macro-Deck/issues/410),
  [Issue #413](https://github.com/Macro-Deck-App/Macro-Deck/issues/413),
  [Issue #416](https://github.com/Macro-Deck-App/Macro-Deck/issues/416)
- [Protocol reference](https://docs.macro-deck.app/reference/protocol/),
  [Plugin hosting guide](https://docs.macro-deck.app/reference/plugin-hosting/),
  [Compatibility policy](https://docs.macro-deck.app/policies/compatibility/)
