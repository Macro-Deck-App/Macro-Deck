# Adding a Web Client target

A **target** is a device-specific flavour of the Web Client: the same application, built with a
different manifest. It exists so a device with unusual runtime, transport or hardware requirements
can be supported without forking the client or pushing device behaviour into the default build.

A target is a manifest plus a build configuration, never a second application: one project emits every
artifact, targets are served as siblings under `/targets/<id>` and discovered from disk, and the client
core consumes normalized input intents rather than device events. A device dials its own end of the
reverse tunnel, which terminates on the public listener
([ADR 0030](../decisions/0030-android-usb-connections-over-adb.md)) - never the loopback one.

> **The Car Thing target is experimental.** It works end to end on a real device, but it does not
> cover every community firmware, and the parts that touch the device were arrived at by reading one
> of them. Treat its defaults as a starting point.

Targets are first-party and live in this repository. They are not a plugin extension point, so nothing
here is a compatibility commitment.

---

## What a target may change

Everything is declared as data on a
[`WebClientTarget`](../../ui/runtime/src/client-target/client-target.ts), which ships in the
framework-free runtime package rather than in the client:

| Field | Purpose |
| --- | --- |
| `id` | Matches the directory the host serves the build from, `/targets/<id>` |
| `hostEndpoint` | `origin` (the browser default) or `probe` with the addresses to try |
| `hardwareInput` | The device's keys and rotary controls, and what each means |
| `capabilities` | What the device cannot do: service worker, wake lock, client settings, setup prompts |

Nothing in the client core learns about any device. A target says its wheel exists and that turning it
moves the focus; the generic sources translate the events without knowing what produced them.

## Step 1: the manifest

Create `ui/web-client/src/targets/<id>/<id>.target.ts` and export a `WebClientTarget`.
[`carthing.target.ts`](../../ui/web-client/src/targets/carthing/carthing.target.ts) is the worked
example.

Physical controls are bound by `KeyboardEvent.key` and by accumulated wheel delta:

```ts
hardwareInput: {
  keys: [
    { key: '1', event: { kind: 'selectIndex', index: 0 } },
    { key: 'Escape', event: { kind: 'back' } },
    { key: 'Enter', event: { kind: 'activate' } },
  ],
  wheel: { stepPx: 40 },
}
```

`stepPx` is how much accumulated delta makes one focus step. A free-spinning encoder reports far less
than a mouse notch per detent, so this is what stops one flick running the cursor across the deck.

If the device is reached through a forwarded port, declare the addresses to try. The resolver always
falls back to the page origin, so an ordinary LAN visit keeps working:

```ts
hostEndpoint: { kind: 'probe', candidates: [{ baseUrl: 'http://127.0.0.1:8193' }] }
```

## Step 2: the swap module

Add `ui/web-client/src/targets/<id>/active-target.ts` re-exporting the manifest as `ACTIVE_TARGET`,
with the same shape as the default
[`active-target.ts`](../../ui/web-client/src/targets/active-target.ts). This is the one module a
target build replaces: [`build.mjs`](../../ui/web-client/build.mjs) resolves
`./targets/active-target` to the target's copy, so each artifact compiles in exactly one target and
the default build carries none of them.

That is the whole per-target surface. The shell, the assembled stylesheet and the service worker are
the same files for every build; the build rewrites the shell's `<base href>` to the path the host
serves the target from. A device that needs something else needs a new field on `WebClientTarget`,
not a second shell.

## Step 3: the build script

The build takes the target from `--target=<id>` and writes `dist-<id>/`. Add the script to
[`ui/web-client/package.json`](../../ui/web-client/package.json) beside the existing ones:

```
node build.mjs --target=<id>
```

The id has to match the host's own id rule - a single lower-case path segment - because it becomes
both a route template and a file path. The build rejects anything else.

Add the script to the device-target step in [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)
as well: a target nobody builds is a target that stops building without anyone noticing until a
device asks for it.

## Step 3a: an old device browser

Check what the device actually runs before assuming the modern bundle works there - a jailbroken Car
Thing turned out to be on Chromium 69, which parses neither optional chaining nor nullish
coalescing and shows a black screen rather than an error.

Every build, target or not, emits a down-levelled ES5 entry beside the modern one
([`legacy/`](../../ui/web-client/legacy/)), and the modern shell carries a feature-detect gate that
redirects an engine which cannot run it to the host's `/legacy/` route. That route is the default
client's down-levelled shell, so the gate takes such a device out of the target it was serving.
Check the gate's conditions against the engine the device actually runs before relying on it.

## Step 4: packaging

The artifact has to reach `wwwroot/targets/<id>` in **both** places that assemble it:

- [`.github/workflows/build.yml`](../../.github/workflows/build.yml), the release step
- [`ci/scripts/stage-host.sh`](../../ci/scripts/stage-host.sh), local staging

The host discovers targets from disk, so no host change is needed to serve one.

[`ci/scripts/verify-web-client-targets.test.mjs`](../../ci/scripts/verify-web-client-targets.test.mjs)
checks that the manifest, the build script, the CI step that runs it and both copy steps agree. Drift
there fails silently in production - the target serves the default client, or 404s - so run it after
any change here.

## Step 5: provisioning, if the device needs it

Optional. Implement
[`IWebClientTargetProvisioner`](../../host/src/MacroDeckHost.Application/ClientTargets/IWebClientTargetProvisioner.cs)
and register it in `AddWebClientTargets`. The host sequences the steps and the desktop UI renders
whatever comes back, so a new target needs no UI change.

Two rules matter more than the rest:

- **Device-specific commands belong to the provisioner**, never to a generic host service.
- **Anything the device cannot be trusted to survive is guided, not automated.** The Car Thing's
  flashing step is instructions and a link, because it needs a button combination on the device and
  bricks it when it goes wrong.

Prefer **detecting** what a device runs over asking the user for it: `CarThingFirmwareDetector` reads
the device's service list and web root and pre-fills the step's fields, which stay editable for the
variant nobody has seen. Keep detection to a couple of read-only `AdbQueryCommand`s - a minimal
`adbd` does not survive being interrogated.

Appliance firmwares tend to ship a **read-only root filesystem**, so a configuration write needs
`AdbRemountRootWritableCommand` before it. They also do not necessarily run systemd - the Car Thing's
community firmwares drive their browser through `supervisord` - which is why the init system is a
field on the step rather than an assumption in the code.

If the provisioner talks to the device over adb, extend the closed `AdbCommand` union rather than
adding a way to run an arbitrary device-side command
([ADR 0030](../decisions/0030-android-usb-connections-over-adb.md)). Values reaching a device-side
shell go through `AdbShellQuote.Quote`, and paths are validated against a closed character set.

**Never point a device at the host's loopback listener.** A tunnelled request arrives from
`127.0.0.1` and would be granted synthetic admin. The device dials its own end of the reverse tunnel,
which terminates on the public listener.

## Verification

```bash
cd ui/web-client && npm run build:<id>
```

```bash
node --test ci/scripts/verify-web-client-targets.test.mjs
```

Every user-facing string a provisioner produces needs a key in
`host/src/MacroDeckHost.Localization/Localization/` with a translation in every shipped language, then:

```bash
MACRODECK_UPDATE_GENERATED=1 dotnet test sdk/tests/MacroDeck.Localization.Tests.UnitTests
```

The input path can be exercised without the hardware: build the target, open it in a browser, and
drive it with the keys and scroll wheel the manifest binds.
