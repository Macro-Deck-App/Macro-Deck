# ADR 0090: Invited testers install unreviewed test builds

Status: Proposed

## Context

Creators on the Macro Deck Platform can invite up to ten testers to a plugin. A build becomes available to
them once a Version is started from it, before any review, and the Platform lists those builds only for the
signed-in account a creator invited (`GET api/v1/store/tests`). A test build is never signed: the Platform
signs at publication.

Under [ADR 0044](0044-plugin-and-store-trust-enforcement.md) such a build could only be installed as an
unsigned `Url` source while Developer Mode is on, and never over a plugin id already admitted as trusted.
Testing the next version of a plugin a tester already runs from the Store is exactly that case.

## Decision

- A test build is its own source kind, `TestBuild`
  ([IPluginInstaller.cs](../../host/src/MacroDeckHost.Application/Plugins/Installation/IPluginInstaller.cs)).
  It is downloaded like a `Url` source: https only, bounded, and verified against the digest the Platform
  returns.
- Its URL and digest come from the Platform for the signed-in account at the moment the operation runs,
  never from a client or from the package. The host first checks that the Platform lists the build for this
  account ([StoreTestService.cs](../../host/src/MacroDeckHost.Application/Store/Testing/StoreTestService.cs)).
- Unsigned consent is required for every test install and does not depend on Developer Mode. It is given in
  a confirmation that says the build was not reviewed; it travels in memory with the operation like store
  consent, and a failed test install is not retryable, so a new attempt asks again.
- A test build is exempt from the monotonic tier: it may replace a plugin admitted as trusted
  ([PluginTrustPolicy.cs](../../host/src/MacroDeckHost.Application/Plugins/Trust/PluginTrustPolicy.cs)). The
  persisted tier is not lowered, so any later non-test unsigned update is still refused.
- Everything else still applies: a signature that does not verify is refused, the package must carry the
  plugin id it was listed under (`ExpectedPluginId`), and launch-time re-verification is unchanged.
- The host remembers which build it installed per plugin id, so the Tests tab can tell two builds of one
  version apart.

## Consequences

- An invited tester can run code nobody reviewed, on one confirmation. It is bounded by the invitation: a
  creator names the account, the account accepts with its verified address, and the Platform serves the
  build only to that account through a short-lived link.
- A compromised creator account can push an unreviewed build to at most ten invited testers, who still
  confirm each install. It cannot reach anyone through the Store.
- A trusted plugin replaced by a test build runs unsigned until the tester installs a Store version again.

## References

- [ADR 0044](0044-plugin-and-store-trust-enforcement.md)
- Platform: tester invitations and `api/v1/store/tests` in the Macro Deck Platform architecture documentation
