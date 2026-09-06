import { DEFAULT_WEB_CLIENT_TARGET } from '@macro-deck/runtime';
import { CAR_THING_TARGET } from './carthing/carthing.target';
import { ACTIVE_TARGET } from './active-target';

describe('web client targets', () => {
  it('leaves the ordinary browser client exactly as it was', () => {
    expect(ACTIVE_TARGET).toBe(DEFAULT_WEB_CLIENT_TARGET);
    expect(ACTIVE_TARGET.hostEndpoint.kind).toBe('origin');
    // No controls declared is what keeps the input layer inert and the focus ring off.
    expect(ACTIVE_TARGET.hardwareInput).toBeUndefined();
  });

  it('looks for the host on the tunnel before falling back, on a device with no discovery', () => {
    const endpoint = CAR_THING_TARGET.hostEndpoint;
    expect(endpoint.kind).toBe('probe');

    const candidates = endpoint.kind === 'probe' ? endpoint.candidates : [];
    // A fixed contract rather than the configured public port: a device on USB has no channel to
    // learn that the port changed (ADR 0030).
    expect(candidates.map(candidate => candidate.baseUrl)).toEqual([
      'http://127.0.0.1:8193',
      'http://127.0.0.1:8194',
      'http://127.0.0.1:8195',
      'http://127.0.0.1:8196',
    ]);
  });

  it('turns off what an appliance on a forwarded port cannot do', () => {
    const capabilities = CAR_THING_TARGET.capabilities;

    // Plain HTTP through the tunnel is not a secure context, so a worker could never register - and
    // there is no certificate in that path for the setup prompts to be about.
    expect(capabilities.serviceWorker).toBeFalse();
    expect(capabilities.deviceSetupPrompts).toBeFalse();
    // The firmware already keeps the screen lit; it is the whole point of the device.
    expect(capabilities.wakeLock).toBeFalse();
    expect(capabilities.clientSettings).toBeTrue();
  });

  it('maps every physical control the device actually has', () => {
    const input = CAR_THING_TARGET.hardwareInput;
    expect(input).toBeDefined();

    const bound: Record<string, string> = {};
    (input ? input.keys : []).forEach(binding => {
      bound[binding.key] = binding.event.kind;
    });

    // The four presets aim and press; menu and back both leave; the wheel press activates.
    expect(bound['1']).toBe('selectIndex');
    expect(bound['4']).toBe('selectIndex');
    expect(bound['m']).toBe('back');
    expect(bound['Escape']).toBe('back');
    expect(bound['Enter']).toBe('activate');

    // A free-spinning encoder: one detent is well under a mouse notch, so steps come from
    // accumulated delta rather than from each event.
    expect(input && input.wheel ? input.wheel.stepPx : 0).toBe(40);
  });
});
