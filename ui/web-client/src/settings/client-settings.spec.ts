import {
  ClientAppStrings,
  LocalizationCatalog,
  Strings,
  type ThemeMode,
  type WebClientTargetCapabilities,
} from '@macro-deck/runtime';
import { type AppUpdatePhase, type PwaAvailability, type PwaInstallOutcome } from '../pwa';
import { type RenderingMode } from '../rendering-mode';
import { type WakeLockStatus } from '../wake-lock';
import {
  createClientSettings,
  type ClientSettingsHandle,
  type ClientSettingsOptions,
  type HintTimer,
} from './client-settings';

const catalog = new LocalizationCatalog();

function english(qualifiedKey: string): string {
  const separator = qualifiedKey.indexOf(':');
  return catalog.translate(qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1));
}

function translator() {
  return {
    translate: (key: string, args?: Record<string, unknown>) => {
      const separator = key.indexOf(':');
      return catalog.translate(key.slice(0, separator), key.slice(separator + 1), args);
    },
    signOut: jasmine.createSpy('signOut').and.resolveTo(undefined),
  };
}

function appearance(mode: ThemeMode = 'system') {
  const listeners: Array<() => void> = [];
  return {
    current: mode,
    setThemeMode: jasmine.createSpy('setThemeMode'),
    themeMode() {
      return this.current;
    },
    onChange: (listener: () => void) => {
      listeners.push(listener);
      return () => undefined;
    },
    notify: () => listeners.forEach(listener => listener()),
  };
}

function renderingMode(mode: RenderingMode = 'standard') {
  const listeners: Array<() => void> = [];
  return {
    current: mode,
    set: jasmine.createSpy('set'),
    get() {
      return this.current;
    },
    onChange: (listener: () => void) => {
      listeners.push(listener);
      return () => undefined;
    },
    notify: () => listeners.forEach(listener => listener()),
  };
}

function wakeLock(status: WakeLockStatus = 'off', on = false) {
  const listeners: Array<() => void> = [];
  return {
    status: status,
    on: on,
    setEnabled: jasmine.createSpy('setEnabled'),
    currentStatus() {
      return this.status;
    },
    enabled() {
      return this.on;
    },
    onChange: (listener: () => void) => {
      listeners.push(listener);
      return () => undefined;
    },
    notify: () => listeners.forEach(listener => listener()),
  };
}

function appUpdate(phase: AppUpdatePhase = 'idle') {
  const listeners: Array<(phase: AppUpdatePhase) => void> = [];
  return {
    current: phase,
    activateNow: jasmine.createSpy('activateNow').and.resolveTo(undefined),
    check: jasmine.createSpy('check').and.resolveTo(undefined),
    phase() {
      return this.current;
    },
    onPhaseChange: (listener: (phase: AppUpdatePhase) => void) => {
      listeners.push(listener);
    },
    notify() {
      listeners.forEach(listener => listener(this.current));
    },
  };
}

function pwaInstall(availability: PwaAvailability = 'unsupported', outcome: PwaInstallOutcome = 'accepted') {
  const listeners: Array<(availability: PwaAvailability) => void> = [];
  return {
    current: availability,
    promptInstall: jasmine.createSpy('promptInstall').and.resolveTo(outcome),
    availability() {
      return this.current;
    },
    onChange: (listener: (availability: PwaAvailability) => void) => {
      listeners.push(listener);
    },
    notify() {
      listeners.forEach(listener => listener(this.current));
    },
  };
}

function capabilities(overrides?: Partial<WebClientTargetCapabilities>): WebClientTargetCapabilities {
  return {
    serviceWorker: true,
    wakeLock: true,
    clientSettings: true,
    deviceSetupPrompts: true,
    ...(overrides === undefined ? {} : overrides),
  };
}

function manualTimer(): HintTimer & { fire(): void } {
  let pending: (() => void) | null = null;
  return {
    set: (callback: () => void) => {
      pending = callback;
      return 1;
    },
    clear: () => {
      pending = null;
    },
    fire: () => {
      const callback = pending;
      pending = null;
      if (callback !== null) callback();
    },
  };
}

interface Fixture {
  handle: ClientSettingsHandle;
  client: ReturnType<typeof translator>;
  appearance: ReturnType<typeof appearance>;
  renderingMode: ReturnType<typeof renderingMode>;
  wakeLock: ReturnType<typeof wakeLock>;
  update: ReturnType<typeof appUpdate>;
  install: ReturnType<typeof pwaInstall>;
  timer: ReturnType<typeof manualTimer>;
}

let live: ClientSettingsHandle[] = [];

async function flush(): Promise<void> {
  for (let index = 0; index < 4; index++) await Promise.resolve();
}

