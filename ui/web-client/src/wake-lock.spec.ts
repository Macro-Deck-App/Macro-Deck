import { WakeLock, WakeLockBackend, WakeLockHandle } from './wake-lock';

describe('wake lock', () => {
  interface Requested {
    resolve(handle: WakeLockHandle): void;
    reject(): void;
  }

  let requests: Requested[];
  let released: number;
  let backend: WakeLockBackend;
  let hidden: boolean;

  function handle(): WakeLockHandle & { fireRelease(): void } {
    let onRelease: () => void = () => undefined;
    return {
      release: () => { released++; return Promise.resolve(); },
      onRelease: callback => { onRelease = callback; },
      fireRelease: () => onRelease(),
    };
  }

  beforeEach(() => {
    requests = [];
    released = 0;
    hidden = false;
    window.localStorage.clear();
    Object.defineProperty(document, 'hidden', { get: () => hidden, configurable: true });
    backend = {
      request: () => new Promise<WakeLockHandle>((resolve, reject) => {
        requests.push({ resolve, reject: () => reject(new Error('refused')) });
      }),
    };
  });

  // A microtask drain rather than a timer: every promise here resolves immediately, and a spec that
  // waits on a real timeout stops working the moment another suite installs a fake clock.
  const settle = async (): Promise<void> => {
    for (let turn = 0; turn < 4; turn++) await Promise.resolve();
  };

  const locks: WakeLock[] = [];
  const wakeLock = (clientType: string, api: WakeLockBackend | null) => {
    const lock = new WakeLock(clientType, api);
    locks.push(lock);
    return lock;
  };

  // Each lock listens on the document for as long as it lives, so one left behind would answer the
  // next spec's visibility change too.
  afterEach(() => {
    while (locks.length > 0) (locks.pop() as WakeLock).dispose();
  });

  it('reports why it cannot run rather than looking merely off', () => {
    // An unsupported browser and a secure-context problem lead to different things to tell someone,
    // so they are different states.
    expect(wakeLock('web-client', null).currentStatus()).toBe('unsupported');
  });

  it('asks for nothing until both the preference and the deck are there', async () => {
    const lock = wakeLock('web-client', backend);

    lock.setEnabled(true);
    expect(requests.length).toBe(0);

    lock.setGate(true);
    expect(requests.length).toBe(1);
    expect(lock.currentStatus()).toBe('pending');

    requests[0].resolve(handle());
    await settle();
    expect(lock.currentStatus()).toBe('active');
  });

  it('releases what it acquired when the deck goes away, and keeps the preference', async () => {
    const lock = wakeLock('web-client', backend);
    lock.setEnabled(true);
    lock.setGate(true);
    requests[0].resolve(handle());
    await settle();

    lock.setGate(false);

    expect(released).toBe(1);
    expect(lock.enabled()).toBeTrue();
    expect(lock.currentStatus()).toBe('off');
  });

  it('drops a lock that arrived after the deck was already left', async () => {
    const lock = wakeLock('web-client', backend);
    lock.setEnabled(true);
    lock.setGate(true);

    lock.setGate(false);
    requests[0].resolve(handle());
    await settle();

    // Holding it anyway is how a kiosk keeps a screen awake for nobody.
    expect(released).toBe(1);
    expect(lock.currentStatus()).toBe('off');
  });

  it('does not ask again when the platform takes the lock away', async () => {
    const lock = wakeLock('web-client', backend);
    lock.setEnabled(true);
    lock.setGate(true);
    const held = handle();
    requests[0].resolve(held);
    await settle();

    held.fireRelease();

    // Re-requesting from the release event is the retry loop #257 calls out.
    expect(requests.length).toBe(1);
    expect(lock.currentStatus()).toBe('denied');
  });

  it('calls a lock lost to a hidden document suspended, not denied', async () => {
    const lock = wakeLock('web-client', backend);
    lock.setEnabled(true);
    lock.setGate(true);
    const held = handle();
    requests[0].resolve(held);
    await settle();

    hidden = true;
    held.fireRelease();

    // The document being hidden explains itself; only anything else reads as a refusal.
    expect(lock.currentStatus()).toBe('suspended');
  });

  it('stops asking once refused, until the document is looked at again', async () => {
    const lock = wakeLock('web-client', backend);
    lock.setEnabled(true);
    lock.setGate(true);
    requests[0].reject();
    await settle();

    expect(lock.currentStatus()).toBe('denied');

    lock.setGate(false);
    lock.setGate(true);
    expect(requests.length).toBe(1);

    document.dispatchEvent(new Event('visibilitychange'));
    expect(requests.length).toBe(2);
  });

  it('remembers the preference on this device across a reload', () => {
    wakeLock('web-client', backend).setEnabled(true);

    expect(wakeLock('web-client', backend).enabled()).toBeTrue();
  });

  it('keeps the two clients on one origin from sharing a preference', () => {
    wakeLock('web-client', backend).setEnabled(true);

    // `/` and `/admin` share an origin, and with it local storage.
    expect(wakeLock('desktop-ui', backend).enabled()).toBeFalse();
  });
});
