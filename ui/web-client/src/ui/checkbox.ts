import { element } from './dom';

export interface CheckboxOptions {
  label?: string;
  ariaLabel?: string;
  checked?: boolean;
  disabled?: boolean;
  onChange?: (checked: boolean) => void;
}

export interface CheckboxHandle {
  readonly element: HTMLElement;
  checked(): boolean;
  setChecked(checked: boolean): void;
  setDisabled(disabled: boolean): void;
}

export function createCheckbox(options: CheckboxOptions): CheckboxHandle {
  const wrap = element('label', 'wc-checkbox');

  const box = element('input', 'wc-checkbox-box');
  box.type = 'checkbox';
  box.checked = options.checked === true;
  box.disabled = options.disabled === true;
  if (options.ariaLabel !== undefined) box.setAttribute('aria-label', options.ariaLabel);
  wrap.appendChild(box);

  if (options.label !== undefined && options.label !== '') {
    const text = element('span', 'wc-checkbox-label');
    text.textContent = options.label;
    wrap.appendChild(text);
  }

  box.addEventListener('change', () => {
    if (options.onChange !== undefined) options.onChange(box.checked);
  });

  return {
    element: wrap,
    checked: () => box.checked,
    setChecked: (checked: boolean) => {
      box.checked = checked;
    },
    setDisabled: (disabled: boolean) => {
      box.disabled = disabled;
    },
  };
}
