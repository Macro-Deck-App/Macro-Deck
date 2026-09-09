import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, tick } from './ui-render-test-support';

// Copied verbatim, in declaration order, from the two .NET source files - not imported from the
// renderer, so this test fails if the renderer's own vocabulary constant ever drifts from them.
const VOCABULARY = [
  'string', 'number', 'boolean', 'choice', 'password', 'secret', 'dynamic-choice', 'autocomplete',
  'multiselect', 'color', 'file', 'folder', 'hotkey', 'duration', 'datetime', 'json', 'code', 'keyvalue',
  'object', 'array', 'ipaddress', 'url', 'icon', 'image', 'keyboard-sequence', 'keyboard-combo',
  'widget-target', 'actions-list-editor', 'action-picker', 'variable-picker', 'device-picker',
  'integration-picker', 'icon-display', 'state-mapping-editor', 'flow', 'step', 'stack',
  'tabs', 'tab', 'heading', 'prose', 'instructions', 'instruction', 'copy-value', 'link',
  'advanced-section', 'divider', 'banner', 'validation-message', 'busy', 'widget-configuration',
  'widget-properties', 'widget-editor',
];

describe('shared-ui-node every configuration primitive', () => {
  afterEach(() => TestBed.resetTestingModule());

  // Structured-value controls: a bare string "value" (fine for every other type) is the wrong shape
  // for these, so they are left without one and rendered with their own type's defaults instead.
  const STRUCTURED_VALUE_TYPES = new Set(['hotkey', 'keyboard-combo', 'keyboard-sequence']);

  for (const type of VOCABULARY) {
    it(`renders "${type}" as a supported component`, async () => {
      const root: UiNode = {
        id: 'n',
        type,
        properties: STRUCTURED_VALUE_TYPES.has(type)
          ? { label: 'L', text: 'T', url: 'https://example.com' }
          : { label: 'L', text: 'T', value: 'V', url: 'https://example.com' },
      };
      const rendered = await renderTree(root);
      const host = el(rendered);

      expect(host.querySelector('.config-node-unsupported')).toBeNull();
      expect(host.firstElementChild).not.toBeNull();
      expect(host.firstElementChild?.children.length ?? 0).toBeGreaterThan(0);
    });
  }

  it('renders a textarea for a multiline literal-only string and no single-line input', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'string',
      properties: { multiline: true, literalOnly: true },
    });
    const host = el(rendered);

    expect(host.querySelector('textarea')).not.toBeNull();
    expect(host.querySelector('input[type="text"]')).toBeNull();
  });

  it('renders a single-line input for a plain literal-only string', async () => {
    const rendered = await renderTree({ id: 'n', type: 'string', properties: { literalOnly: true } });
    const host = el(rendered);

    expect(host.querySelector('input')).not.toBeNull();
    expect(host.querySelector('textarea')).toBeNull();
  });

  it('pairs a slider with the number box when showSlider and min/max are both present', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'number',
      properties: { showSlider: true, min: 0, max: 10, step: 2 },
    });
    const host = el(rendered);

    const slider = host.querySelector('input[type="range"]');
    const numberBox = host.querySelector('input[type="number"]');
    expect(slider).not.toBeNull();
    expect(numberBox).not.toBeNull();
    expect(slider?.getAttribute('min')).toBe('0');
    expect(numberBox?.getAttribute('min')).toBe('0');
    expect(slider?.getAttribute('max')).toBe('10');
    expect(numberBox?.getAttribute('max')).toBe('10');
    expect(slider?.getAttribute('step')).toBe('2');
    expect(numberBox?.getAttribute('step')).toBe('2');
  });

  it('renders no slider for a plain number', async () => {
    const rendered = await renderTree({ id: 'n', type: 'number', properties: {} });
    const host = el(rendered);

    expect(host.querySelector('input[type="number"]')).not.toBeNull();
    expect(host.querySelector('input[type="range"]')).toBeNull();
  });

  it('draws a segmented boolean as a segmented control with its two captions, and emits true/false', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'boolean',
      properties: {
        segmented: true,
        value: false,
        events: ['change'],
        falseLabel: 'Single state',
        trueLabel: 'Multi state',
      },
    });
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(host.querySelector('shared-toggle-switch')).toBeNull();
    expect(host.querySelector('shared-segmented-control')).not.toBeNull();

    const options = Array.from(host.querySelectorAll<HTMLElement>('.seg-option'));
    expect(options.map(o => o.textContent?.trim())).toEqual(['Single state', 'Multi state']);
    expect(options.find(o => o.textContent?.trim() === 'Single state')?.getAttribute('aria-pressed')).toBe('true');

    options.find(o => o.textContent?.trim() === 'Multi state')!.click();
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: true }]);
  });

  it('renders a plain boolean with no segmented property as a toggle switch, unchanged', async () => {
    const rendered = await renderTree({ id: 'n', type: 'boolean', properties: { value: true } });
    const host = el(rendered);

    expect(host.querySelector('shared-segmented-control')).toBeNull();
    expect(host.querySelector('shared-toggle-switch')).not.toBeNull();
  });

  it('draws a cards choice as one tile per option, each with its own description, and picks on click', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'choice',
      properties: {
        cards: true,
        value: 'small',
        events: ['change'],
        options: [
          { value: 'small', label: 'Small cover', description: 'Cover with details below' },
          { value: 'full', label: 'Full cover', description: 'Artwork fills, text overlaid' },
        ],
      },
    });
    const host = el(rendered);

    expect(host.querySelector('shared-select')).toBeNull();
    expect(host.querySelector('shared-segmented-control')).toBeNull();

    const cards = Array.from(host.querySelectorAll<HTMLElement>('.config-choice-card'));
    expect(cards.map(c => c.querySelector('.config-choice-card-name')?.textContent?.trim()))
      .toEqual(['Small cover', 'Full cover']);
    expect(cards.map(c => c.querySelector('.config-choice-card-desc')?.textContent?.trim()))
      .toEqual(['Cover with details below', 'Artwork fills, text overlaid']);

    cards[1].click();
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 'full' }]);
  });

  // Weather's forecast length is the case: seven values that each read as a phrase, so the field is a
  // list to pick from - while the value it stores stays the number the schema declares.
  it('draws a number carrying options as a select, and still emits a number', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'number',
      properties: {
        value: 5,
        events: ['change'],
        options: [{ value: '1', label: '1 day' }, { value: '5', label: '5 days' }],
      },
    });
    const host = el(rendered);

    expect(host.querySelector('input[type="number"]')).toBeNull();
    host.querySelector<HTMLButtonElement>('.control')!.click();
    await tick(rendered);

    const options = Array.from(host.querySelectorAll<HTMLElement>('.sel-option'));
    expect(options.map(o => o.textContent?.trim())).toEqual(['1 day', '5 days']);

    options[0].click();
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 1 }]);
  });

  it('emits a number from the plain number box and keeps showing what was typed', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'number',
      properties: { value: 14, min: 1, max: 100, events: ['change'] },
    });
    const box = el(rendered).querySelector<HTMLInputElement>('input[type="number"]')!;

    box.value = '20';
    box.dispatchEvent(new Event('input'));
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 20 }]);
    expect(box.value).toBe('20');
  });

  it('emits a number from the number box paired with a slider', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'number',
      properties: { value: 5, min: 0, max: 10, showSlider: true, events: ['change'] },
    });
    const box = el(rendered).querySelector<HTMLInputElement>('input[type="number"]')!;

    box.value = '7';
    box.dispatchEvent(new Event('input'));
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 7 }]);
  });

  it('steps the number box from the value it shows (issue #698)', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'number',
      properties: { value: 14, min: 1, max: 100, events: ['change'] },
    });
    const box = el(rendered).querySelector<HTMLInputElement>('input[type="number"]')!;

    box.stepUp();
    box.dispatchEvent(new Event('input'));
    await tick(rendered);
    expect(box.value).toBe('15');

    box.stepUp();
    box.dispatchEvent(new Event('input'));
    await tick(rendered);
    expect(box.value).toBe('16');

    expect(rendered.events).toEqual([
      { nodeId: 'n', name: 'change', data: 15 },
      { nodeId: 'n', name: 'change', data: 16 },
    ]);
  });

  it('leaves a half-typed decimal alone while the model renormalises it', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'number',
      properties: { value: 1, events: ['change'] },
    });
    const box = el(rendered).querySelector<HTMLInputElement>('input[type="number"]')!;

    box.value = '1.10';
    box.dispatchEvent(new Event('input'));
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 1.1 }]);
    expect(box.value).toBe('1.10');
  });

  it('sends nothing for an emptied number box and shows the kept value again on blur', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'number',
      properties: { value: 14, min: 1, max: 100, events: ['change'] },
    });
    const box = el(rendered).querySelector<HTMLInputElement>('input[type="number"]')!;

    box.value = '';
    box.dispatchEvent(new Event('input'));
    await tick(rendered);
    expect(rendered.events).toEqual([]);

    box.dispatchEvent(new Event('blur'));
    await tick(rendered);
    expect(box.value).toBe('14');
  });

  // The Action Button's state list is the case: a user opening it wants to see which state the button
  // is on right now, which is a fact about the option, not part of its name.
  it('marks a choice option carrying a badge, and leaves the others unmarked', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'choice',
      properties: {
        value: 'a',
        options: [
          { value: 'a', label: 'State 1' },
          { value: 'b', label: 'State 2', badge: 'live' },
        ],
      },
    });
    const host = el(rendered);

    host.querySelector<HTMLButtonElement>('.sel-trigger, .control')!.click();
    await tick(rendered);

    const options = Array.from(host.querySelectorAll<HTMLElement>('.sel-option'));
    expect(options.map(o => o.querySelector('.sel-option-label')?.textContent?.trim()))
      .toEqual(['State 1', 'State 2']);
    expect(options.map(o => o.querySelector('.sel-option-badge')?.textContent?.trim()))
      .toEqual([undefined, 'live']);
  });

  it('renders choice options with a label fallback to value, dropping unknown metadata', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'choice',
      properties: {
        value: 'gamma',
        options: [
          { value: 'alpha', label: 'Alpha' },
          { value: 'beta' },
          { value: 'gamma', metadata: { icon: 'x', someFutureKey: 'y' } },
        ],
      },
    });
    const host = el(rendered);

    (host.querySelector('button.control') as HTMLButtonElement)?.click();
    await tick(rendered);

    const options = Array.from(host.querySelectorAll('.sel-option')).map(o => o.textContent?.trim());
    expect(options).toEqual(['Alpha', 'beta', 'gamma']);
    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(host.innerHTML).not.toContain('someFutureKey');
    expect(host.innerHTML).not.toContain('>y<');
  });

  it('draws a segmented choice as a segmented control instead of a select, and emits the chosen option', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'choice',
      properties: {
        segmented: true,
        value: 'single',
        events: ['change'],
        options: [
          { value: 'single', label: 'Single state' },
          { value: 'multi', label: 'Multi state' },
        ],
      },
    });
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(host.querySelector('shared-select')).toBeNull();
    expect(host.querySelector('shared-segmented-control')).not.toBeNull();

    const options = Array.from(host.querySelectorAll<HTMLElement>('.seg-option'));
    expect(options.map(o => o.textContent?.trim())).toEqual(['Single state', 'Multi state']);
    expect(options.find(o => o.textContent?.trim() === 'Single state')?.getAttribute('aria-pressed')).toBe('true');

    options.find(o => o.textContent?.trim() === 'Multi state')!.click();
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 'multi' }]);
  });

  it('draws a plain choice with no segmented property as a select, unchanged', async () => {
    const rendered = await renderTree({
      id: 'n',
      type: 'choice',
      properties: { value: 'a', options: [{ value: 'a', label: 'A' }] },
    });
    const host = el(rendered);

    expect(host.querySelector('shared-segmented-control')).toBeNull();
    expect(host.querySelector('shared-select')).not.toBeNull();
  });
});
