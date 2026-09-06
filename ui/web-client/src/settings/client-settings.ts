import {
  ClientAppStrings,
  Strings,
  type ThemeMode,
  type WebClientTargetCapabilities,
} from '@macro-deck/runtime';
import {
  createButton,
  createSegmentedControl,
  createSettingsRow,
  createSettingsSection,
  createToggle,
  type SegmentedOption,
} from '../ui';
import {
  installHintKey,
  installStatusKey,
  updateActionKey,
  updateStatusKey,
  type AppUpdatePhase,
  type PwaAvailability,
  type PwaInstallOutcome,
} from '../pwa';
import { type RenderingMode } from '../rendering-mode';
import { type WakeLockStatus } from '../wake-lock';

const INSTALL_DISMISSED_HINT_MS = 4000;

const GEAR_RING = 'M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0'
  + ' 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0'
  + ' 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2'
  + ' 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65'
  + ' 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0'
  + ' 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2'
  + ' 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z';

const SVG_NS = 'http://www.w3.org/2000/svg';

export interface SettingsTranslator {
  translate(qualifiedKey: string, args?: Record<string, unknown>): string;
  signOut(): Promise<void>;
}

export interface AppearanceSurface {
  themeMode(): ThemeMode;
  setThemeMode(mode: ThemeMode): void;
  onChange(listener: () => void): () => void;
}

export interface RenderingModeSurface {
  get(): RenderingMode;
  set(mode: RenderingMode): void;
  onChange(listener: () => void): () => void;
}

export interface WakeLockSurface {
  currentStatus(): WakeLockStatus;
  enabled(): boolean;
  setEnabled(on: boolean): void;
  onChange(listener: () => void): () => void;
}

export interface AppUpdateSurface {
  phase(): AppUpdatePhase;
  onPhaseChange(listener: (phase: AppUpdatePhase) => void): void;
  activateNow(): Promise<void>;
  check(): Promise<void>;
}

export interface PwaInstallSurface {
  availability(): PwaAvailability;
  onChange(listener: (availability: PwaAvailability) => void): void;
  promptInstall(): Promise<PwaInstallOutcome>;
}

export interface HintTimer {
  set(callback: () => void, delayMs: number): unknown;
  clear(handle: unknown): void;
}

const REAL_TIMER: HintTimer = {
  set: (callback: () => void, delayMs: number) => setTimeout(callback, delayMs),
  clear: (handle: unknown) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export interface ClientSettingsOptions {
  client: SettingsTranslator;
  appearance: AppearanceSurface;
  renderingMode: RenderingModeSurface;
  wakeLock: WakeLockSurface;
  update: AppUpdateSurface;
  install: PwaInstallSurface;
  capabilities: WebClientTargetCapabilities;
  openDeviceSetup?(): void;
  legacyEntry?: boolean;
  timer?: HintTimer;
}

export interface ClientSettingsHandle {
  readonly element: HTMLElement;
  open(): void;
  close(): void;
  destroy(): void;
}

function element<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
): HTMLElementTagNameMap[K] {
  const created = document.createElement(tag);
  if (className !== undefined) created.className = className;
  return created;
}

function paragraph(className: string, text: string): HTMLElement {
  const node = element('p', className);
  node.textContent = text;
  return node;
}

function clear(node: HTMLElement): void {
  while (node.firstChild !== null) node.removeChild(node.firstChild);
}

function show(node: HTMLElement, visible: boolean): void {
  if (visible) node.removeAttribute('hidden');
  else node.setAttribute('hidden', '');
}

function gearIcon(): SVGElement {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('class', 'wc-client-settings-icon');
  svg.setAttribute('viewBox', '0 0 24 24');
  // An intrinsic size as well as the viewBox: an inline <svg> sized only by CSS falls back to the
  // replaced-element default of 300x150 on the compatibility floor, which is how a 44px button
  // ended up with an icon spilling out of it (issue #829). CSS still wins where CSS is honoured.
  svg.setAttribute('width', '24');
  svg.setAttribute('height', '24');
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2');
  svg.setAttribute('stroke-linecap', 'round');
  svg.setAttribute('stroke-linejoin', 'round');
  svg.setAttribute('aria-hidden', 'true');

  const circle = document.createElementNS(SVG_NS, 'circle');
  circle.setAttribute('cx', '12');
  circle.setAttribute('cy', '12');
  circle.setAttribute('r', '3');
  svg.appendChild(circle);

  const ring = document.createElementNS(SVG_NS, 'path');
  ring.setAttribute('d', GEAR_RING);
  svg.appendChild(ring);

  return svg;
}

