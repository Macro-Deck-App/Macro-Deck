import { dismissButton, element } from './dom';

export interface ErrorBannerOptions {
  message: string;
  dismissLabel?: string;
  onDismiss?: () => void;
}

export interface ErrorBannerHandle {
  readonly element: HTMLElement;
  setMessage(message: string): void;
  dismiss(): void;
}

export function createErrorBanner(options: ErrorBannerOptions): ErrorBannerHandle {
  const banner = element('div', 'wc-error-banner');
  banner.setAttribute('role', 'alert');

  const message = element('span', 'wc-error-banner-message');
  message.textContent = options.message;
  banner.appendChild(message);

  function dismiss(): void {
    if (banner.parentNode !== null) banner.parentNode.removeChild(banner);
    if (options.onDismiss !== undefined) options.onDismiss();
  }

  if (options.dismissLabel !== undefined) {
    const close = dismissButton(
      'wc-error-banner-dismiss', 'wc-error-banner-dismiss-icon', options.dismissLabel);
    close.addEventListener('click', () => dismiss());
    banner.appendChild(close);
  }

  return {
    element: banner,
    setMessage: (next: string) => {
      message.textContent = next;
    },
    dismiss: dismiss,
  };
}
