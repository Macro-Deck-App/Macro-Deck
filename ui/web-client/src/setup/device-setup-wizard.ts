import {
  ClientAppStrings,
  Strings,
  type GetDeviceSetupResponse,
  type LocalizationCatalog,
} from '@macro-deck/runtime';
import { createButton, createSettingsRow } from '../ui';
import { DeviceSetupService, type CertificateTrust } from './device-setup';
import { detectPlatform, type Platform, type PlatformNavigator } from '../pwa/platform';

export type PwaAvailability =
  | 'runningAsApp'
  | 'promptable'
  | 'manualOnly'
  | 'requiresHttps'
  | 'unsupported';

export interface PwaInstall {
  availability(): PwaAvailability;
  promptInstall(): Promise<unknown>;
}

export interface DeviceSetupWizardOptions {
  deviceSetup: DeviceSetupService;
  localization: LocalizationCatalog;
  pwaInstall?: PwaInstall;
  navigator?: PlatformNavigator;
  hostname?: string;
  onClose?(): void;
}

export interface DeviceSetupWizardHandle {
  readonly element: HTMLElement;
  close(): void;
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

function translate(
  localization: LocalizationCatalog,
  qualifiedKey: string,
  args?: Record<string, unknown>,
): string {
  const separator = qualifiedKey.indexOf(':');
  if (separator < 0) return '[[' + qualifiedKey + ']]';
  return localization.translate(
    qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1), args);
}

const IOS_STEPS = [
  ClientAppStrings.WebClient.DeviceSetup.IosStep1,
  ClientAppStrings.WebClient.DeviceSetup.IosStep2,
  ClientAppStrings.WebClient.DeviceSetup.IosStep3,
  ClientAppStrings.WebClient.DeviceSetup.IosStep4,
  ClientAppStrings.WebClient.DeviceSetup.IosStep5,
  ClientAppStrings.WebClient.DeviceSetup.IosStep6,
];

const ANDROID_STEPS = [
  ClientAppStrings.WebClient.DeviceSetup.AndroidStep1,
  ClientAppStrings.WebClient.DeviceSetup.AndroidStep2,
  ClientAppStrings.WebClient.DeviceSetup.AndroidStep3,
  ClientAppStrings.WebClient.DeviceSetup.AndroidStep4,
  ClientAppStrings.WebClient.DeviceSetup.AndroidStep5,
];

