# ADR 0082: LAN discovery uses the platform's own mDNS responder

Status: Accepted

## Context

The host announced itself on the network in no way at all, so the companion app found a host by probing
every address in the subnet: hundreds of HTTP requests, 10 to 15 seconds, and unreliable. The companion
already browses DNS-SD for `_macrodeck._tcp.local.` and connects to what it finds over plain HTTP, reading
the host's real name and version from the TXT record.

Multicast DNS runs on UDP 5353, and every desktop platform the host supports already runs a responder that
owns that port: mDNSResponder on macOS, Avahi on most Linux desktops, and the DNS client service on
Windows 10 1809 and later. A second responder in the host would share the port with it, answer the same
queries independently, and on macOS in particular contend with the system daemon that already publishes the
machine's own host records.

## Decision

The host registers the service through the platform responder, one small implementation per OS behind
`IServiceAdvertiser`, with no new package:

- **macOS:** `DNSServiceRegister` from libSystem. Deallocating the reference withdraws the service with
  goodbye packets, and mDNSResponder does the same when the host process dies.
- **Windows:** `DnsServiceRegister` / `DnsServiceDeRegister` from dnsapi.dll, available from Windows 10
  1809. The host names itself `<host name>.local` and gives each registration that interface's IPv4 address.
- **Linux:** libavahi-client, Avahi's own binding of its D-Bus API, on a threaded poll. The daemon withdraws
  the entry group when the client disconnects.

What is advertised is decided by one pure planner, not by the platform code:

- The service type is `_macrodeck._tcp`. The SRV port is the public **plain-HTTP** listener and never the
  HTTPS one. No plain-HTTP listener (HTTPS replaces it, or it could not be bound) means nothing is
  advertised.
- The instance name is the machine name that connection-info reports, cut to 63 UTF-8 bytes for the wire.
  The TXT record carries `name` (the full, undeduplicated name) and `version`.
- The service is registered **once per eligible interface**, never on "all interfaces", so a virtual adapter
  never answers for the host. Loopback, tunnel and PPP interfaces, link-local-only interfaces (169.254/16),
  and adapters whose name or description marks them as virtual (Hyper-V, WSL, Docker, VPN tunnels, VM
  bridges and similar) are excluded.

A hosted service reconciles the advertisement after start-up, on every network address or availability
change (debounced), when the setting changes, and every 60 seconds. It only touches the responder when the
plan changed or the responder lost a registration, for example after mDNSResponder or avahi-daemon
restarted. Failure is logged and never stops the host. Advertising can be switched off in Settings >
Network; it applies immediately and needs no restart.

## Consequences

- The host announces its machine name, version and plain-HTTP port to anyone on the LAN without
  authentication, while the plain-HTTP listener is serving and the setting is on. The same information was
  already reachable by probing the port.
- A Linux system without avahi-daemon or libavahi-client, or an older Windows, simply is not advertised; the
  companion still has the QR code, manual entry and its subnet scan.
- A name conflict seen on one link only can rename just that interface's registration on macOS; on Linux,
  where every interface shares one entry group, the whole group is renamed. The TXT `name` keeps the real
  name, which is why the companion reads it.
- A registration the responder rejects is retried at the next periodic check, not at once, so a lasting
  conflict costs one warning and a probe a minute rather than a loop.
- Adapter filtering is a name heuristic. A real LAN bridge named `bridge0` is excluded along with the VM
  bridges the rule targets.
- Unverified on Windows, where no test machine was available: whether registrations survive a hard-killed
  host until their TTL ends, whether same-named registrations on several interfaces conflict with each other,
  and whether Windows' own responder answers `<host>.local` with virtual adapter addresses regardless.
  Windows has no documented automatic rename, so a conflicting registration is logged rather than retried
  under another name.

## Alternatives considered

- **A managed mDNS responder library.** A second responder on UDP 5353 beside the one the OS already runs,
  and a dependency for something every supported platform provides.
- **Talking to Avahi over raw D-Bus.** There is no D-Bus library in the host, and libavahi-client is the
  supported binding of exactly that interface.
- **Registering on all interfaces.** Simpler, but it advertises the host on Hyper-V, WSL, Docker and VPN
  adapters, which the companion cannot reach and must not be offered.
