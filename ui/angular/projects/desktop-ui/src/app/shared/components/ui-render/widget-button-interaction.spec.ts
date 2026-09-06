import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { PRESS_FEEDBACK_MIN_VISIBLE_MS } from '../../util/press-feedback';
import { RenderedTree, el, renderTree, tick, updateTree } from './ui-widget-render-test-support';

const Types = UiComponents;
const Props = UiComponentProperties;

const POINTER_ID = 1;
const LONG_PRESS_MS = 600;
const ALL_PRESS_EVENTS = ['press-start', 'long-press', 'press-end', 'press'];

function buttonNode(events: string[] | undefined, properties: Record<string, unknown> = {}): UiNode {
  const props: Record<string, unknown> = { ...properties };
  if (events !== undefined) props[Props.Events] = events;
  return { id: 'btn', type: Types.Button, properties: props };
}

function stackNode(events: string[]): UiNode {
  return { id: 'stack', type: Types.Stack, properties: { [Props.Events]: events } };
}

function down(target: HTMLElement, opts: Partial<PointerEventInit> = {}): PointerEvent {
  const evt = new PointerEvent('pointerdown', {
    bubbles: true, cancelable: true, pointerId: POINTER_ID, button: 0, ...opts,
  });
  target.dispatchEvent(evt);
  return evt;
}

function up(target: HTMLElement, opts: Partial<PointerEventInit> = {}): void {
  target.dispatchEvent(new PointerEvent('pointerup', {
    bubbles: true, cancelable: true, pointerId: POINTER_ID, button: 0, ...opts,
  }));
}

function cancel(target: HTMLElement, opts: Partial<PointerEventInit> = {}): void {
  target.dispatchEvent(new PointerEvent('pointercancel', {
    bubbles: true, cancelable: true, pointerId: POINTER_ID, ...opts,
  }));
}

function leave(target: HTMLElement, opts: Partial<PointerEventInit> = {}): void {
  target.dispatchEvent(new PointerEvent('pointerleave', {
    bubbles: true, cancelable: true, pointerId: POINTER_ID, ...opts,
  }));
}

function surfaceOf(rendered: RenderedTree, selector = '.widget-button'): HTMLElement {
  return el(rendered).querySelector(selector) as HTMLElement;
}

function tintOf(rendered: RenderedTree, selector = '.widget-button'): HTMLElement | null {
  return el(rendered).querySelector(`${selector} .widget-press-tint`);
}

function names(rendered: RenderedTree): string[] {
  return rendered.events.map(e => e.name);
}

