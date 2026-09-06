import { element, setClass } from './dom';

export interface ToggleOptions {
  label?: string;
  ariaLabel?: string;
  checked?: boolean;
  disabled?: boolean;
  busy?: boolean;
  onChange?: (checked: boolean) => void;
}

export interface ToggleHandle {
  readonly element: HTMLElement;
  checked(): boolean;
  setChecked(checked: boolean): void;
  setDisabled(disabled: boolean): void;
  setBusy(busy: boolean): void;
}

export function createToggle(options: ToggleOptions): ToggleHandle {
  const wrap = element('label', 'wc-toggle');

  const input = element('input', 'wc-toggle-input');
  input.type = 'checkbox';
  if (options.ariaLabel !== undefined) input.setAttribute('aria-label', options.ariaLabel);
  wrap.appendChild(input);

  const track = element('span', 'wc-toggle-track');
  const thumb = element('span', 'wc-toggle-thumb');
  const spinner = element('span', 'wc-toggle-spinner');
  spinner.setAttribute('aria-hidden', 'true');
  track.appendChild(thumb);
  wrap.appendChild(track);

  if (options.label !== undefined && options.label !== '') {
    const text = element('span', 'wc-toggle-label');
    text.textContent = options.label;
    wrap.appendChild(text);
  }

  let checked = options.checked === true;
  let disabled = options.disabled === true;
  let busy = options.busy === true;

  function apply(): void {
    input.checked = checked;
    input.disabled = disabled;
    setClass(wrap, 'wc-toggle-checked', checked);
    setClass(wrap, 'wc-toggle-disabled', disabled);
    setClass(wrap, 'wc-toggle-busy', busy);
    if (busy) {
      input.setAttribute('aria-busy', 'true');
      if (spinner.parentNode === null) thumb.appendChild(spinner);
    } else {
      input.removeAttribute('aria-busy');
      if (spinner.parentNode !== null) thumb.removeChild(spinner);
    }
  }

  input.addEventListener('change', () => {
    if (disabled || busy) {
      // The native checkbox already flipped; snap it back so it stays in sync with the state that
      // was ignored.
      input.checked = checked;
      return;
    }
    checked = input.checked;
    apply();
    if (options.onChange !== undefined) options.onChange(checked);
  });

  apply();

  return {
    element: wrap,
    checked: () => checked,
    setChecked: (next: boolean) => {
      checked = next;
      apply();
    },
    setDisabled: (next: boolean) => {
      disabled = next;
      apply();
    },
    setBusy: (next: boolean) => {
      busy = next;
      apply();
    },
  };
}
