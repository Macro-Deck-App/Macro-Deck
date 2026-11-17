import {
  browserWakeLockBackend,
  mediaWakeLockBackend,
  preferredWakeLockBackend,
  WakeLock,
  WakeLockBackend,
  WakeLockHandle,
} from './wake-lock';

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

  it('sends an insecure address without any way to keep awake to https', () => {
    Object.defineProperty(window, 'isSecureContext', { value: false, configurable: true });
    try {
      expect(wakeLock('web-client', null).currentStatus()).toBe('insecureOrigin');
    } finally {
      delete (window as { isSecureContext?: boolean }).isSecureContext;
    }
  });

  it('offers the switch on an insecure address once a fallback can keep it awake', () => {
    Object.defineProperty(window, 'isSecureContext', { value: false, configurable: true });
    try {
      const lock = wakeLock('web-client', backend);
      expect(lock.currentStatus()).toBe('off');

      lock.setEnabled(true);
      lock.setGate(true);
      expect(requests.length).toBe(1);
    } finally {
      delete (window as { isSecureContext?: boolean }).isSecureContext;
    }
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

  it('is held for a screensaver whatever the preference says, and let go with it', async () => {
    const lock = wakeLock('web-client', backend);
    lock.setGate(true);
    expect(requests.length).toBe(0);

    lock.setForced(true);
    expect(requests.length).toBe(1);
    requests[0].resolve(handle());
    await settle();
    expect(lock.currentStatus()).toBe('active');

    lock.setForced(false);
    expect(released).toBe(1);
    expect(lock.enabled()).toBeFalse();
    expect(lock.currentStatus()).toBe('off');
  });

  it('does not ask again for a screensaver on a device that refused', async () => {
    const lock = wakeLock('web-client', backend);
    lock.setEnabled(true);
    lock.setGate(true);
    requests[0].reject();
    await settle();
    expect(lock.currentStatus()).toBe('denied');

    lock.setForced(true);

    expect(requests.length).toBe(1);
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

  describe('choosing how to keep the display awake', () => {
    const silk = 'Mozilla/5.0 (Linux; Android 9; AEOCH) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'Silk/120.4.1 like Chrome/120.0.6099.230 Safari/537.36';

    function withUserAgent(userAgent: string, run: () => void): void {
      Object.defineProperty(navigator, 'userAgent', { get: () => userAgent, configurable: true });
      try {
        run();
      } finally {
        delete (navigator as { userAgent?: string }).userAgent;
      }
    }

    function fakeBackend(): WakeLockBackend & { requested: number } {
      const fake = {
        requested: 0,
        request: () => {
          fake.requested++;
          return Promise.resolve(handle());
        },
      };
      return fake;
    }

    it('prefers the native lock and never starts the fallback beside it', () => {
      const native = fakeBackend();
      const media = jasmine.createSpy('media').and.returnValue(fakeBackend());

      expect(preferredWakeLockBackend(native, media)).toBe(native);
      expect(media).not.toHaveBeenCalled();
    });

    it('falls back to the clip where the native lock is missing', () => {
      const media = fakeBackend();

      expect(preferredWakeLockBackend(null, () => media)).toBe(media);
    });

    it('has nothing to offer when neither is available', () => {
      expect(preferredWakeLockBackend(null, () => null)).toBeNull();
    });

    it('runs both in Silk, which closes an idle page even while the screen is held', async () => {
      const native = fakeBackend();
      const media = fakeBackend();

      withUserAgent(silk, () => {
        const chosen = preferredWakeLockBackend(native, () => media) as WakeLockBackend;
        void chosen.request();
      });
      await settle();

      expect(native.requested).toBe(1);
      expect(media.requested).toBe(1);
    });

    it('offers no native lock outside a secure context', () => {
      const nav = navigator as { wakeLock?: unknown };
      nav.wakeLock = { request: () => Promise.reject(new Error('unused')) };
      Object.defineProperty(window, 'isSecureContext', { value: false, configurable: true });
      try {
        expect(browserWakeLockBackend()).toBeNull();
      } finally {
        delete (window as { isSecureContext?: boolean }).isSecureContext;
        delete nav.wakeLock;
      }
    });

    it('offers no clip on an iPhone that would play it fullscreen', () => {
      const proto = Object.getPrototypeOf(document.createElement('video')) as { playsInline?: boolean };
      const inline = Object.getOwnPropertyDescriptor(proto, 'playsInline') as PropertyDescriptor;
      delete proto.playsInline;
      try {
        withUserAgent('Mozilla/5.0 (iPhone; CPU iPhone OS 9_3 like Mac OS X) AppleWebKit/601.1.46', () => {
          expect(mediaWakeLockBackend(() => video().element)).toBeNull();
        });
      } finally {
        Object.defineProperty(proto, 'playsInline', inline);
      }
    });

    it('offers no clip a browser cannot play', () => {
      expect(mediaWakeLockBackend(() => video({ playable: false }).element)).toBeNull();
    });
  });

  interface FakeVideo {
    element: HTMLVideoElement;
    plays: number;
  }

  type PlayOutcome = 'plays' | 'needsGesture' | 'fails' | 'noPromise' | 'loading' | 'aborted';

  let videos: FakeVideo[] = [];

  function video(options: { playable?: boolean; outcome?: () => PlayOutcome } = {}): FakeVideo {
    const element = document.createElement('video');
    const fake: FakeVideo = { element: element, plays: 0 };
    element.canPlayType = () => (options.playable === false ? '' : 'maybe');
    let loading: ((error: Error) => void) | null = null;
    element.pause = () => {
      if (loading === null) return;
      const abort = new Error('paused');
      abort.name = 'AbortError';
      loading(abort);
      loading = null;
    };
    element.play = (() => {
      fake.plays++;
      const outcome = options.outcome === undefined ? 'plays' : options.outcome();
      if (outcome === 'noPromise') return undefined;
      if (outcome === 'loading') {
        return new Promise<void>((_, reject) => {
          loading = reject;
        });
      }
      if (outcome === 'plays') {
        element.dispatchEvent(new Event('playing'));
        return Promise.resolve();
      }
      const error = new Error('refused');
      const names = { needsGesture: 'NotAllowedError', aborted: 'AbortError', fails: 'NotSupportedError' };
      error.name = names[outcome];
      return Promise.reject(error);
    }) as HTMLVideoElement['play'];
    videos.push(fake);
    return fake;
  }

  describe('the clip fallback', () => {
    let outcome: PlayOutcome;

    beforeEach(() => {
      videos = [];
      outcome = 'plays';
    });

    afterEach(() => {
      document.body.innerHTML = '';
    });

    const clipLock = (): WakeLock => {
      const media = mediaWakeLockBackend(() => video({ outcome: () => outcome }).element) as WakeLockBackend;
      const lock = wakeLock('web-client', media);
      lock.setEnabled(true);
      lock.setGate(true);
      return lock;
    };

    const held = (): FakeVideo => videos[videos.length - 1];
    const tap = (): void => {
      document.dispatchEvent(new Event('touchend'));
    };

    it('keeps the display awake by playing the clip on the page', async () => {
      const lock = clipLock();
      await settle();

      expect(lock.currentStatus()).toBe('active');
      expect(held().element.parentNode).toBe(document.body);
      expect(held().element.loop).toBeTrue();
    });

    it('asks for a tap when the browser refuses to start it on its own, and starts on that tap', async () => {
      outcome = 'needsGesture';
      const lock = clipLock();
      await settle();
      expect(lock.currentStatus()).toBe('awaitingGesture');

      outcome = 'plays';
      tap();
      await settle();

      expect(lock.currentStatus()).toBe('active');
    });

    it('waits for the playing event where play() returns nothing', async () => {
      outcome = 'noPromise';
      const lock = clipLock();
      await settle();
      expect(lock.currentStatus()).toBe('awaitingGesture');

      held().element.dispatchEvent(new Event('playing'));

      expect(lock.currentStatus()).toBe('active');
    });

    it('reads a clip that cannot play at all as refused', async () => {
      outcome = 'fails';
      const lock = clipLock();
      await settle();

      expect(lock.currentStatus()).toBe('denied');
    });

    it('recovers from an interruption on the next tap instead of giving up', async () => {
      const lock = clipLock();
      await settle();

      held().element.dispatchEvent(new Event('pause'));
      expect(lock.currentStatus()).toBe('awaitingGesture');

      tap();
      await settle();
      expect(lock.currentStatus()).toBe('active');
      expect(held().plays).toBe(2);
    });

    it('lets go while the page is hidden and plays again once it is back', async () => {
      const lock = clipLock();
      await settle();
      const first = held();

      hidden = true;
      document.dispatchEvent(new Event('visibilitychange'));
      expect(lock.currentStatus()).toBe('suspended');
      expect(first.element.parentNode).toBeNull();

      hidden = false;
      document.dispatchEvent(new Event('visibilitychange'));
      await settle();
      expect(lock.currentStatus()).toBe('active');
      expect(held()).not.toBe(first);
    });

    it('recovers when the page is hidden before the clip has even started', async () => {
      outcome = 'loading';
      const lock = clipLock();
      await settle();
      expect(lock.currentStatus()).toBe('pending');

      hidden = true;
      document.dispatchEvent(new Event('visibilitychange'));
      await settle();

      outcome = 'plays';
      hidden = false;
      document.dispatchEvent(new Event('visibilitychange'));
      await settle();

      expect(lock.currentStatus()).toBe('active');
      expect(videos.filter(entry => entry.element.parentNode !== null).length).toBe(1);
    });

    it('keeps waiting for a tap when a retry is cut short again', async () => {
      const lock = clipLock();
      await settle();
      held().element.dispatchEvent(new Event('pause'));

      outcome = 'aborted';
      tap();
      await settle();
      expect(lock.currentStatus()).toBe('awaitingGesture');

      outcome = 'plays';
      tap();
      await settle();
      expect(lock.currentStatus()).toBe('active');
    });

    it('starts nothing on the next tap once it was turned off while waiting', async () => {
      outcome = 'needsGesture';
      const lock = clipLock();
      await settle();
      const waiting = held();

      lock.setEnabled(false);
      outcome = 'plays';
      tap();
      waiting.element.dispatchEvent(new Event('playing'));
      await settle();

      expect(waiting.plays).toBe(1);
      expect(waiting.element.parentNode).toBeNull();
      expect(lock.currentStatus()).toBe('off');
    });

    it('reads a clip that breaks while held as refused', async () => {
      const lock = clipLock();
      await settle();

      held().element.dispatchEvent(new Event('error'));

      expect(lock.currentStatus()).toBe('denied');
    });
  });

  describe('in Silk', () => {
    const silk = 'Mozilla/5.0 (Linux; Android 9; AEOCH) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'Silk/120.4.1 like Chrome/120.0.6099.230 Safari/537.36';
    let restoreUserAgent: () => void;

    beforeEach(() => {
      videos = [];
      Object.defineProperty(navigator, 'userAgent', { get: () => silk, configurable: true });
      restoreUserAgent = () => delete (navigator as { userAgent?: string }).userAgent;
    });

    afterEach(() => {
      restoreUserAgent();
      document.body.innerHTML = '';
    });

    const silkLock = (): WakeLock => {
      const media = mediaWakeLockBackend(() => video().element) as WakeLockBackend;
      const lock = wakeLock('web-client', preferredWakeLockBackend(backend, () => media));
      lock.setEnabled(true);
      lock.setGate(true);
      return lock;
    };

    it('keeps the page alive with the clip even when the native lock is refused', async () => {
      const lock = silkLock();
      requests[0].reject();
      await settle();

      expect(lock.currentStatus()).toBe('active');
      expect(videos.length).toBe(2);
    });

    it('lets go of the native lock when the clip breaks', async () => {
      const lock = silkLock();
      requests[0].resolve(handle());
      await settle();

      videos[videos.length - 1].element.dispatchEvent(new Event('error'));
      await settle();

      expect(released).toBe(1);
      expect(lock.currentStatus()).toBe('denied');
    });

    it('lets go of both when the deck is left', async () => {
      const lock = silkLock();
      requests[0].resolve(handle());
      await settle();

      lock.setGate(false);
      await settle();

      expect(released).toBe(1);
      expect(videos[videos.length - 1].element.parentNode).toBeNull();
    });
  });
});
