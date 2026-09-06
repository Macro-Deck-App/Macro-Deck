import { element, setClass } from './dom';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

export interface ButtonOptions {
  label: string;
  variant?: ButtonVariant;
  disabled?: boolean;
  loading?: boolean;
  ariaLabel?: string;
  type?: 'button' | 'submit' | 'reset';
  onClick?: () => void;
}

export interface ButtonHandle {
  readonly element: HTMLButtonElement;
  setLabel(label: string): void;
  setDisabled(disabled: boolean): void;
  setLoading(loading: boolean): void;
}

export function createButton(options: ButtonOptions): ButtonHandle {
  const variant = options.variant === undefined ? 'secondary' : options.variant;
  const button = element('button', 'wc-btn wc-btn-' + variant);
  button.type = options.type === undefined ? 'button' : options.type;
  if (options.ariaLabel !== undefined) button.setAttribute('aria-label', options.ariaLabel);

  const spinner = element('span', 'wc-btn-spinner');
  spinner.setAttribute('aria-hidden', 'true');

  const label = element('span', 'wc-btn-label');
  label.textContent = options.label;
  button.appendChild(label);

  let disabled = options.disabled === true;
  let loading = options.loading === true;

  function apply(): void {
    const inert = disabled || loading;
    button.disabled = inert;
    setClass(button, 'wc-btn-loading', loading);
    if (loading && spinner.parentNode === null) button.insertBefore(spinner, label);
    else if (!loading && spinner.parentNode !== null) button.removeChild(spinner);
    // Loading is a wait, not a refusal, so it is not reported as a disabled control.
    if (loading) button.setAttribute('aria-busy', 'true');
    else button.removeAttribute('aria-busy');
  }

  button.addEventListener('click', () => {
    // The native `disabled` already swallows the press on a current engine; the guard is what makes
    // that true for a synthetic dispatch and for the engines that fire it anyway.
    if (disabled || loading || options.onClick === undefined) return;
    options.onClick();
  });

  apply();

  return {
    element: button,
    setLabel: (next: string) => {
      label.textContent = next;
    },
    setDisabled: (next: boolean) => {
      disabled = next;
      apply();
    },
    setLoading: (next: boolean) => {
      loading = next;
      apply();
    },
  };
}
