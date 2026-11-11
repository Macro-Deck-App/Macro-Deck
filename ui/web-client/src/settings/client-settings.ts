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
  type ButtonHandle,
  type ButtonVariant,
  type SegmentedControlHandle,
  type SegmentedOption,
  type SettingsSectionHandle,
  type ToggleHandle,
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
import { ICON_RESOLUTIONS, type IconResolution, type IconSizeRange } from '../icon-resolution';
import { type WakeLockStatus } from '../wake-lock';

const INSTALL_DISMISSED_HINT_MS = 4000;

const GEAR_RING = 'M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033'
  + ' 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0'
  + ' 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051'
  + 'a2.34 2.34 0 0 0 3.319-1.915';

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

export interface IconResolutionSurface {
  get(): IconResolution;
  set(resolution: IconResolution): void;
  onChange(listener: () => void): () => void;
  inUse(): IconSizeRange | null;
}

export interface WakeLockSurface {
  currentStatus(): WakeLockStatus;
  enabled(): boolean;
  setEnabled(on: boolean): void;
  onChange(listener: () => void): () => void;
}

export interface SettingsButtonSurface {
  offered(): boolean;
  connected(): boolean;
  get(): boolean;
  set(hidden: boolean): Promise<boolean>;
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
  iconResolution?: IconResolutionSurface;
  wakeLock: WakeLockSurface;
  settingsButton?: SettingsButtonSurface;
  update: AppUpdateSurface;
  install: PwaInstallSurface;
  capabilities: WebClientTargetCapabilities;
  openDeviceSetup?(): void;
  loadNotices(): Promise<string>;
  legacyEntry?: boolean;
  timer?: HintTimer;
}

export interface ClientSettingsHandle {
  readonly element: HTMLElement;
  open(): void;
  close(): void;
  isOpen(): boolean;
  setButtonHidden(hidden: boolean): void;
  repaint(): void;
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

export function gearIcon(): SVGElement {
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
    case 'awaitingGesture': return ClientAppStrings.WebClient.KeepAwake.AwaitingTap;
    case 'suspended': return ClientAppStrings.WebClient.KeepAwake.Suspended;
    case 'denied': return ClientAppStrings.WebClient.KeepAwake.Denied;
    case 'unsupported': return ClientAppStrings.WebClient.KeepAwake.Unsupported;
    case 'insecureOrigin': return ClientAppStrings.WebClient.KeepAwake.InsecureOrigin;
    case 'pending': return ClientAppStrings.WebClient.KeepAwake.Pending;
    default: return null;
  }
}

type Localize = (paint: () => void) => void;

interface LicensesViewer {
  element: HTMLElement;
  back: HTMLButtonElement;
  loading: HTMLElement;
  notices: HTMLElement;
  failure: HTMLElement;
  showFailure(): void;
  loaded: boolean;
  request: number;
}

interface OpenModal {
  backdrop: HTMLElement;
  heading: HTMLElement;
  body: HTMLElement;
  paints: Array<() => void>;
  licensesButton: HTMLButtonElement;
  licenses: LicensesViewer;
  themeMode: SegmentedControlHandle;
  renderMode: SegmentedControlHandle;
  iconResolution: SegmentedControlHandle | null;
  iconResolutionDescription: HTMLElement | null;
  wakeLockControl: HTMLElement;
  wakeLockStatus: HTMLElement;
  settingsButtonToggle: ToggleHandle | null;
  settingsButtonStatus: HTMLElement;
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
  const paintTrigger = (): void => {
    trigger.setAttribute('aria-label', text(ClientAppStrings.WebClient.Settings.OpenSettings));
  };
  paintTrigger();
  trigger.appendChild(gearIcon());

  let open: OpenModal | null = null;
  let dismissedHintVisible = false;
  let dismissedHintTimer: unknown = null;
  let destroyed = false;
  let settingsButtonPending: boolean | null = null;
  let settingsButtonFailed = false;
  let settingsButtonRequest = 0;

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

  function iconResolutionOptions(): SegmentedOption[] {
    return ICON_RESOLUTIONS.map(resolution => ({
      value: String(resolution),
      label: resolution === 'auto'
        ? text(ClientAppStrings.WebClient.IconResolution.Automatic)
        : text(ClientAppStrings.WebClient.IconResolution.SizeOption, { size: resolution }),
    }));
  }

