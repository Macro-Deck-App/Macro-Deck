import { element, setClass } from './dom';

export interface OtpInputOptions {
  length: number;
  ariaLabel: string;
  name?: string;
  describedBy?: string;
  autofocus?: boolean;
  normalize?: (raw: string) => string;
  onComplete?: (value: string) => void;
}

export interface OtpInputHandle {
  readonly element: HTMLElement;
  value(): string;
  setValue(value: string): void;
  setDisabled(disabled: boolean): void;
  setInvalid(invalid: boolean): void;
  focus(): void;
}

export function createOtpInput(options: OtpInputOptions): OtpInputHandle {
  const host = element('div', 'wc-otp');
  const cells = element('div', 'wc-otp-cells');
  cells.setAttribute('aria-hidden', 'true');
  host.appendChild(cells);

  const cellElements: HTMLElement[] = [];
  for (let index = 0; index < options.length; index++) {
    const cell = element('span', 'wc-otp-cell');
    cells.appendChild(cell);
    cellElements.push(cell);
  }

  // One real input over the boxes, so paste, SMS code autofill and the numeric keypad all work
  // the way they do for any text field; pattern is what makes older iOS show the keypad.
  const input = element('input', 'wc-otp-input');
  input.type = 'text';
  input.setAttribute('inputmode', 'numeric');
  input.setAttribute('pattern', '[0-9]*');
  input.setAttribute('autocomplete', 'one-time-code');
  input.setAttribute('aria-label', options.ariaLabel);
  if (options.name !== undefined) input.name = options.name;
  if (options.describedBy !== undefined) input.setAttribute('aria-describedby', options.describedBy);
  host.appendChild(input);

  let focused = false;

  function clean(raw: string): string {
    const normalized = options.normalize === undefined ? raw : options.normalize(raw);
    return normalized.replace(/[^0-9]/g, '').slice(0, options.length);
  }

  function paint(): void {
    const value = input.value;
    const active = Math.min(value.length, options.length - 1);
    for (let index = 0; index < cellElements.length; index++) {
      cellElements[index].textContent = value.charAt(index);
      setClass(cellElements[index], 'wc-otp-cell-filled', index < value.length);
      setClass(cellElements[index], 'wc-otp-cell-active', focused && index === active);
    }
  }

  function setInvalid(invalid: boolean): void {
    setClass(host, 'wc-otp-invalid', invalid);
    if (invalid) input.setAttribute('aria-invalid', 'true');
    else input.removeAttribute('aria-invalid');
  }

  input.addEventListener('input', () => {
    const value = clean(input.value);
    if (value !== input.value) input.value = value;
    setInvalid(false);
    paint();
    if (value.length === options.length && options.onComplete !== undefined) options.onComplete(value);
  });
  input.addEventListener('focus', () => {
    focused = true;
    paint();
  });
  input.addEventListener('blur', () => {
    focused = false;
    paint();
  });

  if (options.autofocus === true) {
    // The element is focused once it is in a document; a detached one cannot take focus at all.
    setTimeout(() => input.focus(), 0);
  }

  paint();

  return {
    element: host,
    value: () => input.value,
    setValue: (value: string) => {
      input.value = clean(value);
      paint();
    },
    setDisabled: (disabled: boolean) => {
      input.disabled = disabled;
      setClass(host, 'wc-otp-disabled', disabled);
    },
    setInvalid,
    focus: () => input.focus(),
  };
}
