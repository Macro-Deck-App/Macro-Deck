import { isSecureContext } from '@macro-deck/runtime';
import { detectPlatform, type PlatformNavigator } from './platform';

interface BeforeInstallPromptEvent extends Event {
  readonly userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
  prompt(): Promise<void>;
}

export type PwaInstallOutcome = 'accepted' | 'dismissed' | 'unavailable';

// runningAsApp is not "installed": what is measured is whether this window runs standalone, which
// is the only question a browser API can answer here. manualOnly is iOS Safari, which never fires
// beforeinstallprompt and installs only through its own Share sheet.
export type PwaAvailability = 'runningAsApp' | 'promptable' | 'manualOnly' | 'requiresHttps' | 'unsupported';

export interface PwaInstallOptions {
  window?: Window;
  navigator?: PlatformNavigator;
  secure?: () => boolean;
}

// navigator.standalone is Safari-only and absent from lib.dom - reached through a narrow cast.
function isIosStandalone(nav: unknown): boolean {
  return (nav as { standalone?: boolean }).standalone === true;
}

export class PwaInstall {
  private readonly win: Window;
  private readonly nav: PlatformNavigator;
  private readonly secure: () => boolean;

  private displayModeValue: 'standalone' | 'browser';
  private promptable = false;
  private deferredPrompt: BeforeInstallPromptEvent | null = null;
  private readonly listeners: Array<(availability: PwaAvailability) => void> = [];

  private readonly media: MediaQueryList;
  private readonly onDisplayModeChange: () => void;
  private readonly onBeforeInstallPrompt: (event: Event) => void;
  private readonly onAppInstalled: () => void;

  constructor(options?: PwaInstallOptions) {
    const settings = options || {};
    this.win = settings.window || window;
    this.nav = settings.navigator || (this.win.navigator as PlatformNavigator);
    const win = this.win;
    this.secure = settings.secure || function () { return isSecureContext(win); };

    this.media = this.win.matchMedia('(display-mode: standalone)');
    this.displayModeValue = this.readDisplayMode();

    const self = this;
    this.onDisplayModeChange = function () {
      self.displayModeValue = self.readDisplayMode();
      self.notify();
    };
    // Safari only gained addEventListener on MediaQueryList in 14; on the legacy targets calling it
    // unguarded throws before the client can start at all, leaving a blank page and no way to see why.
    if (typeof this.media.addEventListener === 'function') {
      this.media.addEventListener('change', this.onDisplayModeChange);
    } else if (typeof this.media.addListener === 'function') {
      this.media.addListener(this.onDisplayModeChange);
    }

    this.onBeforeInstallPrompt = function (event) {
      // Without this the browser shows its own mini-infobar and this class never learns the prompt
      // exists; capturing it here is what lets the settings row offer an explicit button.
      event.preventDefault();
      self.deferredPrompt = event as BeforeInstallPromptEvent;
      self.promptable = true;
      self.notify();
    };
    this.win.addEventListener('beforeinstallprompt', this.onBeforeInstallPrompt);

    this.onAppInstalled = function () {
      self.deferredPrompt = null;
      self.promptable = false;
      self.displayModeValue = self.readDisplayMode();
      self.notify();
    };
    this.win.addEventListener('appinstalled', this.onAppInstalled);
  }

  displayMode(): 'standalone' | 'browser' {
    return this.displayModeValue;
  }

  availability(): PwaAvailability {
    if (this.displayModeValue === 'standalone') return 'runningAsApp';
    if (!this.secure()) return 'requiresHttps';
    if (this.promptable) return 'promptable';
    return detectPlatform(this.nav) === 'ios' ? 'manualOnly' : 'unsupported';
  }

  onChange(listener: (availability: PwaAvailability) => void): void {
    this.listeners.push(listener);
  }

  promptInstall(): Promise<PwaInstallOutcome> {
    const prompt = this.deferredPrompt;
    if (!prompt) return Promise.resolve<PwaInstallOutcome>('unavailable');

    // The browser only honours one call per captured event, whatever the outcome - clear it first so
    // a second click while the choice is still resolving cannot fire it twice.
    this.deferredPrompt = null;
    this.promptable = false;
    this.notify();

    return prompt.prompt()
      .then(function () { return prompt.userChoice; })
      .then(function (choice) { return choice.outcome as PwaInstallOutcome; });
  }

  destroy(): void {
    if (typeof this.media.removeEventListener === 'function') {
      this.media.removeEventListener('change', this.onDisplayModeChange);
    } else if (typeof this.media.removeListener === 'function') {
      this.media.removeListener(this.onDisplayModeChange);
    }
    this.win.removeEventListener('beforeinstallprompt', this.onBeforeInstallPrompt);
    this.win.removeEventListener('appinstalled', this.onAppInstalled);
  }

  private readDisplayMode(): 'standalone' | 'browser' {
    return this.media.matches || isIosStandalone(this.nav) ? 'standalone' : 'browser';
  }

  private notify(): void {
    const availability = this.availability();
    for (let index = 0; index < this.listeners.length; index++) {
      this.listeners[index](availability);
    }
  }
}