  function iconResolutionText(surface: IconResolutionSurface): string {
    const inUse = surface.get() === 'auto' ? surface.inUse() : null;
    if (inUse === null) return text(ClientAppStrings.WebClient.IconResolution.Description);
    return inUse.min === inUse.max
      ? text(ClientAppStrings.WebClient.IconResolution.InUse, { size: inUse.min })
      : text(ClientAppStrings.WebClient.IconResolution.InUseRange, { min: inUse.min, max: inUse.max });
  }

  function description(row: HTMLElement): HTMLElement | null {
    return row.querySelector<HTMLElement>('.wc-settings-row-desc');
  }

  function localizedParagraph(localized: Localize, className: string, key: string): HTMLElement {
    const node = paragraph(className, text(key));
    localized(() => {
      node.textContent = text(key);
    });
    return node;
  }

  function localizedButton(
    localized: Localize,
    key: string,
    variant: ButtonVariant,
    onClick: () => void,
  ): ButtonHandle {
    const button = createButton({ label: text(key), variant: variant, onClick: onClick });
    localized(() => button.setLabel(text(key)));
    return button;
  }

  function localizedRow(
    localized: Localize,
    labelKey: string,
    control: HTMLElement,
    describe?: () => string,
  ): HTMLElement {
    const row = createSettingsRow({
      label: text(labelKey),
      description: describe === undefined ? undefined : describe(),
      control: control,
    });
    const label = row.querySelector<HTMLElement>('.wc-settings-row-label');
    const detail = description(row);
    localized(() => {
      if (label !== null) label.textContent = text(labelKey);
      if (detail !== null && describe !== undefined) detail.textContent = describe();
    });
    return row;
  }

  function localizedSection(
    localized: Localize,
    headingKey: string,
    rows?: HTMLElement[],
    descriptionKey?: string,
  ): SettingsSectionHandle {
    const section = createSettingsSection({
      heading: text(headingKey),
      description: descriptionKey === undefined ? undefined : text(descriptionKey),
      rows: rows,
    });
    const heading = section.element.querySelector<HTMLElement>('.wc-settings-section-title');
    const detail = section.element.querySelector<HTMLElement>('.wc-settings-section-desc');
    localized(() => {
      if (heading !== null) heading.textContent = text(headingKey);
      if (detail !== null && descriptionKey !== undefined) detail.textContent = text(descriptionKey);
    });
    return section;
  }

  function legacySection(localized: Localize): HTMLElement {
    const section = localizedSection(localized, ClientAppStrings.WebClient.Legacy.SectionTitle);
    section.body.appendChild(
      localizedParagraph(localized, 'wc-client-settings-status', ClientAppStrings.WebClient.Legacy.Notice));
    section.body.appendChild(
      localizedParagraph(localized, 'wc-client-settings-status', ClientAppStrings.WebClient.Legacy.InstallHint));
    return section.element;
  }

  function deviceSetupSection(localized: Localize, openWizard: () => void): HTMLElement {
    const action = localizedButton(
      localized,
      ClientAppStrings.WebClient.DeviceSetup.OpenAction,
      'secondary',
      () => {
        // The wizard is a dialog of its own, so this one gets out of its way first.
        close();
        openWizard();
      });
    const section = localizedSection(
      localized,
      ClientAppStrings.WebClient.DeviceSetup.SectionTitle,
      undefined,
      ClientAppStrings.WebClient.DeviceSetup.Description);
    section.body.appendChild(action.element);
    return section.element;
  }