export function createDeviceSetupWizard(
  options: DeviceSetupWizardOptions,
): DeviceSetupWizardHandle {
  const localization = options.localization;
  const deviceSetup = options.deviceSetup;
  const text = (key: string, args?: Record<string, unknown>): string =>
    translate(localization, key, args);

  const navigatorLike: PlatformNavigator = options.navigator === undefined
    ? navigator
    : options.navigator;
  const platform: Platform = detectPlatform(navigatorLike);
  const hostname = options.hostname === undefined
    ? (typeof location === 'undefined' ? '' : location.hostname)
    : options.hostname;

  const backdrop = element('div', 'wc-setup-backdrop');
  const dialog = element('div', 'wc-setup-dialog');
  dialog.setAttribute('role', 'dialog');
  dialog.setAttribute('aria-modal', 'true');
  backdrop.appendChild(dialog);

  const header = element('div', 'wc-setup-header');
  const heading = element('h2', 'wc-setup-title');
  heading.textContent = text(ClientAppStrings.WebClient.DeviceSetup.Title);
  header.appendChild(heading);

  const closeButton = element('button', 'wc-setup-close');
  closeButton.type = 'button';
  closeButton.setAttribute('aria-label', text(Strings.Common.Close));
  closeButton.textContent = '×';
  header.appendChild(closeButton);
  dialog.appendChild(header);

  const body = element('div', 'wc-setup-body');
  dialog.appendChild(body);

  const connectionDescription = element('span', 'wc-settings-row-desc');
  const certificateDescription = element('span', 'wc-settings-row-desc');
  const appDescription = element('span', 'wc-settings-row-desc');

  body.appendChild(step(text(ClientAppStrings.WebClient.DeviceSetup.StepConnection), connectionDescription));
  body.appendChild(step(text(ClientAppStrings.WebClient.DeviceSetup.StepCertificate), certificateDescription));

  const installControl = element('div', 'wc-setup-install');
  body.appendChild(step(text(ClientAppStrings.WebClient.DeviceSetup.StepApp), appDescription, installControl));

  const actions = element('div', 'wc-setup-actions');
  const download = element('a', 'wc-setup-download');
  download.textContent = text(ClientAppStrings.WebClient.DeviceSetup.DownloadAction);
  download.setAttribute('download', '');
  actions.appendChild(download);

  const recheck = createButton({
    label: text(ClientAppStrings.WebClient.DeviceSetup.RecheckAction),
    variant: 'secondary',
    onClick: () => {
      void deviceSetup.recheck();
    },
  });
  actions.appendChild(recheck.element);
  body.appendChild(actions);

  const downloadedHint = paragraph(
    'wc-setup-hint wc-setup-downloaded', text(ClientAppStrings.WebClient.DeviceSetup.DownloadedHint));
  body.appendChild(downloadedHint);

  const fingerprintBlock = element('div', 'wc-setup-fingerprint-block');
  fingerprintBlock.appendChild(
    paragraph('wc-setup-hint', text(ClientAppStrings.Settings.Network.Tls.FingerprintLabel)));
  const fingerprint = paragraph('wc-setup-fingerprint', '');
  fingerprintBlock.appendChild(fingerprint);
  fingerprintBlock.appendChild(
    paragraph('wc-setup-hint', text(ClientAppStrings.WebClient.DeviceSetup.FingerprintHint)));
  body.appendChild(fingerprintBlock);

  body.appendChild(paragraph('wc-setup-notice', text(ClientAppStrings.WebClient.DeviceSetup.OsStepWarning)));
  body.appendChild(paragraph('wc-setup-notice', text(ClientAppStrings.WebClient.DeviceSetup.PrivateKeyNote)));

  body.appendChild(platformSteps());

  const openSecure = element('div', 'wc-setup-open-secure');
  const openSecureButton = createButton({
    label: text(ClientAppStrings.WebClient.DeviceSetup.OpenSecureAction),
    variant: 'secondary',
    onClick: () => deviceSetup.openSecureAddress(),
  });
  openSecure.appendChild(openSecureButton.element);
  openSecure.appendChild(
    paragraph('wc-setup-notice', text(ClientAppStrings.WebClient.DeviceSetup.SignInAgainWarning)));
  body.appendChild(openSecure);

  const why = element('details', 'wc-setup-why');
  const summary = element('summary');
  summary.textContent = text(ClientAppStrings.WebClient.DeviceSetup.WhyHeading);
  why.appendChild(summary);
  why.appendChild(paragraph('wc-setup-why-body', text(ClientAppStrings.WebClient.DeviceSetup.WhyBody)));
  body.appendChild(why);

  let downloaded = false;
  let closed = false;

  function step(label: string, description: HTMLElement, control?: HTMLElement): HTMLElement {
    const row = createSettingsRow({
      label: label,
      control: control === undefined ? element('span', 'wc-setup-step-spacer') : control,
    });
    const info = row.querySelector('.wc-settings-row-info');
    if (info !== null) info.appendChild(description);
    return row;
  }

  function platformSteps(): HTMLElement {
    const section = element('section', 'wc-setup-platform-steps');
    const title = element('h3');
    if (platform === 'desktop') {
      title.textContent = text(ClientAppStrings.WebClient.DeviceSetup.DesktopHeading);
      section.appendChild(title);
      section.appendChild(paragraph('wc-setup-platform-body',
        text(ClientAppStrings.WebClient.DeviceSetup.DesktopBody)));
      return section;
    }

    const isIos = platform === 'ios';
    title.textContent = text(isIos
      ? ClientAppStrings.WebClient.DeviceSetup.IosHeading
      : ClientAppStrings.WebClient.DeviceSetup.AndroidHeading);
    section.appendChild(title);

    const list = element('ol');
    const keys = isIos ? IOS_STEPS : ANDROID_STEPS;
    for (let index = 0; index < keys.length; index++) {
      const item = element('li');
      // Only the iOS profile step names the authority, and it is the certificate's own subject.
      item.textContent = keys[index] === ClientAppStrings.WebClient.DeviceSetup.IosStep5
        ? text(keys[index], { authority: authoritySubject() })
        : text(keys[index]);
      list.appendChild(item);
    }
    section.appendChild(list);
    return section;
  }

  function authoritySubject(): string {
    const info = deviceSetup.info.get();
    if (info === null) return '';
    const subject = info.certificateAuthority.subject;
    return subject === null ? '' : subject;
  }

  function certificateText(info: GetDeviceSetupResponse | null, trust: CertificateTrust): string {
    if (info !== null && info.httpsEnabled && !info.certificateAuthority.available) {
      return text(ClientAppStrings.WebClient.DeviceSetup.CertificateUnavailable);
    }
    switch (trust) {
      case 'notApplicable':
        return text(ClientAppStrings.WebClient.DeviceSetup.CertificateNotApplicable);
      case 'trusted':
        return text(ClientAppStrings.WebClient.DeviceSetup.CertificateTrusted);
      case 'trustRequired':
        return text(ClientAppStrings.WebClient.DeviceSetup.CertificateRequired);
      case 'addressNotCovered':
        return text(
          ClientAppStrings.WebClient.DeviceSetup.CertificateAddressNotCovered, { address: hostname });
      case 'probeFailed':
        return text(ClientAppStrings.WebClient.DeviceSetup.CertificateProbeFailed);
      default:
        return text(ClientAppStrings.WebClient.DeviceSetup.CertificateChecking);
    }
  }

  function appText(trust: CertificateTrust): string {
    if (options.pwaInstall !== undefined && options.pwaInstall.availability() === 'runningAsApp') {
      return text(ClientAppStrings.WebClient.DeviceSetup.AppInstalled);
    }
    const blocked = trust === 'trustRequired' || trust === 'addressNotCovered' || trust === 'probeFailed';
    return blocked
      ? text(ClientAppStrings.WebClient.DeviceSetup.AppBlocked)
      : text(ClientAppStrings.WebClient.DeviceSetup.AppReady);
  }

  function show(node: HTMLElement, visible: boolean): void {
    if (visible) node.removeAttribute('hidden');
    else node.setAttribute('hidden', '');
  }

  function renderInstallControl(): void {
    while (installControl.firstChild !== null) {
      installControl.removeChild(installControl.firstChild);
    }
    const install = options.pwaInstall;
    if (install === undefined) return;

    const availability = install.availability();
    if (availability === 'promptable') {
      const button = createButton({
        label: text(ClientAppStrings.WebClient.Install.InstallAction),
        variant: 'secondary',
        onClick: () => {
          void install.promptInstall();
        },
      });
      installControl.appendChild(button.element);
      return;
    }
    if (availability === 'manualOnly') {
      const hint = element('span', 'wc-setup-hint');
      hint.textContent = text(ClientAppStrings.WebClient.Install.InstallManualIos);
      installControl.appendChild(hint);
    }
  }

  function render(): void {
    const info = deviceSetup.info.get();
    const trust = deviceSetup.certificateTrust.get();

    connectionDescription.textContent = info === null
      ? ''
      : text(ClientAppStrings.WebClient.DeviceSetup.StepConnectionDone, { instance: info.instanceName });
    certificateDescription.textContent = certificateText(info, trust);
    appDescription.textContent = appText(trust);
    renderInstallControl();

    const downloadUrl = deviceSetup.caDownloadUrl();
    show(download, downloadUrl !== null);
    if (downloadUrl !== null) download.href = downloadUrl;

    show(downloadedHint, downloaded);

    const printed = info === null ? null : info.certificateAuthority.fingerprintSha256;
    show(fingerprintBlock, printed !== null && printed !== '');
    fingerprint.textContent = printed === null ? '' : printed;

    show(openSecure, deviceSetup.httpsUrl() !== null);
  }

  const unsubscribeInfo = deviceSetup.info.subscribe(() => render());
  const unsubscribeTrust = deviceSetup.certificateTrust.subscribe(() => render());

  function close(): void {
    if (closed) return;
    closed = true;
    unsubscribeInfo();
    unsubscribeTrust();
    document.removeEventListener('keydown', onKeydown);
    if (backdrop.parentNode !== null) backdrop.parentNode.removeChild(backdrop);
    if (options.onClose !== undefined) options.onClose();
  }

  function onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') close();
  }

  closeButton.addEventListener('click', () => close());
  backdrop.addEventListener('click', event => {
    if (event.target === backdrop) close();
  });
  document.addEventListener('keydown', onKeydown);
  download.addEventListener('click', () => {
    downloaded = true;
    render();
  });

  render();
  // Re-checked as it opens, so a wizard reopened after the certificate was installed reflects that
  // rather than the state of the last check.
  void deviceSetup.recheck();

  return { element: backdrop, close: close };
}
