import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UiNode, type Variable } from '@macro-deck/runtime';
import { VariableService } from '@shared';
import { el, renderTree, tick } from './ui-render-test-support';

const fourBands = {
  bands: [
    { id: 'green', color: '#34c759' },
    { id: 'yellow', color: '#ffcc00', from: 26 },
    { id: 'orange', color: '#ff9500', from: 63 },
    { id: 'red', color: '#ff3b30', from: 90 },
  ],
};

describe('shared-ui-input thresholds', () => {
  afterEach(() => TestBed.resetTestingModule());

  function root(properties: Record<string, unknown> = {}): UiNode {
    return {
      id: 'n',
      type: 'thresholds',
      properties: { defaultValue: fourBands, min: 0, max: 100, step: 1, unit: '%', events: ['change'], ...properties },
    };
  }

  function handles(host: HTMLElement): HTMLElement[] {
    return Array.from(host.querySelectorAll<HTMLElement>('.te-handle'));
  }

  function button(host: HTMLElement, text: string): HTMLButtonElement | undefined {
    return Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find(
      candidate => candidate.textContent?.includes(text) || candidate.getAttribute('aria-label') === text,
    );
  }

  function key(target: HTMLElement, name: string): void {
    target.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));
  }

  function lastChange(events: { name: string; data?: unknown }[]): unknown {
    return events.filter(event => event.name === 'change').at(-1)?.data;
  }

  it('shows the developer defaults while no value is stored: one handle per boundary and one row per range', async () => {
    const rendered = await renderTree(root());
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(handles(host).map(handle => handle.getAttribute('aria-valuenow'))).toEqual(['26', '63', '90']);
    expect(Array.from(host.querySelectorAll('.te-range-text')).map(range => range.textContent?.trim())).toEqual([
      '0 – 26 %',
      '26 – 63 %',
      '63 – 90 %',
      '90 – 100 %',
    ]);
    expect(rendered.events).toEqual([]);
  });

  it('keeps editing bands whose colour variable is gone or that have no colour of their own', async () => {
    const primary: Variable = { id: 'p', name: 'primary', scope: 'global', type: 'color', classification: 'user', value: '#3366ff' };
    const value = {
      bands: [
        { id: 'own', color: '{{ vars.primary | color | color_darken: 20 }}' },
        { id: 'gone', color: '{{ vars.deleted | color }}', from: 30 },
        { id: 'none', color: '', from: 60 },
      ],
    };
    const rendered = await renderTree(root({ value, allowVariables: true }), null, [
      { provide: VariableService, useValue: { variables: signal([primary]) } },
    ]);
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(host.querySelectorAll('.te-range-text').length).toBe(3);
    const swatches = Array.from(host.querySelectorAll<HTMLElement>('.te-swatch'));
    expect(swatches[0].style.background).toBe('rgb(0, 61, 245)');
    expect(swatches.map(swatch => swatch.classList.contains('te-swatch-missing'))).toEqual([false, true, false]);
    const missing = Array.from(host.querySelectorAll('.te-range-missing')).map(text => text.textContent?.trim());
    expect(missing.length).toBe(1);
    expect(missing[0]).toContain('deleted');
  });

  it('moves a handle with the keyboard and stops it one step short of its neighbour', async () => {
    const rendered = await renderTree(root({ value: fourBands }));
    const host = el(rendered);

    key(handles(host)[0], 'ArrowRight');
    await tick(rendered);
    expect((lastChange(rendered.events) as typeof fourBands).bands[1].from).toBe(27);

    key(handles(host)[1], 'Home');
    await tick(rendered);
    expect((lastChange(rendered.events) as typeof fourBands).bands[2].from).toBe(28);
  });

  it('keeps a dragged outer handle on the bar however far past its end the pointer goes', async () => {
    const rendered = await renderTree(root({ value: fourBands }));
    const host = el(rendered);
    const track = host.querySelector<HTMLElement>('.te-track')!;
    const last = handles(host)[2];
    spyOn(last, 'setPointerCapture');
    const rect = track.getBoundingClientRect();
    const pointer = (type: string, clientX: number) =>
      last.dispatchEvent(new PointerEvent(type, { bubbles: true, cancelable: true, button: 0, pointerId: 1, clientX }));

    pointer('pointerdown', rect.left + rect.width * 0.9);
    for (let i = 0; i < 10; i++) {
      pointer('pointermove', rect.right + 20);
      await tick(rendered);
    }
    pointer('pointerup', rect.right + 20);
    await tick(rendered);

    expect((lastChange(rendered.events) as typeof fourBands).bands[3].from).toBe(100);
    expect(handles(host)[0].getAttribute('aria-valuemax')).toBe('100');
  });

  it('stops the keyboard at the end of the bar', async () => {
    const atEnd = { bands: [{ id: 'a', color: '#000000' }, { id: 'b', color: '#ffffff', from: 100 }] };
    const rendered = await renderTree(root({ value: atEnd }));

    key(handles(el(rendered))[0], 'PageUp');
    await tick(rendered);

    expect(rendered.events).toEqual([]);
  });

  function clickTrack(host: HTMLElement, fraction: number): void {
    const track = host.querySelector<HTMLElement>('.te-track')!;
    const rect = track.getBoundingClientRect();
    track.dispatchEvent(
      new MouseEvent('click', { bubbles: true, cancelable: true, clientX: rect.left + rect.width * fraction, clientY: rect.top }),
    );
  }

  it('adds a range where the bar is clicked and removes the selected range', async () => {
    const rendered = await renderTree(root({ value: fourBands }));
    const host = el(rendered);

    expect(button(host, 'Add range')).toBeUndefined();
    clickTrack(host, 0.4);
    await tick(rendered);
    const added = lastChange(rendered.events) as typeof fourBands;
    expect(added.bands.map(band => band.from)).toEqual([undefined, 26, 40, 63, 90]);

    button(host, 'Remove range')!.click();
    await tick(rendered);
    expect(document.activeElement?.classList.contains('te-range-select')).toBeTrue();
    expect((lastChange(rendered.events) as typeof fourBands).bands.map(band => band.id)).toEqual([
      'green',
      'yellow',
      'orange',
      'red',
    ]);
  });

  it('adds nothing when the click lands on a handle or too close to a boundary', async () => {
    const rendered = await renderTree(root({ value: fourBands }));
    const host = el(rendered);

    handles(host)[0].click();
    clickTrack(host, 0.265);
    await tick(rendered);

    expect(rendered.events).toEqual([]);
  });

  it('never turns the click that ends a handle drag into a new range, wherever the browser delivers it', async () => {
    const rendered = await renderTree(root({ value: fourBands }));
    const host = el(rendered);
    const handle = handles(host)[1];
    spyOn(handle, 'setPointerCapture');
    const rect = host.querySelector<HTMLElement>('.te-track')!.getBoundingClientRect();

    handle.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0, pointerId: 1, clientX: rect.left + rect.width * 0.63 }));
    handle.dispatchEvent(new PointerEvent('pointermove', { bubbles: true, pointerId: 1, clientX: rect.left + rect.width * 0.7 }));
    handle.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, pointerId: 1, clientX: rect.left + rect.width * 0.7 }));
    clickTrack(host, 0.75);
    await tick(rendered);

    const changes = rendered.events.filter(event => event.name === 'change');
    expect(changes.length).toBe(1);
    expect((changes[0].data as typeof fourBands).bands.length).toBe(4);

    clickTrack(host, 0.45);
    await tick(rendered);
    expect((lastChange(rendered.events) as typeof fourBands).bands.length).toBe(5);
  });

  it('adds a range with + and removes one with Delete on a focused range row', async () => {
    const rendered = await renderTree(root({ value: fourBands }));
    const host = el(rendered);

    key(host.querySelectorAll<HTMLElement>('.te-range-select')[1], '+');
    await tick(rendered);
    expect((lastChange(rendered.events) as typeof fourBands).bands.map(band => band.from)).toEqual([
      undefined,
      26,
      45,
      63,
      90,
    ]);

    key(host.querySelectorAll<HTMLElement>('.te-range-select')[2], 'Delete');
    await tick(rendered);
    expect((lastChange(rendered.events) as typeof fourBands).bands.map(band => band.from)).toEqual([undefined, 26, 63, 90]);
    expect(host.querySelector('.te-range-select')?.getAttribute('aria-keyshortcuts')).toBe('Plus Delete');
  });

  it('resets a customised value to null so the defaults apply again', async () => {
    const customised = { bands: [{ id: 'a', color: '#000000' }, { id: 'b', color: '#ffffff', from: 50 }] };
    const rendered = await renderTree(root({ value: customised, supportsReset: true }));
    const host = el(rendered);

    button(host, 'Reset to defaults')!.click();
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: null }]);
  });

  it('offers no reset while the defaults are showing', async () => {
    const rendered = await renderTree(root({ supportsReset: true }));
    expect(button(el(rendered), 'Reset to defaults')).toBeUndefined();
  });

  it('keeps the count under fixedCount and the colours under fixedColors', async () => {
    const fixedCount = await renderTree(root({ fixedCount: true }));
    clickTrack(el(fixedCount), 0.4);
    await tick(fixedCount);
    expect(fixedCount.events).toEqual([]);
    expect(button(el(fixedCount), 'Remove range')).toBeUndefined();
    TestBed.resetTestingModule();

    const fixedColors = await renderTree(root({ fixedColors: true }));
    clickTrack(el(fixedColors), 0.4);
    await tick(fixedColors);
    expect(fixedColors.events).toEqual([]);
    expect(button(el(fixedColors), 'Remove range')).toBeDefined();
    expect(el(fixedColors).querySelector('shared-color-picker')).toBeNull();
  });

  it('adds nothing at maxCount', async () => {
    const rendered = await renderTree(root({ maxCount: 4 }));
    clickTrack(el(rendered), 0.4);
    await tick(rendered);
    expect(rendered.events).toEqual([]);
  });

  it('is read-only while disabled: no edit controls and keys change nothing', async () => {
    const rendered = await renderTree(root({ disabled: true }));
    const host = el(rendered);

    expect(host.querySelector('shared-color-picker')).toBeNull();
    key(handles(host)[0], 'ArrowRight');
    await tick(rendered);
    expect(rendered.events).toEqual([]);
  });

  it('widens the bar to show a boundary stored outside min..max instead of moving it', async () => {
    const outside = { bands: [{ id: 'a', color: '#000000' }, { id: 'b', color: '#ffffff', from: 150 }] };
    const rendered = await renderTree(root({ value: outside }));
    const host = el(rendered);

    expect(handles(host)[0].getAttribute('aria-valuemax')).toBe('150');
    expect(rendered.events).toEqual([]);
  });
});
