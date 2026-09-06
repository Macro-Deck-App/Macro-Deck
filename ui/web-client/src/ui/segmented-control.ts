import { scrollActiveIntoView } from '@macro-deck/runtime';
import { element, setClass } from './dom';

export interface SegmentedOption {
  value: string;
  label?: string;
  icon?: string;
  ariaLabel?: string;
}

export interface SegmentedControlOptions {
  options: SegmentedOption[];
  value?: string | null;
  ariaLabel?: string;
  stretch?: boolean;
  onChange?: (value: string) => void;
}

export interface SegmentedControlHandle {
  readonly element: HTMLElement;
  value(): string | null;
  setValue(value: string | null): void;
  setOptions(options: SegmentedOption[]): void;
}

export function createSegmentedControl(options: SegmentedControlOptions): SegmentedControlHandle {
  const host = element('div', options.stretch === true ? 'wc-seg-host wc-seg-stretch' : 'wc-seg-host');
  const strip = element('div', 'wc-seg');
  strip.setAttribute('role', 'group');
  if (options.ariaLabel !== undefined) strip.setAttribute('aria-label', options.ariaLabel);
  host.appendChild(strip);

  let value: string | null = options.value === undefined ? null : options.value;
  let buttons: Array<{ option: SegmentedOption; button: HTMLButtonElement }> = [];

  function paint(): void {
    for (let index = 0; index < buttons.length; index++) {
      const entry = buttons[index];
      const active = entry.option.value === value;
      setClass(entry.button, 'wc-seg-active', active);
      entry.button.setAttribute('aria-pressed', active ? 'true' : 'false');
      if (active) scrollActiveIntoView(strip, entry.button);
    }
  }

  function build(next: SegmentedOption[]): void {
    while (strip.firstChild !== null) strip.removeChild(strip.firstChild);
    buttons = [];

    for (let index = 0; index < next.length; index++) {
      const option = next[index];
      const button = element('button', 'wc-seg-option');
      button.type = 'button';
      button.setAttribute(
        'aria-label',
        option.ariaLabel !== undefined ? option.ariaLabel
          : option.label !== undefined ? option.label
            : option.value,
      );

      if (option.icon !== undefined) {
        const icon = element('span', 'icon icon-' + option.icon + ' icon-sm');
        icon.setAttribute('aria-hidden', 'true');
        button.appendChild(icon);
      }
      if (option.label !== undefined) {
        const text = element('span');
        text.textContent = option.label;
        button.appendChild(text);
      }

      button.addEventListener('click', () => pick(option.value));
      strip.appendChild(button);
      buttons.push({ option: option, button: button });
    }

    paint();
  }

  function pick(next: string): void {
    // Picking what is already picked is not a change, and reporting it would make a caller that
    // reloads on every change reload on a press that chose nothing.
    if (next === value) return;
    value = next;
    paint();
    if (options.onChange !== undefined) options.onChange(next);
  }

  build(options.options);

  return {
    element: host,
    value: () => value,
    setValue: (next: string | null) => {
      value = next;
      paint();
    },
    setOptions: (next: SegmentedOption[]) => build(next),
  };
}
