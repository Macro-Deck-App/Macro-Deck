import {
  ClientAppStrings,
  LocalizationCatalog,
  Strings,
  type ThemeMode,
  type WebClientTargetCapabilities,
} from '@macro-deck/runtime';
import { type AppUpdatePhase, type PwaAvailability, type PwaInstallOutcome } from '../pwa';
import { type RenderingMode } from '../rendering-mode';
import { type IconResolution, type IconSizeRange } from '../icon-resolution';
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

function settingsButton(hidden = false) {
  const listeners: Array<() => void> = [];
  let answer: ((saved: boolean) => void) | null = null;
  const surface = {
    isOffered: true,
    isConnected: true,
    hidden: hidden,
    stopped: 0,
    set: jasmine.createSpy('set').and.callFake(() => new Promise<boolean>(resolve => {
      answer = resolve;
    })),
    offered() {
      return surface.isOffered;
    },
    connected() {
      return surface.isConnected;
    },
    get() {
      return surface.hidden;
    },
    onChange: (listener: () => void) => {
      listeners.push(listener);
      return () => {
        surface.stopped++;
      };
    },
    notify: () => listeners.forEach(listener => listener()),
    answer: async (saved: boolean) => {
      const pending = answer;
      answer = null;
      if (pending !== null) pending(saved);
      await flush();
    },
  };
  return surface;
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
  loadNotices: jasmine.Spy<() => Promise<string>>;
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
    loadNotices: jasmine.createSpy<() => Promise<string>>('loadNotices').and.resolveTo('NOTICES'),
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
    loadNotices: () => parts.loadNotices(),
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
      loadNotices: () => Promise.resolve(''),
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

  describe('hiding the settings button', () => {
    const label = () => english(ClientAppStrings.WebClient.Settings.HideSettingsButton.Label);
    const toggle = () =>
      dialog().querySelector<HTMLInputElement>(`.wc-toggle-input[aria-label="${label()}"]`);
    const flip = (on: boolean) => {
      const input = toggle()!;
      input.checked = on;
      input.dispatchEvent(new Event('change'));
    };
    const failure = () => english(ClientAppStrings.WebClient.Settings.HideSettingsButton.SaveFailed);

    it('is offered in the display section while the device is signed in', () => {
      const surface = settingsButton(true);
      create({ settingsButton: surface }).handle.open();

      expect(text()).toContain(label());
      expect(text()).toContain(english(ClientAppStrings.WebClient.Settings.HideSettingsButton.Description));
      expect(toggle()!.checked).toBeTrue();
      expect(toggle()!.disabled).toBeFalse();
    });

    it('is not offered before the device is signed in, decided again on every open', () => {
      const surface = settingsButton();
      surface.isOffered = false;
      const fixture = create({ settingsButton: surface });
      fixture.handle.open();
      expect(text()).not.toContain(label());
      fixture.handle.close();

      surface.isOffered = true;
      fixture.handle.open();

      expect(toggle()).not.toBeNull();
    });

    it('cannot be switched while the host is not connected', () => {
      const surface = settingsButton();
      surface.isConnected = false;
      create({ settingsButton: surface }).handle.open();
      expect(toggle()!.disabled).toBeTrue();

      surface.isConnected = true;
      surface.notify();

      expect(toggle()!.disabled).toBeFalse();
    });

    it('asks the host with the chosen value and keeps the dialog open', async () => {
      const surface = settingsButton();
      create({ settingsButton: surface }).handle.open();

      flip(true);
      surface.hidden = true;
      surface.notify();
      await surface.answer(true);

      expect(surface.set).toHaveBeenCalledOnceWith(true);
      expect(toggle()!.checked).toBeTrue();
      expect(statusLines()).not.toContain(failure());
    });

    it('follows a change the host pushes while the dialog is open', () => {
      const surface = settingsButton();
      create({ settingsButton: surface }).handle.open();

      surface.hidden = true;
      surface.notify();

      expect(toggle()!.checked).toBeTrue();
    });

    it('says so and shows the stored value again when the change fails', async () => {
      const surface = settingsButton();
      create({ settingsButton: surface }).handle.open();

      flip(true);
      await surface.answer(false);

      expect(statusLines()).toContain(failure());
      expect(toggle()!.checked).toBeFalse();
    });

    it('stops listening once destroyed', () => {
      const surface = settingsButton();
      const fixture = create({ settingsButton: surface });

      fixture.handle.destroy();

      expect(surface.stopped).toBe(1);
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

    function iconResolution(current: IconResolution = 'auto', inUse: IconSizeRange | null = null) {
      const listeners: Array<() => void> = [];
      return {
        current: current,
        inUseNow: inUse,
        set: jasmine.createSpy('set'),
        get() {
          return this.current;
        },
        inUse() {
          return this.inUseNow;
        },
        onChange: (listener: () => void) => {
          listeners.push(listener);
          return () => undefined;
        },
        notify: () => listeners.forEach(listener => listener()),
      };
    }

    function iconResolutionDescription(): string {
      const label = english(ClientAppStrings.WebClient.IconResolution.Label);
      const rows = dialog().querySelectorAll<HTMLElement>('.wc-settings-row');
      for (let index = 0; index < rows.length; index++) {
        if (rows[index].textContent!.indexOf(label) === 0) {
          return rows[index].querySelector('.wc-settings-row-desc')!.textContent as string;
        }
      }
      throw new Error('no icon resolution row');
    }

    it('offers automatic and the three icon resolutions, and applies the one picked', () => {
      const surface = iconResolution();
      const fixture = create({ iconResolution: surface });
      fixture.handle.open();

      expect(activeOptions()).toContain(english(ClientAppStrings.WebClient.IconResolution.Automatic));
      option('128 px').click();
      option('512 px').click();
      option(english(ClientAppStrings.WebClient.IconResolution.Automatic)).click();

      expect(surface.set.calls.allArgs()).toEqual([[128], [512], ['auto']]);
    });

    it('says which resolution automatic is using, and follows it while open', () => {
      const surface = iconResolution('auto', { min: 128, max: 128 });
      const fixture = create({ iconResolution: surface });
      fixture.handle.open();

      expect(iconResolutionDescription()).toBe('Currently 128 px.');

      surface.inUseNow = { min: 128, max: 256 };
      surface.notify();

      expect(iconResolutionDescription()).toBe('Currently 128 to 256 px.');
    });

    it('explains the setting instead when a fixed resolution is chosen', () => {
      const surface = iconResolution(256, { min: 128, max: 128 });
      const fixture = create({ iconResolution: surface });
      fixture.handle.open();

      expect(activeOptions()).toContain('256 px');
      expect(iconResolutionDescription()).toBe(english(ClientAppStrings.WebClient.IconResolution.Description));
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

  describe('the open source licenses', () => {
    const notices = 'THIRD-PARTY SOFTWARE NOTICES AND INFORMATION\n\nCronos\n  License: MIT';

    function title(): string {
      return dialog().querySelector('.wc-client-settings-title')?.textContent as string;
    }

    function viewer(): HTMLElement {
      return dialog().querySelector('.wc-client-settings-licenses') as HTMLElement;
    }

    function settingsBody(): HTMLElement {
      return dialog().querySelector('.wc-client-settings-body') as HTMLElement;
    }

    function noticesText(): HTMLElement {
      return dialog().querySelector('.wc-client-settings-licenses-text') as HTMLElement;
    }

    function openViewer(): void {
      (buttonLabelled(english(ClientAppStrings.WebClient.Settings.Licenses.Button)) as HTMLButtonElement).click();
    }

    function viewerLines(): string[] {
      const lines = viewer().querySelectorAll<HTMLElement>('.wc-client-settings-status');
      const found: string[] = [];
      for (let index = 0; index < lines.length; index++) {
        if (lines[index].closest('[hidden]') === null) found.push(lines[index].textContent as string);
      }
      return found;
    }

    function activeOptions(): string[] {
      const active = settingsBody().querySelectorAll<HTMLElement>('.wc-seg-option.wc-seg-active');
      const found: string[] = [];
      for (let index = 0; index < active.length; index++) found.push(active[index].textContent as string);
      return found;
    }

    it('shows the notices in place of the settings, with focus on the way back', async () => {
      const fixture = create();
      fixture.loadNotices.and.resolveTo(notices);
      fixture.handle.open();

      openViewer();

      expect(title()).toBe(english(ClientAppStrings.WebClient.Settings.Licenses.Title));
      expect(settingsBody().hasAttribute('hidden')).toBeTrue();
      expect(viewer().hasAttribute('hidden')).toBeFalse();
      expect(document.activeElement).toBe(buttonLabelled(english(Strings.Common.Back)));
      expect(viewerLines()).toContain(english(Strings.Common.Loading));

      await flush();

      expect(fixture.loadNotices).toHaveBeenCalledTimes(1);
      expect(noticesText().hasAttribute('hidden')).toBeFalse();
      expect(noticesText().textContent).toBe(notices);
      expect(viewerLines()).not.toContain(english(Strings.Common.Loading));
    });

    it('says the notices could not be loaded and fetches them again on retry', async () => {
      const fixture = create();
      fixture.loadNotices.and.rejectWith(new Error('HTTP 404'));
      fixture.handle.open();
      openViewer();
      await flush();

      expect(viewerLines()).toContain(english(ClientAppStrings.WebClient.Settings.Licenses.LoadFailed));
      expect(noticesText().hasAttribute('hidden')).toBeTrue();

      fixture.loadNotices.and.resolveTo(notices);
      (buttonLabelled(english(Strings.Common.Retry)) as HTMLButtonElement).click();
      await flush();

      expect(fixture.loadNotices).toHaveBeenCalledTimes(2);
      expect(viewerLines()).not.toContain(english(ClientAppStrings.WebClient.Settings.Licenses.LoadFailed));
      expect(noticesText().textContent).toBe(notices);
    });

    it('goes back to settings that still follow a theme changed elsewhere', async () => {
      const fixture = create();
      fixture.handle.open();
      openViewer();
      await flush();

      (buttonLabelled(english(Strings.Common.Back)) as HTMLButtonElement).click();

      expect(title()).toBe(english(Strings.Settings.Title));
      expect(viewer().hasAttribute('hidden')).toBeTrue();
      expect(settingsBody().hasAttribute('hidden')).toBeFalse();
      expect(document.activeElement).toBe(buttonLabelled(english(ClientAppStrings.WebClient.Settings.Licenses.Button)));

      fixture.appearance.current = 'dark';
      fixture.appearance.notify();

      expect(activeOptions()).toContain(english(ClientAppStrings.Settings.Appearance.Dark));
    });

    it('closes the whole dialog on Escape while the notices are showing', async () => {
      const fixture = create();
      fixture.handle.open();
      openViewer();
      await flush();

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

      expect(document.querySelector('.wc-client-settings-dialog')).toBeNull();
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