  function licensesViewer(localized: Localize, onBack: () => void, onRetry: () => void): LicensesViewer {
    const viewer = element('div', 'wc-client-settings-licenses');
    viewer.setAttribute('hidden', '');

    const back = localizedButton(localized, Strings.Common.Back, 'ghost', onBack);
    const toolbar = element('div', 'wc-client-settings-licenses-toolbar');
    toolbar.appendChild(back.element);
    viewer.appendChild(toolbar);

    const loading = localizedParagraph(localized, 'wc-client-settings-status', Strings.Common.Loading);
    loading.setAttribute('role', 'status');
    viewer.appendChild(loading);

    const failure = element('div', 'wc-client-settings-licenses-failure');
    failure.setAttribute('role', 'alert');
    show(failure, false);
    viewer.appendChild(failure);

    const showFailure = (): void => {
      if (failure.firstChild === null) {
        failure.appendChild(localizedParagraph(
          localized,
          'wc-client-settings-status',
          ClientAppStrings.WebClient.Settings.Licenses.LoadFailed));
        failure.appendChild(localizedButton(localized, Strings.Common.Retry, 'secondary', () => {
          // The retry button is hidden by the reload it starts, so focus moves somewhere that stays.
          back.element.focus();
          onRetry();
        }).element);
      }
      show(failure, true);
    };

    const notices = element('pre', 'wc-client-settings-licenses-text');
    notices.setAttribute('tabindex', '0');
    const paintNotices = (): void => {
      notices.setAttribute('aria-label', text(ClientAppStrings.WebClient.Settings.Licenses.Title));
    };
    paintNotices();
    localized(paintNotices);
    show(notices, false);
    viewer.appendChild(notices);

    return {
      element: viewer,
      back: back.element,
      loading: loading,
      notices: notices,
      failure: failure,
      showFailure: showFailure,
      loaded: false,
      request: 0,
    };
  }