describe('ui.button gesture', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('emits exactly press-start, press-end, press in order for a tap under the long-press threshold', async () => {
    jasmine.clock().install();
    try {
      const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
      const surface = surfaceOf(rendered);

      down(surface);
      await tick(rendered);
      jasmine.clock().tick(LONG_PRESS_MS - 1);
      up(surface);
      await tick(rendered);

      expect(names(rendered)).toEqual(['press-start', 'press-end', 'press']);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('emits exactly press-start, long-press, press-end (no press) for a hold at the 600ms threshold', async () => {
    jasmine.clock().install();
    try {
      const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
      const surface = surfaceOf(rendered);

      down(surface);
      await tick(rendered);
      jasmine.clock().tick(LONG_PRESS_MS);
      await tick(rendered);
      up(surface);
      await tick(rendered);

      expect(names(rendered)).toEqual(['press-start', 'long-press', 'press-end']);
      expect(names(rendered)).not.toContain('press');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('pointercancel emits exactly press-start, press-end - no press, no long-press even after 600ms', async () => {
    jasmine.clock().install();
    try {
      const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
      const surface = surfaceOf(rendered);

      down(surface);
      await tick(rendered);
      cancel(surface);
      await tick(rendered);
      jasmine.clock().tick(LONG_PRESS_MS);
      await tick(rendered);

      expect(names(rendered)).toEqual(['press-start', 'press-end']);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('pointerleave emits exactly press-start, press-end - no press, no long-press even after 600ms', async () => {
    jasmine.clock().install();
    try {
      const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
      const surface = surfaceOf(rendered);

      down(surface);
      await tick(rendered);
      leave(surface);
      await tick(rendered);
      jasmine.clock().tick(LONG_PRESS_MS);
      await tick(rendered);

      expect(names(rendered)).toEqual(['press-start', 'press-end']);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('a node declaring only press-start/press-end emits exactly those two for a tap, never press', async () => {
    const rendered = await renderTree(buttonNode(['press-start', 'press-end']));
    const surface = surfaceOf(rendered);

    down(surface);
    up(surface);
    await tick(rendered);

    expect(names(rendered)).toEqual(['press-start', 'press-end']);
  });

  it('a node declaring only press emits exactly press, with no start/end', async () => {
    const rendered = await renderTree(buttonNode(['press']));
    const surface = surfaceOf(rendered);

    down(surface);
    up(surface);
    await tick(rendered);

    expect(names(rendered)).toEqual(['press']);
  });

  it('a node declaring only long-press emits long-press for a 700ms hold and nothing for a 100ms tap', async () => {
    jasmine.clock().install();
    try {
      const rendered = await renderTree(buttonNode(['long-press']));
      const surface = surfaceOf(rendered);

      down(surface);
      await tick(rendered);
      jasmine.clock().tick(700);
      await tick(rendered);
      up(surface);
      await tick(rendered);

      expect(names(rendered)).toEqual(['long-press']);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('a node declaring only long-press emits nothing for a 100ms tap', async () => {
    jasmine.clock().install();
    try {
      const rendered = await renderTree(buttonNode(['long-press']));
      const surface = surfaceOf(rendered);

      down(surface);
      await tick(rendered);
      jasmine.clock().tick(100);
      up(surface);
      await tick(rendered);

      expect(names(rendered)).toEqual([]);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('a node with no events key emits nothing, does not preventDefault or stopPropagation, and paints no tint', async () => {
    const rendered = await renderTree(buttonNode(undefined));
    const surface = surfaceOf(rendered);

    const evt = down(surface);
    up(surface);
    await tick(rendered);

    expect(names(rendered)).toEqual([]);
    expect(evt.defaultPrevented).toBeFalse();
    expect(tintOf(rendered)?.classList.contains('widget-press-tint-active')).toBeFalsy();
  });

  it('"events": [] behaves identically to no events key: emits nothing', async () => {
    const rendered = await renderTree(buttonNode([]));
    const surface = surfaceOf(rendered);

    down(surface);
    up(surface);
    await tick(rendered);

    expect(names(rendered)).toEqual([]);
  });

  it('completes the press when the pointer moves outside the box mid-press and is released there', async () => {
    const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
    const surface = surfaceOf(rendered);

    down(surface);
    surface.dispatchEvent(new PointerEvent('pointermove', {
      bubbles: true, cancelable: true, pointerId: POINTER_ID, clientX: 5000, clientY: 5000,
    }));
    up(surface);
    await tick(rendered);

    expect(names(rendered)).toEqual(['press-start', 'press-end', 'press']);
  });

  it('ignores a second pointer and a right-click while a press is already in progress', async () => {
    const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
    const surface = surfaceOf(rendered);

    down(surface, { pointerId: POINTER_ID });
    down(surface, { pointerId: POINTER_ID + 1 });
    down(surface, { pointerId: POINTER_ID + 2, button: 2 });
    up(surface, { pointerId: POINTER_ID });
    await tick(rendered);

    expect(names(rendered)).toEqual(['press-start', 'press-end', 'press']);
  });

  it('a right-click alone never starts a press', async () => {
    const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
    const surface = surfaceOf(rendered);

    down(surface, { button: 2 });
    up(surface, { button: 2 });
    await tick(rendered);

    expect(names(rendered)).toEqual([]);
  });

  it('paints the press tint synchronously on pointerdown with no round trip required', async () => {
    const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
    const surface = surfaceOf(rendered);

    down(surface);
    await tick(rendered);

    expect(tintOf(rendered)?.classList.contains('widget-press-tint-active')).toBeTrue();
  });

  it('keeps the press tint while a repaint arrives under the finger', async () => {
    // A press is a pointer state, not a model state. Any tree holding a clock or a playing track is
    // repainted about once a second, and a producer commonly patches its widget in response to
    // press-start - so a repaint mid-press is the ordinary case, not an edge one.
    const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
    const surface = surfaceOf(rendered);

    down(surface);
    await tick(rendered);
    expect(tintOf(rendered)?.classList.contains('widget-press-tint-active')).toBeTrue();

    await updateTree(rendered, { root: buttonNode(ALL_PRESS_EVENTS, { [Props.Background]: '#123456' }) });

    expect(tintOf(rendered)?.classList.contains('widget-press-tint-active')).toBeTrue();
  });

  it('holds the press tint for at least the minimum visible duration on a very short tap', async () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date());
    try {
      const rendered = await renderTree(buttonNode(ALL_PRESS_EVENTS));
      const surface = surfaceOf(rendered);

      down(surface);
      await tick(rendered);
      jasmine.clock().tick(5);
      up(surface);
      await tick(rendered);

      jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS - 5 - 1);
      await tick(rendered);
      expect(tintOf(rendered)?.classList.contains('widget-press-tint-active')).toBeTrue();

      jasmine.clock().tick(2);
      await tick(rendered);
      expect(tintOf(rendered)?.classList.contains('widget-press-tint-active')).toBeFalse();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('a ui.stack node carrying press events paints the same tint and emits press - feedback comes ' +
    'from declared events, not the node type', async () => {
    const rendered = await renderTree(stackNode(['press']));
    const surface = surfaceOf(rendered, '.widget-stack');

    down(surface);
    await tick(rendered);
    expect(tintOf(rendered, '.widget-stack')?.classList.contains('widget-press-tint-active')).toBeTrue();

    up(surface);
    await tick(rendered);

    expect(names(rendered)).toEqual(['press']);
  });
});
