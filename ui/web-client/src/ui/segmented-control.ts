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
  setAriaLabel(label: string): void;
}

interface SegmentedEntry {
  option: SegmentedOption;
  button: HTMLButtonElement;
  text: HTMLElement | null;
}

function optionAriaLabel(option: SegmentedOption): string {
  if (option.ariaLabel !== undefined) return option.ariaLabel;
  return option.label !== undefined ? option.label : option.value;
}

export function createSegmentedControl(options: SegmentedControlOptions): SegmentedControlHandle {
  const host = element('div', options.stretch === true ? 'wc-seg-host wc-seg-stretch' : 'wc-seg-host');
  const strip = element('div', 'wc-seg');
  strip.setAttribute('role', 'group');
  if (options.ariaLabel !== undefined) strip.setAttribute('aria-label', options.ariaLabel);
  host.appendChild(strip);

  let value: string | null = options.value === undefined ? null : options.value;
  let buttons: SegmentedEntry[] = [];

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
      button.setAttribute('aria-label', optionAriaLabel(option));

      if (option.icon !== undefined) {
        const icon = element('span', 'icon icon-' + option.icon + ' icon-sm');
        icon.setAttribute('aria-hidden', 'true');
        button.appendChild(icon);
      }
      let text: HTMLElement | null = null;
      if (option.label !== undefined) {
        text = element('span');
        text.textContent = option.label;
        button.appendChild(text);
      }

      button.addEventListener('click', () => pick(option.value));
      strip.appendChild(button);
      buttons.push({ option: option, button: button, text: text });
    }

    paint();
  }

  function sameShape(next: SegmentedOption[]): boolean {
    if (next.length !== buttons.length) return false;
    for (let index = 0; index < next.length; index++) {
      const current = buttons[index].option;
      if (current.value !== next[index].value || current.icon !== next[index].icon) return false;
      if ((current.label === undefined) !== (next[index].label === undefined)) return false;
    }
    return true;
  }

  // Only the wording changed, as on a language switch: the buttons stay, and so does focus on one.
  function relabel(next: SegmentedOption[]): void {
    for (let index = 0; index < next.length; index++) {
      const entry = buttons[index];
      entry.option = next[index];
      entry.button.setAttribute('aria-label', optionAriaLabel(next[index]));
      if (entry.text !== null) entry.text.textContent = next[index].label as string;
    }
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
    setOptions: (next: SegmentedOption[]) => {
      if (sameShape(next)) relabel(next);
      else build(next);
    },
    setAriaLabel: (label: string) => strip.setAttribute('aria-label', label),
  };
}