function create(overrides?: Partial<ClientSettingsOptions>): Fixture {
  const parts = {
    client: translator(),
    appearance: appearance(),
    renderingMode: renderingMode(),
    wakeLock: wakeLock(),
    update: appUpdate(),
    install: pwaInstall(),
    timer: manualTimer(),
  };
  const handle = createClientSettings({
    client: parts.client,
    appearance: parts.appearance,
    renderingMode: parts.renderingMode,
    wakeLock: parts.wakeLock,
    update: parts.update,
    install: parts.install,
    capabilities: capabilities(),
    timer: parts.timer,
    ...(overrides === undefined ? {} : overrides),
  });
  if (handle === null) throw new Error('the surface was gated off');
  live.push(handle);
  return { handle: handle, ...parts };
}

function dialog(): HTMLElement {
  const found = document.querySelector<HTMLElement>('.wc-client-settings-dialog');
  if (found === null) throw new Error('no settings dialog is open');
  return found;
}

function text(): string {
  return dialog().textContent === null ? '' : (dialog().textContent as string);
}

function buttonLabelled(label: string): HTMLButtonElement | null {
  const buttons = dialog().querySelectorAll<HTMLButtonElement>('.wc-btn');
  for (let index = 0; index < buttons.length; index++) {
    if (buttons[index].textContent === label) return buttons[index];
  }
  return null;
}

function wakeLockToggle(): HTMLInputElement | null {
  return dialog().querySelector<HTMLInputElement>('.wc-toggle-input');
}

function statusLines(): string[] {
  const lines = dialog().querySelectorAll<HTMLElement>('.wc-client-settings-status');
  const found: string[] = [];
  for (let index = 0; index < lines.length; index++) {
    if (!lines[index].hasAttribute('hidden')) found.push(lines[index].textContent as string);
  }
  return found;
}

