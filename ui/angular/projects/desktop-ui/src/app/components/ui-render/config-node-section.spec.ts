import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, tick } from './ui-render-test-support';

const MEMORY_KEY = 'spec.section';
const STORAGE_KEY = `macrodeck.config-section.${MEMORY_KEY}`;

function section(properties: Record<string, unknown>): UiNode {
  return {
    id: 'look',
    type: 'section',
    properties: { label: 'Appearance', ...properties },
    children: [{ id: 'look.note', type: 'prose', properties: { text: 'Inside the section' } }],
  };
}

describe('shared-ui-node section', () => {
  beforeEach(() => localStorage.removeItem(STORAGE_KEY));
  afterEach(() => {
    localStorage.removeItem(STORAGE_KEY);
    TestBed.resetTestingModule();
  });

  const toggle = (host: HTMLElement) => host.querySelector<HTMLButtonElement>('.config-chrome-section-toggle')!;
  const body = (host: HTMLElement) => host.querySelector('.config-chrome-section-body');

  it('opens the way its author declared and hides its content while collapsed', async () => {
    const open = el(await renderTree(section({ defaultExpanded: true })));
    expect(toggle(open).getAttribute('aria-expanded')).toBe('true');
    expect(body(open)?.textContent).toContain('Inside the section');

    const closed = el(await renderTree(section({ defaultExpanded: false })));
    expect(toggle(closed).getAttribute('aria-expanded')).toBe('false');
    expect(body(closed)).toBeNull();
  });

  it('remembers a collapsed section under its memory key the next time it is rendered', async () => {
    const rendered = await renderTree(section({ defaultExpanded: true, memoryKey: MEMORY_KEY }));
    toggle(el(rendered)).click();
    await tick(rendered);
    expect(body(el(rendered))).toBeNull();

    const again = el(await renderTree(section({ defaultExpanded: true, memoryKey: MEMORY_KEY })));
    expect(toggle(again).getAttribute('aria-expanded')).toBe('false');
    expect(body(again)).toBeNull();
  });

  it('forgets the state of a section that names no memory key', async () => {
    const rendered = await renderTree(section({ defaultExpanded: true }));
    toggle(el(rendered)).click();
    await tick(rendered);

    const again = el(await renderTree(section({ defaultExpanded: true })));
    expect(body(again)).not.toBeNull();
  });

  it('marks a modified section even while it is collapsed', async () => {
    const plain = el(await renderTree(section({ defaultExpanded: false })));
    expect(plain.querySelector('.config-chrome-section-dot')).toBeNull();

    const modified = el(await renderTree(section({ defaultExpanded: false, modified: true })));
    expect(modified.querySelector('.config-chrome-section-dot')).not.toBeNull();
  });

  it('offers a reset only for a modified section that declares the event, and reports it', async () => {
    const reset = (host: HTMLElement) => host.querySelector<HTMLElement>('.config-chrome-section-header shared-button');

    expect(reset(el(await renderTree(section({ modified: true }))))).toBeNull();
    expect(reset(el(await renderTree(section({ modified: false, events: ['reset'] }))))).toBeNull();

    const rendered = await renderTree(section({ modified: true, events: ['reset'] }));
    reset(el(rendered))!.click();
    await tick(rendered);

    expect(rendered.events.map(event => [event.nodeId, event.name])).toEqual([['look', 'reset']]);
  });
});