function wakeLockStatusKey(status: WakeLockStatus): string | null {
  switch (status) {
    case 'active': return ClientAppStrings.WebClient.KeepAwake.Active;
    case 'suspended': return ClientAppStrings.WebClient.KeepAwake.Suspended;
    case 'denied': return ClientAppStrings.WebClient.KeepAwake.Denied;
    case 'unsupported': return ClientAppStrings.WebClient.KeepAwake.Unsupported;
    case 'insecureOrigin': return ClientAppStrings.WebClient.KeepAwake.InsecureOrigin;
    case 'pending': return ClientAppStrings.WebClient.KeepAwake.Pending;
    default: return null;
  }
}

interface OpenModal {
  backdrop: HTMLElement;
  themeMode: { setValue(value: string | null): void };
  renderMode: { setValue(value: string | null): void };
  wakeLockControl: HTMLElement;
  wakeLockStatus: HTMLElement;
  installDescription: HTMLElement | null;
  installControl: HTMLElement;
  installNote: HTMLElement;
  updateRow: HTMLElement;
  updateSlot: HTMLElement;
  updateDescription: HTMLElement | null;
  updateControl: HTMLElement;
}

export function createClientSettings(options: ClientSettingsOptions): ClientSettingsHandle | null {
  if (!options.capabilities.clientSettings) return null;

  const timer = options.timer === undefined ? REAL_TIMER : options.timer;
  const text = (key: string, args?: Record<string, unknown>): string =>
    options.client.translate(key, args);

  const trigger = element('button', 'wc-client-settings-trigger');
  trigger.type = 'button';
  trigger.setAttribute('aria-label', text(ClientAppStrings.WebClient.Settings.OpenSettings));
  trigger.appendChild(gearIcon());

  let open: OpenModal | null = null;
  let dismissedHintVisible = false;
  let dismissedHintTimer: unknown = null;
  let destroyed = false;

  function themeOptions(): SegmentedOption[] {
    // The deck runs on tablets whose OS may have no colour-scheme preference to follow at all - iOS
    // only gained one in 13 - so "system" cannot be the only way to get a dark deck.
    return [
      { value: 'light', label: text(ClientAppStrings.Settings.Appearance.Light), icon: 'sun' },
      { value: 'dark', label: text(ClientAppStrings.Settings.Appearance.Dark), icon: 'moon' },
      { value: 'system', label: text(ClientAppStrings.Settings.Appearance.System) },
    ];
  }

  function renderModeOptions(): SegmentedOption[] {
    return [
      { value: 'standard', label: text(ClientAppStrings.WebClient.Rendering.Standard) },
      { value: 'simple', label: text(ClientAppStrings.WebClient.Rendering.Simple) },
    ];
  }

  function description(row: HTMLElement): HTMLElement | null {
    return row.querySelector<HTMLElement>('.wc-settings-row-desc');
  }

  function legacySection(): HTMLElement {
    const section = createSettingsSection({
      heading: text(ClientAppStrings.WebClient.Legacy.SectionTitle),
    });
    section.body.appendChild(
      paragraph('wc-client-settings-status', text(ClientAppStrings.WebClient.Legacy.Notice)));
    section.body.appendChild(
      paragraph('wc-client-settings-status', text(ClientAppStrings.WebClient.Legacy.InstallHint)));
    return section.element;
  }

  function deviceSetupSection(openWizard: () => void): HTMLElement {
    const action = createButton({
      label: text(ClientAppStrings.WebClient.DeviceSetup.OpenAction),
      variant: 'secondary',
      onClick: () => {
        // The wizard is a dialog of its own, so this one gets out of its way first.
        close();
        openWizard();
      },
    });
    const section = createSettingsSection({
      heading: text(ClientAppStrings.WebClient.DeviceSetup.SectionTitle),
      description: text(ClientAppStrings.WebClient.DeviceSetup.Description),
    });
    section.body.appendChild(action.element);
    return section.element;
  }

  function build(): OpenModal {
    const backdrop = element('div', 'wc-client-settings-backdrop');
    const dialog = element('div', 'wc-client-settings-dialog');
    dialog.setAttribute('role', 'dialog');
    dialog.setAttribute('aria-modal', 'true');
    backdrop.appendChild(dialog);

    const header = element('div', 'wc-client-settings-header');
    const heading = element('h2', 'wc-client-settings-title');
    heading.textContent = text(Strings.Settings.Title);
    header.appendChild(heading);

    const closeButton = element('button', 'wc-client-settings-close');
    closeButton.type = 'button';
    closeButton.setAttribute('aria-label', text(Strings.Common.Close));
    closeButton.textContent = '×';
    closeButton.addEventListener('click', () => close());
    header.appendChild(closeButton);
    dialog.appendChild(header);

    const body = element('div', 'wc-client-settings-body');
    dialog.appendChild(body);

    if (options.legacyEntry === true) body.appendChild(legacySection());

    const openWizard = options.openDeviceSetup;
    if (openWizard !== undefined && options.capabilities.deviceSetupPrompts) {
      body.appendChild(deviceSetupSection(openWizard));
    }

    const renderMode = createSegmentedControl({
      ariaLabel: text(ClientAppStrings.WebClient.Rendering.Label),
      options: renderModeOptions(),
      value: options.renderingMode.get(),
      onChange: value => options.renderingMode.set(value as RenderingMode),
    });

    const themeMode = createSegmentedControl({
      ariaLabel: text(ClientAppStrings.Settings.Appearance.ThemeModeLabel),
      options: themeOptions(),
      value: options.appearance.themeMode(),
      onChange: value => options.appearance.setThemeMode(value as ThemeMode),
    });

    const wakeLockControl = element('div', 'wc-client-settings-control');
    const wakeLockStatus = paragraph('wc-client-settings-status', '');

    // A target whose firmware already keeps its screen lit declares `wakeLock: false`, and offering
    // a switch that does nothing is precisely the dead control the capability list exists to remove.
    const displayRows = [];
    displayRows.push(
        createSettingsRow({
          label: text(ClientAppStrings.WebClient.Rendering.Label),
          description: text(ClientAppStrings.WebClient.Rendering.Description),
          control: renderMode.element,
        }),
        createSettingsRow({
          label: text(ClientAppStrings.Settings.Appearance.ThemeModeLabel),
          description: text(ClientAppStrings.Settings.Appearance.ThemeModeDescription),
          control: themeMode.element,
        }),
    );

    const keepAwakeOffered = options.capabilities.wakeLock;
    if (keepAwakeOffered) {
      displayRows.push(createSettingsRow({
        label: text(ClientAppStrings.WebClient.KeepAwake.Label),
        description: text(ClientAppStrings.WebClient.KeepAwake.Description),
        control: wakeLockControl,
      }));
    }

    const display = createSettingsSection({
      heading: text(ClientAppStrings.WebClient.Settings.DisplaySection),
      rows: displayRows,
    });
    if (keepAwakeOffered) display.body.appendChild(wakeLockStatus);
    body.appendChild(display.element);

    const installControl = element('div', 'wc-client-settings-control');
    const installRow = createSettingsRow({
      label: text(ClientAppStrings.WebClient.Install.Label),
      description: text(installStatusKey(options.install.availability())),
      control: installControl,
    });
    const installNote = paragraph('wc-client-settings-status', '');

    const updateControl = element('div', 'wc-client-settings-control');
    const updateRow = createSettingsRow({
      label: text(ClientAppStrings.WebClient.Install.UpdateLabel),
      description: text(updateStatusKey(options.update.phase())),
      control: updateControl,
    });

    const install = createSettingsSection({
      heading: text(ClientAppStrings.WebClient.Install.SectionTitle),
      rows: [installRow],
    });
    install.body.appendChild(installNote);
    body.appendChild(install.element);

    const signOut = createButton({
      label: text(ClientAppStrings.WebClient.Account.SignOut),
      variant: 'secondary',
      onClick: () => {
        void options.client.signOut();
      },
    });
    body.appendChild(createSettingsSection({
      heading: text(ClientAppStrings.WebClient.Account.SectionTitle),
      rows: [createSettingsRow({
        label: text(ClientAppStrings.WebClient.Account.SignedInLabel),
        control: signOut.element,
      })],
    }).element);

    backdrop.addEventListener('click', event => {
      if (event.target === backdrop) close();
    });

    return {
      backdrop: backdrop,
      themeMode: themeMode,
      renderMode: renderMode,
      wakeLockControl: wakeLockControl,
      wakeLockStatus: wakeLockStatus,
      installDescription: description(installRow),
      installControl: installControl,
      installNote: installNote,
      updateRow: updateRow,
      updateSlot: install.body,
      updateDescription: description(updateRow),
      updateControl: updateControl,
    };
  }

  function renderWakeLock(modal: OpenModal): void {
    const status = options.wakeLock.currentStatus();
    clear(modal.wakeLockControl);

    // A switch that can never be flipped is worse than no switch: it invites a tap and answers with
    // nothing. Both of these states are properties of the browser or the address, not something the
    // user can change from here, so the row keeps only its explanation.
    if (status !== 'unsupported' && status !== 'insecureOrigin') {
      const toggle = createToggle({
        ariaLabel: text(ClientAppStrings.WebClient.KeepAwake.Label),
        checked: options.wakeLock.enabled(),
        busy: status === 'pending',
        // Runs straight from the click, not from a later microtask: the acquire has to carry the
        // user gesture the platform requires of it.
        onChange: on => options.wakeLock.setEnabled(on),
      });
      modal.wakeLockControl.appendChild(toggle.element);
    }

    const key = wakeLockStatusKey(status);
    modal.wakeLockStatus.textContent = key === null ? '' : text(key);
    show(modal.wakeLockStatus, key !== null);
  }

  function renderInstall(modal: OpenModal): void {
    const availability = options.install.availability();
    if (modal.installDescription !== null) {
      modal.installDescription.textContent = text(installStatusKey(availability));
    }

    clear(modal.installControl);
    if (availability === 'promptable' && !dismissedHintVisible) {
      const action = createButton({
        label: text(ClientAppStrings.WebClient.Install.InstallAction),
        variant: 'secondary',
        onClick: () => {
          void promptInstall();
        },
      });
      modal.installControl.appendChild(action.element);
    }

    // Explanatory text belongs under the row, not in its control slot: the slot is laid out beside
    // the label, so a sentence there takes most of the row's width and reads as if it were the
    // control.
    const hint = dismissedHintVisible
      ? ClientAppStrings.WebClient.Install.InstallDismissed
      : installHintKey(availability);
    modal.installNote.textContent = hint === null ? '' : text(hint);
    show(modal.installNote, hint !== null);
  }

  function renderUpdate(modal: OpenModal): void {
    const phase = options.update.phase();
    // Taken off the page rather than hidden: a client that cannot update itself should not read out
    // an update row to a screen reader, and `hidden` loses to the row's own display rule anyway.
    // A browser tab is taken off too, whatever the phase: reloading the tab is how a tab updates,
    // and the out-of-date guard already forces that - the row is for the installed app, which has
    // no reload button of its own.
    const attached = modal.updateRow.parentNode !== null;
    if (phase === 'unsupported' || options.install.availability() !== 'runningAsApp') {
      if (attached) modal.updateSlot.removeChild(modal.updateRow);
      return;
    }
    if (!attached) modal.updateSlot.appendChild(modal.updateRow);

    if (modal.updateDescription !== null) {
      modal.updateDescription.textContent = text(updateStatusKey(phase));
    }

    clear(modal.updateControl);
    const actionKey = updateActionKey(phase);
    if (actionKey === null) return;

    const action = createButton({
      label: text(actionKey),
      variant: 'secondary',
      onClick: () => runUpdateAction(),
    });
    modal.updateControl.appendChild(action.element);
  }

  function render(): void {
    const modal = open;
    if (modal === null) return;

    modal.renderMode.setValue(options.renderingMode.get());
    modal.themeMode.setValue(options.appearance.themeMode());
    renderWakeLock(modal);
    renderInstall(modal);
    renderUpdate(modal);
  }

  function runUpdateAction(): void {
    const phase = options.update.phase();
    if (phase === 'available') {
      void options.update.activateNow();
      return;
    }
    if (phase === 'idle' || phase === 'upToDate' || phase === 'checkFailed' || phase === 'applyFailed') {
      void options.update.check();
    }
  }

  async function promptInstall(): Promise<void> {
    if (options.install.availability() !== 'promptable') return;

    const outcome = await options.install.promptInstall();
    if (outcome !== 'dismissed' || destroyed) return;

    dismissedHintVisible = true;
    render();
    timer.clear(dismissedHintTimer);
    dismissedHintTimer = timer.set(() => {
      dismissedHintVisible = false;
      render();
    }, INSTALL_DISMISSED_HINT_MS);
  }

  function onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') close();
  }

  function openModal(): void {
    if (open !== null || destroyed) return;
    open = build();
    document.body.appendChild(open.backdrop);
    document.addEventListener('keydown', onKeydown);
    render();
  }

  function close(): void {
    const modal = open;
    if (modal === null) return;
    open = null;
    document.removeEventListener('keydown', onKeydown);
    if (modal.backdrop.parentNode !== null) modal.backdrop.parentNode.removeChild(modal.backdrop);
  }

  trigger.addEventListener('click', () => openModal());

  const unsubscribeAppearance = options.appearance.onChange(() => render());
  const unsubscribeRendering = options.renderingMode.onChange(() => render());
  const unsubscribeWakeLock = options.wakeLock.onChange(() => render());
  // Neither of these hands back an unsubscribe, so they are taken once here rather than per open,
  // and answered with nothing once the modal is closed.
  options.update.onPhaseChange(() => render());
  options.install.onChange(() => render());

  return {
    element: trigger,
    open: () => openModal(),
    close: () => close(),
    destroy: () => {
      destroyed = true;
      close();
      timer.clear(dismissedHintTimer);
      unsubscribeAppearance();
      unsubscribeRendering();
      unsubscribeWakeLock();
      if (trigger.parentNode !== null) trigger.parentNode.removeChild(trigger);
    },
  };
}
