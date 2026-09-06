import { createSegmentedControl } from './segmented-control';

const options = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
];

const optionAt = (control: { element: HTMLElement }, index: number): HTMLElement =>
  control.element.querySelectorAll<HTMLElement>('.wc-seg-option')[index];

describe('segmented control', () => {
  it('reports the option that was picked', () => {
    const picks: string[] = [];
    const control = createSegmentedControl({ options, value: 'light', onChange: v => picks.push(v) });

    optionAt(control, 1).click();

    expect(picks).toEqual(['dark']);
    expect(control.value()).toBe('dark');
  });

  it('says nothing when the option already selected is picked again', () => {
    let changes = 0;
    const control = createSegmentedControl({ options, value: 'dark', onChange: () => changes++ });

    optionAt(control, 1).click();

    expect(changes).toBe(0);
  });

  it('marks exactly one option as the selected one', () => {
    const control = createSegmentedControl({ options, value: 'light' });

    optionAt(control, 2).click();

    const pressed = control.element.querySelectorAll('[aria-pressed="true"]');
    expect(pressed.length).toBe(1);
    expect(pressed[0].textContent).toBe('System');
  });

  it('does not report a selection the caller made itself', () => {
    let changes = 0;
    const control = createSegmentedControl({ options, value: 'light', onChange: () => changes++ });

    control.setValue('system');

    expect(changes).toBe(0);
    expect(control.value()).toBe('system');
  });

  it('keeps the selection when the options are replaced', () => {
    const control = createSegmentedControl({ options, value: 'dark' });

    control.setOptions([{ value: 'dark', label: 'Dunkel' }, { value: 'light', label: 'Hell' }]);

    expect(optionAt(control, 0).getAttribute('aria-pressed')).toBe('true');
  });
});
