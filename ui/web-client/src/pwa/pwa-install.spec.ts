import { detectPlatform } from './platform';
import { PwaInstall } from './pwa-install';

interface FakeWindow {
  matches: boolean;
  navigator: { userAgent: string; platform?: string; maxTouchPoints?: number; standalone?: boolean };
  fire(type: string, event?: unknown): void;
  asWindow(): Window;
}

function fakeWindow(options?: {
  matches?: boolean;
  userAgent?: string;
  platform?: string;
  maxTouchPoints?: number;
  iosStandalone?: boolean;
}): FakeWindow {
  const settings = options || {};
  const listeners: Record<string, Array<(event: unknown) => void>> = {};
  const navigatorStub = {
    userAgent: settings.userAgent || 'Mozilla/5.0 (Windows NT 10.0) Chrome/120',
    platform: settings.platform,
    maxTouchPoints: settings.maxTouchPoints,
    standalone: settings.iosStandalone === true ? true : undefined,
  };

  const state = {
    matches: settings.matches === true,
    navigator: navigatorStub,
    fire(type: string, event?: unknown): void {
      const found = listeners[type] || [];
      for (const listener of found) listener(event || { preventDefault: () => undefined });
    },
    asWindow(): Window {
      return {
        navigator: navigatorStub,
        matchMedia: () => ({
          get matches(): boolean { return state.matches; },
          addEventListener: (_type: string, listener: () => void) => {
            (listeners['media'] = listeners['media'] || []).push(listener);
          },
          removeEventListener: () => undefined,
        }),
        addEventListener: (type: string, listener: (event: unknown) => void) => {
          (listeners[type] = listeners[type] || []).push(listener);
        },
        removeEventListener: () => undefined,
      } as unknown as Window;
    },
  };
  return state;
}

function install(win: FakeWindow, secure = true): PwaInstall {
  return new PwaInstall({ window: win.asWindow(), secure: () => secure });
}

function promptEvent(outcome: 'accepted' | 'dismissed'): unknown {
  return {
    preventDefault: () => undefined,
    prompt: () => Promise.resolve(),
    userChoice: Promise.resolve({ outcome }),
  };
}

describe('PwaInstall', () => {
  it('reports a standalone window as already running as an app', () => {
    expect(install(fakeWindow({ matches: true })).availability()).toBe('runningAsApp');
  });

  it('reports an iOS home-screen window as already running as an app', () => {
    const win = fakeWindow({ userAgent: 'iPhone Safari', iosStandalone: true });

    expect(install(win).availability()).toBe('runningAsApp');
  });

  it('reports a captured prompt as promptable', () => {
    const win = fakeWindow();
    const service = install(win);

    win.fire('beforeinstallprompt', promptEvent('accepted'));

    expect(service.availability()).toBe('promptable');
  });

  it('reports iOS, which never offers a prompt, as manual only', () => {
    const win = fakeWindow({ userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0) Safari' });

    expect(install(win).availability()).toBe('manualOnly');
  });

  it('reports an iPadOS tablet as manual only despite its desktop user agent', () => {
    // iPadOS 13 dropped the iPad token and reports MacIntel; only its touch points give it away.
    const win = fakeWindow({
      userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Safari',
      platform: 'MacIntel',
      maxTouchPoints: 5,
    });

    expect(install(win).availability()).toBe('manualOnly');
  });

  it('reports an insecure origin as requiring HTTPS', () => {
    expect(install(fakeWindow(), false).availability()).toBe('requiresHttps');
  });

  it('reports a browser that offers no prompt as unsupported', () => {
    expect(install(fakeWindow()).availability()).toBe('unsupported');
  });

  it('has nothing to prompt with before an install prompt is captured', async () => {
    expect(await install(fakeWindow()).promptInstall()).toBe('unavailable');
  });

  it('reports the choice the person made, and offers the prompt only once', async () => {
    const win = fakeWindow();
    const service = install(win);
    win.fire('beforeinstallprompt', promptEvent('dismissed'));

    expect(await service.promptInstall()).toBe('dismissed');
    expect(await service.promptInstall()).toBe('unavailable');
  });

  it('stops offering installation once the app has been installed', () => {
    const win = fakeWindow();
    const service = install(win);
    win.fire('beforeinstallprompt', promptEvent('accepted'));

    win.matches = true;
    win.fire('appinstalled');

    expect(service.availability()).toBe('runningAsApp');
  });
});

describe('detectPlatform', () => {
  it('recognises an iPhone', () => {
    expect(detectPlatform({ userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0)' })).toBe('ios');
  });

  it('recognises an iPadOS tablet reporting itself as a Mac', () => {
    expect(detectPlatform({ userAgent: 'Macintosh; Intel Mac OS X', platform: 'MacIntel', maxTouchPoints: 5 }))
      .toBe('ios');
  });

  it('leaves a real Mac a desktop', () => {
    expect(detectPlatform({ userAgent: 'Macintosh; Intel Mac OS X', platform: 'MacIntel', maxTouchPoints: 0 }))
      .toBe('desktop');
  });

  it('recognises Android', () => {
    expect(detectPlatform({ userAgent: 'Mozilla/5.0 (Linux; Android 14)' })).toBe('android');
  });
});
