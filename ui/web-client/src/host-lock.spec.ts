import { HttpClient } from '@macro-deck/runtime';
import { HostLock } from './host-lock';

describe('host lock', () => {
  let resolvers: Array<(state: unknown) => void>;
  let http: HttpClient;

  beforeEach(() => {
    resolvers = [];
    http = {
      get: () => new Promise(resolve => resolvers.push(resolve)),
    } as unknown as HttpClient;
  });

  const settle = async (): Promise<void> => {
    for (let turn = 0; turn < 4; turn++) await Promise.resolve();
  };

  it('shows nothing on a host that locked without the setting on', () => {
    const lock = new HostLock(http);

    lock.apply({ locked: true, lockScreenEnabled: false, supported: true });

    // The setting is what decides whether locking the host shows anything at all.
    expect(lock.showLockScreen()).toBeFalse();
  });

  it('shows the lock screen only when both the lock and the setting say so', () => {
    const lock = new HostLock(http);

    lock.apply({ locked: true, lockScreenEnabled: true, supported: true });
    expect(lock.showLockScreen()).toBeTrue();

    lock.apply({ locked: false, lockScreenEnabled: true, supported: true });
    expect(lock.showLockScreen()).toBeFalse();
  });

  it('re-reads the state on demand, for the lock that happened while the socket was down', async () => {
    const lock = new HostLock(http);
    void lock.load();

    resolvers[0]({ locked: true, lockScreenEnabled: true, supported: true });
    await settle();

    expect(lock.showLockScreen()).toBeTrue();
  });

  it('keeps a push that overtook the request it raced', async () => {
    const lock = new HostLock(http);
    void lock.load();

    // The machine was unlocked while the snapshot was in flight.
    lock.apply({ locked: false, lockScreenEnabled: true, supported: true });
    resolvers[0]({ locked: true, lockScreenEnabled: true, supported: true });
    await settle();

    // Applying the older snapshot would leave a lock screen over a machine nobody locked, until the
    // next lock or unlock - which could be a day away.
    expect(lock.showLockScreen()).toBeFalse();
  });

  it('leaves the state alone when the host cannot be asked', async () => {
    const lock = new HostLock({ get: () => Promise.reject(new Error('offline')) } as unknown as HttpClient);
    lock.apply({ locked: true, lockScreenEnabled: true, supported: true });

    await lock.load();

    expect(lock.showLockScreen()).toBeTrue();
  });
});