  function build(): OpenModal {
    const paints: Array<() => void> = [];
    const localized: Localize = paint => {
      paints.push(paint);
    };

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
    const paintClose = (): void => {
      closeButton.setAttribute('aria-label', text(Strings.Common.Close));
    };
    paintClose();
    localized(paintClose);
    closeButton.textContent = '×';
    closeButton.addEventListener('click', () => close());
    header.appendChild(closeButton);
    dialog.appendChild(header);

    const body = element('div', 'wc-client-settings-body');
    dialog.appendChild(body);

    // A sibling of the settings body rather than a replacement for it: render() keeps updating the
    // controls in the body while the viewer is showing, so they must stay attached.
    const licenses = licensesViewer(localized, () => closeLicenses(), () => {
      const modal = open;
      if (modal !== null) void loadLicenses(modal);
    });
    dialog.appendChild(licenses.element);
    localized(() => {
      heading.textContent = licenses.element.hasAttribute('hidden')
        ? text(Strings.Settings.Title)
        : text(ClientAppStrings.WebClient.Settings.Licenses.Title);
    });

    if (options.legacyEntry === true) body.appendChild(legacySection(localized));

    const openWizard = options.openDeviceSetup;
    if (openWizard !== undefined && options.capabilities.deviceSetupPrompts) {
      body.appendChild(deviceSetupSection(localized, openWizard));
    }

    const renderMode = createSegmentedControl({
      ariaLabel: text(ClientAppStrings.WebClient.Rendering.Label),
      options: renderModeOptions(),
      value: options.renderingMode.get(),
      onChange: value => options.renderingMode.set(value as RenderingMode),
    });
    localized(() => {
      renderMode.setAriaLabel(text(ClientAppStrings.WebClient.Rendering.Label));
      renderMode.setOptions(renderModeOptions());
    });

    const iconSurface = options.iconResolution;
    const iconResolution = iconSurface === undefined ? null : createSegmentedControl({
      ariaLabel: text(ClientAppStrings.WebClient.IconResolution.Label),
      options: iconResolutionOptions(),
      value: String(iconSurface.get()),
      onChange: value => iconSurface.set(value === 'auto' ? 'auto' : Number(value) as IconResolution),
    });
    if (iconResolution !== null) {
      localized(() => {
        iconResolution.setAriaLabel(text(ClientAppStrings.WebClient.IconResolution.Label));
        iconResolution.setOptions(iconResolutionOptions());
      });
    }

    const themeMode = createSegmentedControl({
      ariaLabel: text(ClientAppStrings.Settings.Appearance.ThemeModeLabel),
      options: themeOptions(),
      value: options.appearance.themeMode(),
      onChange: value => options.appearance.setThemeMode(value as ThemeMode),
    });
    localized(() => {
      themeMode.setAriaLabel(text(ClientAppStrings.Settings.Appearance.ThemeModeLabel));
      themeMode.setOptions(themeOptions());
    });

    const wakeLockControl = element('div', 'wc-client-settings-control');
    const wakeLockStatus = paragraph('wc-client-settings-status', '');

    // A target whose firmware already keeps its screen lit declares `wakeLock: false`, and offering
    // a switch that does nothing is precisely the dead control the capability list exists to remove.
    const displayRows = [];
    displayRows.push(
        localizedRow(
          localized,
          ClientAppStrings.WebClient.Rendering.Label,
          renderMode.element,
          () => text(ClientAppStrings.WebClient.Rendering.Description)),
        localizedRow(
          localized,
          ClientAppStrings.Settings.Appearance.ThemeModeLabel,
          themeMode.element,
          () => text(ClientAppStrings.Settings.Appearance.ThemeModeDescription)),
    );

    let iconResolutionRow: HTMLElement | null = null;
    if (iconSurface !== undefined && iconResolution !== null) {
      iconResolutionRow = localizedRow(
        localized,
        ClientAppStrings.WebClient.IconResolution.Label,
        iconResolution.element,
        () => iconResolutionText(iconSurface));
      displayRows.push(iconResolutionRow);
    }

    const keepAwakeOffered = options.capabilities.wakeLock;
    if (keepAwakeOffered) {
      displayRows.push(localizedRow(
        localized,
        ClientAppStrings.WebClient.KeepAwake.Label,
        wakeLockControl,
        () => text(ClientAppStrings.WebClient.KeepAwake.Description)));
    }

    const settingsButton = options.settingsButton;
    const settingsButtonStatus = paragraph('wc-client-settings-status', '');
    let settingsButtonToggle: ToggleHandle | null = null;
    if (settingsButton !== undefined && settingsButton.offered()) {
      const toggle = createToggle({
        ariaLabel: text(ClientAppStrings.WebClient.Settings.HideSettingsButton.Label),
        checked: settingsButton.get(),
        onChange: hidden => {
          void saveSettingsButton(settingsButton, hidden);
        },
      });
      localized(() => toggle.setAriaLabel(text(ClientAppStrings.WebClient.Settings.HideSettingsButton.Label)));
      settingsButtonToggle = toggle;
      displayRows.push(localizedRow(
        localized,
        ClientAppStrings.WebClient.Settings.HideSettingsButton.Label,
        toggle.element,
        () => text(ClientAppStrings.WebClient.Settings.HideSettingsButton.Description)));
    }

    const display = localizedSection(localized, ClientAppStrings.WebClient.Settings.DisplaySection, displayRows);
    if (keepAwakeOffered) display.body.appendChild(wakeLockStatus);
    if (settingsButtonToggle !== null) display.body.appendChild(settingsButtonStatus);
    body.appendChild(display.element);

    const installControl = element('div', 'wc-client-settings-control');
    const installRow = localizedRow(
      localized,
      ClientAppStrings.WebClient.Install.Label,
      installControl,
      () => text(installStatusKey(options.install.availability())));
    const installNote = paragraph('wc-client-settings-status', '');

    const updateControl = element('div', 'wc-client-settings-control');
    const updateRow = localizedRow(
      localized,
      ClientAppStrings.WebClient.Install.UpdateLabel,
      updateControl,
      () => text(updateStatusKey(options.update.phase())));

    const install = localizedSection(localized, ClientAppStrings.WebClient.Install.SectionTitle, [installRow]);
    install.body.appendChild(installNote);
    body.appendChild(install.element);

    const signOut = localizedButton(localized, ClientAppStrings.WebClient.Account.SignOut, 'secondary', () => {
      void options.client.signOut();
    });
    body.appendChild(localizedSection(
      localized,
      ClientAppStrings.WebClient.Account.SectionTitle,
      [localizedRow(localized, ClientAppStrings.WebClient.Account.SignedInLabel, signOut.element)]).element);

    const licensesButton = localizedButton(
      localized,
      ClientAppStrings.WebClient.Settings.Licenses.Button,
      'ghost',
      () => openLicenses());
    const licensesEntry = element('div', 'wc-client-settings-licenses-entry');
    licensesEntry.appendChild(licensesButton.element);
    body.appendChild(licensesEntry);

    backdrop.addEventListener('click', event => {
      if (event.target === backdrop) close();
    });

    return {
      backdrop: backdrop,
      heading: heading,
      body: body,
      paints: paints,
      licensesButton: licensesButton.element,
      licenses: licenses,
      themeMode: themeMode,
      renderMode: renderMode,
      iconResolution: iconResolution,
      iconResolutionDescription: iconResolutionRow === null ? null : description(iconResolutionRow),
      wakeLockControl: wakeLockControl,
      wakeLockStatus: wakeLockStatus,
      settingsButtonToggle: settingsButtonToggle,
      settingsButtonStatus: settingsButtonStatus,
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

  function renderSettingsButton(modal: OpenModal): void {
    const surface = options.settingsButton;
    const toggle = modal.settingsButtonToggle;
    if (surface === undefined || toggle === null) return;

    toggle.setChecked(settingsButtonPending === null ? surface.get() : settingsButtonPending);
    toggle.setBusy(settingsButtonPending !== null);
    toggle.setDisabled(!surface.connected());
    modal.settingsButtonStatus.textContent = settingsButtonFailed
      ? text(ClientAppStrings.WebClient.Settings.HideSettingsButton.SaveFailed)
      : '';
    show(modal.settingsButtonStatus, settingsButtonFailed);
  }

  async function saveSettingsButton(surface: SettingsButtonSurface, hidden: boolean): Promise<void> {
    const modal = open;
    const request = ++settingsButtonRequest;
    settingsButtonPending = hidden;
    settingsButtonFailed = false;
    render();

    let saved = false;
    try {
      saved = await surface.set(hidden);
    } catch {
      saved = false;
    }
    if (open !== modal || request !== settingsButtonRequest) return;

    settingsButtonPending = null;
    settingsButtonFailed = !saved;
    render();
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
    const iconSurface = options.iconResolution;
    if (iconSurface !== undefined) {
      if (modal.iconResolution !== null) modal.iconResolution.setValue(String(iconSurface.get()));
      if (modal.iconResolutionDescription !== null) {
        modal.iconResolutionDescription.textContent = iconResolutionText(iconSurface);
      }
    }
    modal.themeMode.setValue(options.appearance.themeMode());
    renderWakeLock(modal);
    renderSettingsButton(modal);
    renderInstall(modal);
    renderUpdate(modal);
  }

  function repaint(): void {
    paintTrigger();
    const modal = open;
    if (modal === null) return;
    for (let index = 0; index < modal.paints.length; index++) modal.paints[index]();
    render();
  }

  function openLicenses(): void {
    const modal = open;
    if (modal === null) return;
    show(modal.body, false);
    show(modal.licenses.element, true);
    modal.heading.textContent = text(ClientAppStrings.WebClient.Settings.Licenses.Title);
    modal.licenses.back.focus();
    if (!modal.licenses.loaded) void loadLicenses(modal);
  }

  function closeLicenses(): void {
    const modal = open;
    if (modal === null) return;
    show(modal.licenses.element, false);
    show(modal.body, true);
    modal.heading.textContent = text(Strings.Settings.Title);
    modal.licensesButton.focus();
  }

  async function loadLicenses(modal: OpenModal): Promise<void> {
    const viewer = modal.licenses;
    const request = ++viewer.request;
    show(viewer.loading, true);
    show(viewer.failure, false);
    show(viewer.notices, false);

    let notices: string | null = null;
    try {
      notices = await options.loadNotices();
    } catch {
      notices = null;
    }
    if (open !== modal || request !== viewer.request) return;

    show(viewer.loading, false);
    if (notices === null) {
      viewer.showFailure();
      return;
    }
    viewer.notices.textContent = notices;
    viewer.loaded = true;
    show(viewer.notices, true);
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
    settingsButtonPending = null;
    settingsButtonFailed = false;
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
  const unsubscribeIconResolution = options.iconResolution?.onChange(() => render());
  const unsubscribeSettingsButton = options.settingsButton === undefined
    ? () => undefined
    : options.settingsButton.onChange(() => render());
  // Neither of these hands back an unsubscribe, so they are taken once here rather than per open,
  // and answered with nothing once the modal is closed.
  options.update.onPhaseChange(() => render());
  options.install.onChange(() => render());

  return {
    element: trigger,
    open: () => openModal(),
    close: () => close(),
    isOpen: () => open !== null,
    setButtonHidden: (hidden: boolean) => show(trigger, !hidden),
    repaint: () => repaint(),
    destroy: () => {
      destroyed = true;
      close();
      timer.clear(dismissedHintTimer);
      unsubscribeAppearance();
      unsubscribeRendering();
      unsubscribeWakeLock();
      if (unsubscribeIconResolution !== undefined) unsubscribeIconResolution();
      unsubscribeSettingsButton();
      if (trigger.parentNode !== null) trigger.parentNode.removeChild(trigger);
    },
  };
}
