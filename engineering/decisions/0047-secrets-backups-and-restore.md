# ADR 0047: Secrets at rest, encrypted backups, and a staged boot-time restore

Status: Accepted

## Context

Every secret in an installation — integration credentials, OAuth tokens, the Connect refresh token — is
an ASP.NET Data Protection ciphertext in the `secret` table. The key that opens all of them is the Data
Protection key ring under `<data>/keys/`. The auth signing key and the public listener's TLS private key
are protected by that same ring without being `secret` rows, so the ring is the single root of the
installation's credentials. It was configured with no `ProtectKeysWith*` call: on Windows that still
means DPAPI, but on macOS and Linux it meant plaintext XML carrying raw key material, with file
permissions as the only protection ([#672](https://github.com/Macro-Deck-App/Macro-Deck/issues/672)).

Full-installation backups ([#36](https://github.com/Macro-Deck-App/Macro-Deck/issues/36)) must carry the
whole installation including that key ring, because the ring is what makes the `secret` rows readable at
all. A future cloud storage provider must never see plaintext, so encryption has to happen before any
provider is involved rather than being a property one provider adds and another forgets. The existing
portable-archive crypto has the right posture but holds plaintext and ciphertext as one `byte[]`, caps
the payload, and derives its key from a password — none of which carries over to an archive produced
synchronously before every update with no natural size ceiling.

Restore is constrained by what a running host holds open: the database, the key ring and plugin binaries
cannot be replaced while it is up, a partial restore must never leave a half-swapped installation, and
an older archive must not apply an outdated schema over a newer one.

Two facts about Data Protection shape the key-ring half of this decision.

**A key the host cannot decrypt fails silently, not loudly.** The default key resolver swallows a
decryption failure and returns "ineligible"; the key ring provider then finds no default key and, with
automatic generation on, writes a fresh one — orphaning every existing secret while the host looks
healthy. A design that guesses wrong about whether a key-encryption key exists does not fail; it
destroys the installation quietly.

**The decryptor's type name is an on-disk contract.** Data Protection writes the decryptor's
assembly-qualified name — assembly version included — into every key file it encrypts and resolves it
with `Type.GetType` on read.

## Decision

### The key ring is wrapped by a key held in the platform keystore

A random 256-bit key-encryption key seals each key file with AES-256-GCM directly — it is already 256
uniformly random bits, so a KDF would add nothing — binding a versioned purpose string plus the KEK's own
id as associated data. That id is an HKDF derivation of the KEK, so a key file names the key that opens
it and a wrong key is distinguishable from corruption before anything is decrypted.

The KEK lives in the Windows Credential Manager (additionally DPAPI-protected), the macOS keychain, or
the Secret Service over libsecret, soft-loaded so a machine without it still starts. The item is keyed on
the installation id, so two checkouts cannot write over each other.

**A read returns `Found`, `NotFound` or `Unavailable`, and the last two are never collapsed.**
`NotFound` means the entry can be recreated from the escrow; `Unavailable` means the backend could not
answer and nothing may be rewritten. Treating a locked keychain or an absent D-Bus session as "no entry"
would mint a second KEK and strand every secret the first one protects.

The KEK is escrowed under the backup recovery key, as a **list** of wraps each naming the recovery key
that opens it. It is a list because regenerating a recovery key must not open a window in which the
installation is unrecoverable: a regeneration adds a wrap and keeps the previous one, pruned only once
the replacement has been exported, and a prune that would empty the escrow is refused. For the same
reason the ring is not wrapped at all until the recovery key has been exported. Migration writes the
escrow first, then the keystore entry, then the key files, so from the first write onward the KEK is
recoverable from the recovery key alone, and every rewritten file is verified by decrypting it back
before anything on disk is replaced.

The ring is then `Unprotected` (always reported with the reason — no keystore, no exported recovery key,
or a portable installation), `Protected`, or `Locked`. **A portable installation is never wrapped**:
binding a portable data root to a per-user keystore would defeat the one property portable mode exists
for. In `Locked`, Data Protection is pointed at a scratch directory with automatic generation disabled,
so the host creates, rotates and overwrites nothing under `keys/`; it answers 503 for the API apart from
the key ring status and unlock endpoints, the readiness probes the bootstrapper waits on, shutdown and
localization, and it leaves the plugin WebSocket unmapped. The desktop unlock gate takes the exported
recovery key, restores the KEK and restarts.

The decryptor's type name may not be renamed or moved, and its assembly version is pinned, because an
unresolvable type is exactly the input to the silent failure above.

### Backups are streamed and AEAD-encrypted under the recovery key

A `.macroDeckBackup` is one outer ZIP holding a plaintext `manifest.json` and an encrypted `payload.enc`.
The manifest bytes are the associated data of the key wrap, so the manifest cannot be edited
independently of the payload it describes — which also makes an encryption-version downgrade impossible,
since swapping in a weaker `Encryption` block invalidates the wrap tag.

`payload.enc` is an inner, component-addressable ZIP encrypted with AES-256-GCM in the STREAM
construction, so the payload is produced and consumed as a stream instead of held whole in memory, and
the final-segment flag stops a truncated payload from decrypting to a shorter, silently incomplete
backup.

A per-archive random data key is wrapped by a key derived with HKDF-SHA256 from a 256-bit recovery key,
generated once per installation, stored only through the secret service and exported to the user as a
checksummed string they are expected to keep outside the installation. HKDF replaces a password KDF
because the recovery key already carries 256 bits of entropy; stretching it would only add latency to a
backup that must complete synchronously on the pre-update path.

The manifest cannot carry a digest of the payload it authenticates, for the reason the portable format
already documents: the payload does not exist until the manifest bytes have been consumed as associated
data. Authenticated integrity therefore lives entirely inside the AEAD.

### Storage is a provider abstraction that only ever sees ciphertext

`IBackupStorageProvider` is shaped around what a *remote* provider needs, not around what the local one
happens to do. Backups are identified by opaque provider-defined storage ids, never filesystem paths.
The write contract takes a **re-invokable content callback plus a required expected length** rather than
a single stream, so a cloud provider can own its own chunking, retry and multipart loop — a failed
attempt re-invokes the callback for a fresh stream instead of the core buffering and replaying bytes on
its behalf. Capabilities and availability are first-class metadata rather than assumptions baked into the
caller, and backup identity is provider-independent.

Encryption happens before a provider is ever called, so a provider receives an opaque ciphertext stream
and has no way to distinguish a backup's contents from random data. The zero-knowledge property is
enforced by the interface signature, not by convention.

### Restore is a staged, boot-time swap of coarse component groups

**Prepare (online).** Decrypt and validate the *entire* archive, not just the selected components. Stage
it, run the database migrator against the staged copy so an older archive is migrated up before anything
is applied, expand the selection to its dependencies, take a protected safety backup, and only then write
a pending-restore intent.

**Commit (online).** Request a restart through the mechanism
[ADR 0040](0040-public-listeners-and-tls.md) provides.

**Apply (startup).** Before the database is opened, under the applier's own exclusive lock: file-backed
groups swap by directory move with a flushed journal and reverse-replay rollback; database-backed groups
apply table-wise from the staged database in one transaction over an explicit column list. The migration
history table is never restored, so a restored installation keeps its own record. A denylist protects the
recovery-key pointer and the installation id from being overwritten by a partial restore.

Selection is nine coarse groups, each replaced wholesale rather than merged field by field, with
dependencies auto-selected.

## Consequences

- The threat the key ring wrapping addresses is the **incidental copy** — a Time Machine or file-level
  backup, a folder-sync client, a `cp -r` of the data directory. On Windows and Linux any process running
  as that user can read the keystore entry; it does not defend against malware running as the same user,
  which [ADR 0003](0003-loopback-trust-token-scopes-and-device-identity.md) already places outside this
  boundary.
- **macOS ended up stricter than intended, and that is shipped behaviour rather than a defect.** The
  intent was an entry readable by any application running as that user. It proved unachievable and was
  verified empirically: `SecItemAdd` does not honour `kSecAttrAccess`, and the ACL alternatives left a
  second binary blocked on an authorisation prompt. Reaching it would require the fully deprecated
  `SecKeychain*` family. The keychain's default ACL therefore trusts the binary that created the entry:
  the signed shipping app reads its own entry across updates without prompting, while an unsigned
  development build prompts after every rebuild — a further reason a development installation is left
  unprotected.
- Locked mode is observable from outside the host: a plugin sees 503 and no WebSocket to connect to. That
  is new behaviour in a state which previously destroyed the installation's secrets instead.
- The archive ships `keys/` **verbatim** rather than unprotecting and re-protecting secrets the way a
  portable archive does. The ring is what makes the `secret` rows readable, and re-protecting it alone
  would still leave the TLS private key and the auth signing key unrecoverable, since neither is a
  `secret` row. The cost is explicit: anyone holding both the archive and the exported recovery key holds
  the installation's credentials in full. A restored installation whose keystore holds no entry comes up
  locked until the recovery key is supplied — the same key that decrypted the archive.
- Losing both the local key ring and the exported recovery key makes every existing backup permanently
  unreadable. That is intended: a recoverable or escrowed key would undermine the zero-knowledge property.
  What is escrowed is the ring's KEK, wrapped *under* the recovery key; the direction is the whole point.
- The required expected length forces the core to stage a finished container before storage begins, so a
  provider is never asked to accept a backup of unknown size. Adding a cloud provider is expected to be
  one class plus one registration. Retention runs per provider.
- Restore always costs a restart; when no shell is available to request one, applying is deferred rather
  than refused. A staged restore that fails re-validation at boot is abandoned and the current
  installation boots untouched, and an archive whose schema is newer than the running build is refused.
- The archive is not a quiesced point-in-time snapshot: the database is transactionally consistent and
  each file is complete, but the two were not captured at one instant. Changes made after a restore is
  confirmed are lost — that is what restore means. Wholesale group replacement means a restore cannot
  merge one automation from the archive with a newer one on disk.
- An escrow can name a recovery key the user has already replaced, because the previous wrap is retained
  until the new one is exported. A stale wrap is the smaller problem.

## References

- [Issue #36](https://github.com/Macro-Deck-App/Macro-Deck/issues/36),
  [Issue #672](https://github.com/Macro-Deck-App/Macro-Deck/issues/672)
