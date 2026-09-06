import { element } from './dom';

export interface SettingsRowOptions {
  label: string;
  description?: string;
  control: HTMLElement;
}

export function createSettingsRow(options: SettingsRowOptions): HTMLElement {
  const row = element('div', 'wc-settings-row');

  const info = element('div', 'wc-settings-row-info');
  const label = element('span', 'wc-settings-row-label');
  label.textContent = options.label;
  info.appendChild(label);

  if (options.description !== undefined && options.description !== '') {
    const description = element('span', 'wc-settings-row-desc');
    description.textContent = options.description;
    info.appendChild(description);
  }
  row.appendChild(info);

  const control = element('div', 'wc-settings-row-control');
  control.appendChild(options.control);
  row.appendChild(control);

  return row;
}
