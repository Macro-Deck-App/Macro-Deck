import { element, setClass } from './dom';

export type InputType = 'text' | 'password';

export interface InputOptions {
  type?: InputType;
  value?: string;
  placeholder?: string;
  ariaLabel?: string;
  autofocus?: boolean;
  disabled?: boolean;
  invalid?: boolean;
  name?: string;
  onChange?: (value: string) => void;
  onSubmit?: () => void;
}

export interface InputHandle {
  readonly element: HTMLInputElement;
  value(): string;
  setValue(value: string): void;
  setDisabled(disabled: boolean): void;
  setInvalid(invalid: boolean): void;
  focus(): void;
}

export function createInput(options: InputOptions): InputHandle {
  const input = element('input', 'wc-input');
  input.type = options.type === undefined ? 'text' : options.type;
  input.value = options.value === undefined ? '' : options.value;
  if (options.placeholder !== undefined) input.placeholder = options.placeholder;
  if (options.ariaLabel !== undefined) input.setAttribute('aria-label', options.ariaLabel);
  if (options.name !== undefined) input.name = options.name;
  input.disabled = options.disabled === true;

  function setInvalid(invalid: boolean): void {
    setClass(input, 'wc-input-invalid', invalid);
    if (invalid) input.setAttribute('aria-invalid', 'true');
    else input.removeAttribute('aria-invalid');
  }

  setInvalid(options.invalid === true);

  input.addEventListener('input', () => {
    if (options.onChange !== undefined) options.onChange(input.value);
  });

  if (options.onSubmit !== undefined) {
    input.addEventListener('keydown', event => {
      const key = (event as KeyboardEvent).key;
      if (key !== 'Enter' || input.disabled) return;
      event.preventDefault();
      (options.onSubmit as () => void)();
    });
  }

  // Autofocus is applied by hand rather than through the attribute: the attribute only acts while
  // the document is parsing, and this element is created long after that.
  if (options.autofocus === true) {
    // The element is focused once it is in a document; a detached one cannot take focus at all.
    setTimeout(() => input.focus(), 0);
  }

  return {
    element: input,
    value: () => input.value,
    setValue: (value: string) => {
      input.value = value;
    },
    setDisabled: (disabled: boolean) => {
      input.disabled = disabled;
    },
    setInvalid,
    focus: () => input.focus(),
  };
}
