import { element } from './dom';

export interface SettingsSectionOptions {
  heading: string;
  description?: string;
  action?: HTMLElement;
  rows?: HTMLElement[];
}

export interface SettingsSectionHandle {
  readonly element: HTMLElement;
  readonly body: HTMLElement;
}

export function createSettingsSection(options: SettingsSectionOptions): SettingsSectionHandle {
  const section = element('section', 'wc-settings-section');

  const header = element('header', 'wc-settings-section-header');
  const headingRow = element('div', 'wc-settings-section-heading-row');
  const heading = element('h2', 'wc-settings-section-title');
  heading.textContent = options.heading;
  headingRow.appendChild(heading);
  if (options.action !== undefined) headingRow.appendChild(options.action);
  header.appendChild(headingRow);

  if (options.description !== undefined && options.description !== '') {
    const description = element('p', 'wc-settings-section-desc');
    description.textContent = options.description;
    header.appendChild(description);
  }
  section.appendChild(header);

  const body = element('div', 'wc-settings-section-body');
  const rows = options.rows === undefined ? [] : options.rows;
  for (let index = 0; index < rows.length; index++) body.appendChild(rows[index]);
  section.appendChild(body);

  return { element: section, body: body };
}