describe('client settings', () => {
  afterEach(() => {
    live.forEach(handle => handle.destroy());
    live = [];
  });

  it('offers a gear the user can name, and opens the settings dialog with it', () => {
    const fixture = create();

    expect(fixture.handle.element.getAttribute('aria-label'))
      .toBe(english(ClientAppStrings.WebClient.Settings.OpenSettings));
    expect(document.querySelector('.wc-client-settings-dialog')).toBeNull();

    fixture.handle.element.click();

    expect(dialog().textContent).toContain(english(Strings.Settings.Title));
  });

  // The whole surface, not only the modal: a gear that opens nothing is worse than no gear.
  it('is absent on a target that carries no client settings', () => {
    const handle = createClientSettings({
      client: translator(),
      appearance: appearance(),
      renderingMode: renderingMode(),
      wakeLock: wakeLock(),
      update: appUpdate(),
      install: pwaInstall(),
      capabilities: capabilities({ clientSettings: false }),
    });

    expect(handle).toBeNull();
  });

  it('signs out of the host session', () => {
    const fixture = create();
    fixture.handle.open();

    buttonLabelled(english(ClientAppStrings.WebClient.Account.SignOut))!.click();

    expect(fixture.client.signOut).toHaveBeenCalled();
  });

  describe('device setup', () => {
    it('leads into the wizard, getting its own dialog out of the way first', () => {
      const openDeviceSetup = jasmine.createSpy('openDeviceSetup');
      const fixture = create({ openDeviceSetup: openDeviceSetup });
      fixture.handle.open();

      buttonLabelled(english(ClientAppStrings.WebClient.DeviceSetup.OpenAction))!.click();

      expect(openDeviceSetup).toHaveBeenCalled();
      expect(document.querySelector('.wc-client-settings-dialog')).toBeNull();
    });

    it('withholds the entry from a target whose setup prompts are meaningless', () => {
      const fixture = create({
        openDeviceSetup: () => undefined,
        capabilities: capabilities({ deviceSetupPrompts: false }),
      });
      fixture.handle.open();

      expect(text()).not.toContain(english(ClientAppStrings.WebClient.DeviceSetup.SectionTitle));
    });
  });

  describe('the wake lock', () => {
    const explained: Array<[WakeLockStatus, string]> = [
      ['active', ClientAppStrings.WebClient.KeepAwake.Active],
      ['suspended', ClientAppStrings.WebClient.KeepAwake.Suspended],
      ['denied', ClientAppStrings.WebClient.KeepAwake.Denied],
      ['unsupported', ClientAppStrings.WebClient.KeepAwake.Unsupported],
      ['insecureOrigin', ClientAppStrings.WebClient.KeepAwake.InsecureOrigin],
      ['pending', ClientAppStrings.WebClient.KeepAwake.Pending],
    ];

    explained.forEach(entry => {
      it(`explains itself while it is ${entry[0]}`, () => {
        const fixture = create();
        fixture.wakeLock.status = entry[0];
        fixture.handle.open();

        expect(statusLines()).toContain(english(entry[1]));
      });
    });

    it('says nothing at all while it is simply off', () => {
      const fixture = create();
      fixture.handle.open();

      const keepAwake = explained.map(entry => english(entry[1]));
      statusLines().forEach(line => expect(keepAwake).not.toContain(line));
    });

    // A switch that can never be flipped invites a tap and answers with nothing; neither state is
    // anything the user can change from this screen.
    ['unsupported', 'insecureOrigin'].forEach(status => {
      it(`offers no switch on ${status}`, () => {
        const fixture = create();
        fixture.wakeLock.status = status as WakeLockStatus;
        fixture.handle.open();

        expect(wakeLockToggle()).toBeNull();
      });
    });

    it('flips the preference straight from the switch', () => {
      const fixture = create();
      fixture.handle.open();

      const toggle = wakeLockToggle()!;
      toggle.checked = true;
      toggle.dispatchEvent(new Event('change'));

      expect(fixture.wakeLock.setEnabled).toHaveBeenCalledWith(true);
    });

    it('reports a status the store reaches on its own into the open dialog', () => {
      const fixture = create();
      fixture.handle.open();
      expect(statusLines()).not.toContain(english(ClientAppStrings.WebClient.KeepAwake.Denied));

      fixture.wakeLock.status = 'denied';
      fixture.wakeLock.notify();

      expect(statusLines()).toContain(english(ClientAppStrings.WebClient.KeepAwake.Denied));
    });
  });

  describe('the update row', () => {
    const installed = () => {
      const fixture = create();
      fixture.install.current = 'runningAsApp';
      return fixture;
    };

    it('says nothing about updating at all where the platform cannot update itself', () => {
      const fixture = installed();
      fixture.update.current = 'unsupported';
      fixture.handle.open();

      expect(text()).not.toContain(english(ClientAppStrings.WebClient.Install.UpdateLabel));
    });

    it('stays off the page in a browser tab, where reloading the tab is the update', () => {
      const fixture = create();
      fixture.update.current = 'upToDate';
      fixture.handle.open();

      expect(text()).not.toContain(english(ClientAppStrings.WebClient.Install.UpdateLabel));
    });

    const described: Array<[AppUpdatePhase, string]> = [
      ['idle', ClientAppStrings.WebClient.Update.UpToDate],
      ['upToDate', ClientAppStrings.WebClient.Update.UpToDate],
      ['checking', ClientAppStrings.WebClient.Update.Checking],
      ['available', ClientAppStrings.WebClient.Install.UpdateDescription],
      ['applying', ClientAppStrings.WebClient.Update.Applying],
      ['reloading', ClientAppStrings.WebClient.Update.Reloading],
      ['checkFailed', ClientAppStrings.WebClient.Update.CheckFailed],
      ['applyFailed', ClientAppStrings.WebClient.Update.ApplyFailed],
    ];

    described.forEach(entry => {
      it(`describes the ${entry[0]} phase`, () => {
        const fixture = installed();
        fixture.update.current = entry[0];
        fixture.handle.open();

        expect(text()).toContain(english(entry[1]));
      });
    });

    it('activates a version that is actually ready, rather than checking again', () => {
      const fixture = installed();
      fixture.update.current = 'available';
      fixture.handle.open();

      buttonLabelled(english(ClientAppStrings.WebClient.Install.UpdateNow))!.click();

      expect(fixture.update.activateNow).toHaveBeenCalled();
      expect(fixture.update.check).not.toHaveBeenCalled();
    });

    it('retries the check - not the install - after a failed one', () => {
      const fixture = installed();
      fixture.update.current = 'checkFailed';
      fixture.handle.open();

      expect(buttonLabelled(english(ClientAppStrings.WebClient.Install.UpdateNow))).toBeNull();
      buttonLabelled(english(Strings.Common.Retry))!.click();

      expect(fixture.update.check).toHaveBeenCalled();
      expect(fixture.update.activateNow).not.toHaveBeenCalled();
    });

    ['checking', 'applying', 'reloading'].forEach(phase => {
      it(`offers nothing to press while it is ${phase}`, () => {
        const fixture = installed();
        fixture.update.current = phase as AppUpdatePhase;
        fixture.handle.open();

        expect(buttonLabelled(english(ClientAppStrings.WebClient.Update.CheckAction))).toBeNull();
        expect(buttonLabelled(english(ClientAppStrings.WebClient.Install.UpdateNow))).toBeNull();
        expect(buttonLabelled(english(Strings.Common.Retry))).toBeNull();
      });
    });

    it('follows a phase the source reaches on its own into the open dialog', () => {
      const fixture = installed();
      fixture.handle.open();

      fixture.update.current = 'available';
      fixture.update.notify();

      expect(buttonLabelled(english(ClientAppStrings.WebClient.Install.UpdateNow))).not.toBeNull();
    });
  });

  describe('the install row', () => {
    it('reports being an app rather than a tab once it is one', () => {
      const fixture = create();
      fixture.install.current = 'runningAsApp';
      fixture.handle.open();

      expect(text()).toContain(english(ClientAppStrings.WebClient.Install.Installed));
      expect(buttonLabelled(english(ClientAppStrings.WebClient.Install.InstallAction))).toBeNull();
    });

    it('offers the prompt the browser handed over, and uses it', () => {
      const fixture = create();
      fixture.install.current = 'promptable';
      fixture.handle.open();

      buttonLabelled(english(ClientAppStrings.WebClient.Install.InstallAction))!.click();

      expect(fixture.install.promptInstall).toHaveBeenCalled();
    });

    const hinted: Array<[PwaAvailability, string]> = [
      ['manualOnly', ClientAppStrings.WebClient.Install.InstallManualIos],
      ['requiresHttps', ClientAppStrings.WebClient.Install.RequiresHttps],
      ['unsupported', ClientAppStrings.WebClient.Install.Unsupported],
    ];

    hinted.forEach(entry => {
      it(`explains why installing is not on offer while ${entry[0]}`, () => {
        const fixture = create();
        fixture.install.current = entry[0];
        fixture.handle.open();

        expect(statusLines()).toContain(english(entry[1]));
      });
    });

    it('says the installation was cancelled, until the hint has had its time', async () => {
      const fixture = create();
      fixture.install.current = 'promptable';
      fixture.install.promptInstall.and.resolveTo('dismissed');
      fixture.handle.open();

      buttonLabelled(english(ClientAppStrings.WebClient.Install.InstallAction))!.click();
      await flush();

      expect(statusLines()).toContain(english(ClientAppStrings.WebClient.Install.InstallDismissed));
      expect(buttonLabelled(english(ClientAppStrings.WebClient.Install.InstallAction))).toBeNull();

      fixture.timer.fire();

      expect(statusLines()).not.toContain(english(ClientAppStrings.WebClient.Install.InstallDismissed));
      expect(buttonLabelled(english(ClientAppStrings.WebClient.Install.InstallAction))).not.toBeNull();
    });
  });

  describe('the display section', () => {
    function activeOptions(): string[] {
      const active = dialog().querySelectorAll<HTMLElement>('.wc-seg-option.wc-seg-active');
      const found: string[] = [];
      for (let index = 0; index < active.length; index++) {
        found.push(active[index].textContent as string);
      }
      return found;
    }

    function option(label: string): HTMLElement {
      const options = dialog().querySelectorAll<HTMLElement>('.wc-seg-option');
      for (let index = 0; index < options.length; index++) {
        if (options[index].textContent === label) return options[index];
      }
      throw new Error(`no ${label} option`);
    }

    it('lets a theme be picked instead of only following the system', () => {
      const fixture = create();
      fixture.handle.open();

      option(english(ClientAppStrings.Settings.Appearance.Dark)).click();

      expect(fixture.appearance.setThemeMode).toHaveBeenCalledWith('dark');
    });

    it('simplifies the deck on request', () => {
      const fixture = create();
      fixture.handle.open();

      option(english(ClientAppStrings.WebClient.Rendering.Simple)).click();

      expect(fixture.renderingMode.set).toHaveBeenCalledWith('simple');
    });

    it('follows a theme changed elsewhere into the open dialog', () => {
      const fixture = create();
      fixture.handle.open();
      expect(activeOptions()).toContain(english(ClientAppStrings.Settings.Appearance.System));

      fixture.appearance.current = 'light';
      fixture.appearance.notify();

      expect(activeOptions()).toContain(english(ClientAppStrings.Settings.Appearance.Light));
    });
  });

  describe('the legacy entry', () => {
    it('explains itself when the page came from it', () => {
      const fixture = create({ legacyEntry: true });
      fixture.handle.open();

      expect(text()).toContain(english(ClientAppStrings.WebClient.Legacy.Notice));
    });

    it('says nothing about compatibility on the modern entry', () => {
      const fixture = create();
      fixture.handle.open();

      expect(text()).not.toContain(english(ClientAppStrings.WebClient.Legacy.SectionTitle));
    });
  });

  it('offers no keep-awake switch on a device whose firmware already keeps its screen lit', () => {
    // The Car Thing declares wakeLock: false for exactly this reason. A switch that cannot do
    // anything is the dead control the capability list exists to remove.
    const fixture = create({ capabilities: capabilities({ wakeLock: false }) });
    fixture.handle.open();

    expect(dialog().textContent).not.toContain(english(ClientAppStrings.WebClient.KeepAwake.Label));
  });

  it('keeps the keep-awake switch wherever the device can actually hold a lock', () => {
    const fixture = create();
    fixture.handle.open();

    expect(dialog().textContent).toContain(english(ClientAppStrings.WebClient.KeepAwake.Label));
  });
});
