import { HostEndpointCandidate, WebClientTarget } from '@macro-deck/runtime';

const TUNNEL_PORTS = [8193, 8194, 8195, 8196];

const TUNNEL_CANDIDATES: readonly HostEndpointCandidate[] =
  TUNNEL_PORTS.map(port => ({ baseUrl: `http://127.0.0.1:${port}` }));

// Spotify Car Thing (issue #727) runs Chromium 69, so this target ships the down-levelled build:
// Chromium 69 has neither optional chaining nor nullish coalescing, and a bundle carrying either
// fails to parse there - a black screen, not a layout problem. The key mapping is community-
// documented data: a device reporting something else is a change here, not in the input layer.
export const CAR_THING_TARGET: WebClientTarget = {
  id: 'carthing',
  hostEndpoint: { kind: 'probe', candidates: TUNNEL_CANDIDATES },
  hardwareInput: {
    keys: [
      { key: '1', event: { kind: 'selectIndex', index: 0 } },
      { key: '2', event: { kind: 'selectIndex', index: 1 } },
      { key: '3', event: { kind: 'selectIndex', index: 2 } },
      { key: '4', event: { kind: 'selectIndex', index: 3 } },
      { key: 'm', event: { kind: 'back' } },
      { key: 'Escape', event: { kind: 'back' } },
      { key: 'Enter', event: { kind: 'activate' } },
      { key: ' ', event: { kind: 'activate' } }
    ],
    // The wheel is a free-spinning encoder: one detent is well under a mouse notch, so steps are
    // taken from accumulated delta rather than from each event.
    wheel: { stepPx: 40 }
  },
  capabilities: {
    // The device is reached over plain HTTP through a forwarded port, which is not a secure context,
    // so a worker could never register anyway - and an appliance has nothing to install itself onto.
    serviceWorker: false,
    // The screen is the whole point of the device and its firmware already keeps it lit.
    wakeLock: false,
    clientSettings: true,
    // Reached over plain HTTP through the ADB tunnel, so there is no certificate to trust and the
    // banner would ask the user to fix something that is not broken - on a screen with no keyboard.
    deviceSetupPrompts: false
  }
};
