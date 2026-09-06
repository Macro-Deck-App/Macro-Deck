import { ClientAppStrings, type LocalizationCatalog } from '@macro-deck/runtime';
import { createButton } from '../ui';
import { DeviceSetupService } from './device-setup';
import { DismissibleHints } from './dismissible-hints';

export const SETUP_BANNER_HINT = 'web-client.device-setup-banner';

export interface SetupBannerOptions {
  deviceSetup: DeviceSetupService;
  localization: LocalizationCatalog;
  hints: DismissibleHints;
  onOpen(): void;
  httpsPage?(): boolean;
}

export interface SetupBannerHandle {
  readonly element: HTMLElement;
  destroy(): void;
}

function translate(localization: LocalizationCatalog, qualifiedKey: string): string {
  const separator = qualifiedKey.indexOf(':');
  if (separator < 0) return '[[' + qualifiedKey + ']]';
  return localization.translate(qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1));
}

export function createSetupBanner(options: SetupBannerOptions): SetupBannerHandle {
  const text = (key: string): string => translate(options.localization, key);
  const httpsPage = options.httpsPage === undefined
    ? () => location.protocol === 'https:'
    : options.httpsPage;

  const banner = document.createElement('div');
  banner.className = 'wc-setup-banner';
  banner.setAttribute('role', 'status');

  const message = document.createElement('span');
  message.className = 'wc-setup-banner-message';
  message.textContent = text(ClientAppStrings.WebClient.DeviceSetup.BannerMessage);
  banner.appendChild(message);

  const actions = document.createElement('div');
  actions.className = 'wc-setup-banner-actions';

  const open = createButton({
    label: text(ClientAppStrings.WebClient.DeviceSetup.BannerAction),
    variant: 'secondary',
    onClick: () => options.onOpen(),
  });
  actions.appendChild(open.element);

  const dismissLabel = text(ClientAppStrings.WebClient.DeviceSetup.BannerDismiss);
  const dismiss = createButton({
    label: dismissLabel,
    ariaLabel: dismissLabel,
    variant: 'ghost',
    onClick: () => {
      options.hints.dismiss(SETUP_BANNER_HINT);
      render();
    },
  });
  actions.appendChild(dismiss.element);
  banner.appendChild(actions);

  function render(): void {
    const visible = httpsPage()
      && options.deviceSetup.certificateTrust.get() === 'trustRequired'
      && !options.hints.isDismissed(SETUP_BANNER_HINT);
    if (visible) banner.removeAttribute('hidden');
    else banner.setAttribute('hidden', '');
  }

  const unsubscribe = options.deviceSetup.certificateTrust.subscribe(() => render());
  render();

  return {
    element: banner,
    destroy: () => {
      unsubscribe();
      if (banner.parentNode !== null) banner.parentNode.removeChild(banner);
    },
  };
}
