import { Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree, tick, updateTree } from './ui-widget-render-test-support';

const Types = UiComponents;
const Props = UiComponentProperties;

const POINTER_ID = 1;

function withBasis(basis: number): Provider[] {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function sliderNode(overrides: Record<string, unknown> = {}, events: string[] = ['adjust', 'change']): UiNode {
  return {
    id: 'slider',
    type: Types.Slider,
    properties: { [Props.Events]: events, ...overrides },
  };
}

function down(target: HTMLElement, clientX: number, clientY: number): void {
  target.dispatchEvent(new PointerEvent('pointerdown', {
    bubbles: true, cancelable: true, pointerId: POINTER_ID, clientX, clientY,
  }));
}

function move(target: HTMLElement, clientX: number, clientY: number): void {
  target.dispatchEvent(new PointerEvent('pointermove', {
    bubbles: true, cancelable: true, pointerId: POINTER_ID, clientX, clientY,
  }));
}

function up(target: HTMLElement, clientX: number, clientY: number): void {
  target.dispatchEvent(new PointerEvent('pointerup', {
    bubbles: true, cancelable: true, pointerId: POINTER_ID, clientX, clientY,
  }));
}

function surfaceOf(rendered: RenderedTree): HTMLElement {
  return el(rendered).querySelector('.widget-slider') as HTMLElement;
}

function fillOf(rendered: RenderedTree): HTMLElement {
  return el(rendered).querySelector('.widget-slider-fill') as HTMLElement;
}

describe('ui.slider interaction', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('sends a level of 0.25 for a press a quarter of the way along the track (regression: the widget ' +
    'renderer emits nothing at all without the drag wiring)', async () => {
    const rendered = await renderTree(sliderNode(), withBasis(200));
    const surface = surfaceOf(rendered);
    const rect = surface.getBoundingClientRect();

    down(surface, rect.left + 0.25 * rect.width, rect.top + rect.height / 2);
    await tick(rendered);

    expect(rendered.events.length).toBe(1);
    expect(rendered.events[0].nodeId).toBe('slider');
    expect(rendered.events[0].name).toBe('adjust');
    expect(rendered.events[0].data as number).toBeCloseTo(0.25, 3);
  });

  it('streams adjust while dragging and ends with exactly one change carrying the landed level, ' +
    'with no trailing adjust after it', async () => {
    jasmine.clock().install();
    try {
      const rendered = await renderTree(sliderNode(), withBasis(200));
      const surface = surfaceOf(rendered);
      const rect = surface.getBoundingClientRect();
      const y = rect.top + rect.height / 2;

      down(surface, rect.left + 0.1 * rect.width, y);
      await tick(rendered);

      jasmine.clock().tick(200);
      move(surface, rect.left + 0.6 * rect.width, y);
      await tick(rendered);

      up(surface, rect.left + 0.6 * rect.width, y);
      await tick(rendered);

      jasmine.clock().tick(1000);
      await tick(rendered);

      expect(rendered.events.filter(e => e.name === 'adjust').length).toBeGreaterThanOrEqual(1);
      const changes = rendered.events.filter(e => e.name === 'change');
      expect(changes.length).toBe(1);
      expect(changes[0].data as number).toBeCloseTo(0.6, 3);
      expect(rendered.events[rendered.events.length - 1].name).toBe('change');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('caps a rapid burst of moves at the throttle rate, and the level the user landed on is still ' +
    'the last one sent', async () => {
    jasmine.clock().install();
    try {
      const rendered = await renderTree(sliderNode(), withBasis(200));
      const surface = surfaceOf(rendered);
      const rect = surface.getBoundingClientRect();
      const y = rect.top + rect.height / 2;

      down(surface, rect.left + 0.05 * rect.width, y);
      await tick(rendered);
      expect(rendered.events.filter(e => e.name === 'adjust').length).toBe(1);

      for (let i = 1; i <= 20; i++) {
        move(surface, rect.left + (i / 20) * rect.width, y);
      }
      await tick(rendered);

      // Inside the 150ms window, the burst is coalesced - only the leading send has gone out so far.
      expect(rendered.events.filter(e => e.name === 'adjust').length).toBe(1);

      jasmine.clock().tick(150);
      await tick(rendered);

      const adjusts = rendered.events.filter(e => e.name === 'adjust');
      expect(adjusts.length).toBe(2);
      expect(adjusts[adjusts.length - 1].data as number).toBeCloseTo(1, 3);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('sends change on release even inside a throttle window that just suppressed an adjust - ' +
    'the release is never throttled away', async () => {
    jasmine.clock().install();
    try {
      const rendered = await renderTree(sliderNode(), withBasis(200));
      const surface = surfaceOf(rendered);
      const rect = surface.getBoundingClientRect();
      const y = rect.top + rect.height / 2;

      down(surface, rect.left + 0.01 * rect.width, y);
      await tick(rendered);

      for (let i = 1; i <= 20; i++) {
        move(surface, rect.left + (i / 20) * rect.width, y);
      }
      await tick(rendered);

      // No jasmine.clock().tick() here: the release lands inside the same throttle window as the
      // burst above, which just suppressed an adjust for this exact position.
      up(surface, rect.left + rect.width, y);
      await tick(rendered);

      const changes = rendered.events.filter(e => e.name === 'change');
      expect(changes.length).toBe(1);
      expect(changes[0].data as number).toBeCloseTo(1, 3);
      expect(rendered.events[rendered.events.length - 1].name).toBe('change');

      // The pending trailing adjust the throttle window would otherwise have flushed must have been
      // cancelled by the release, not merely delayed.
      jasmine.clock().tick(1000);
      await tick(rendered);
      expect(rendered.events.filter(e => e.name === 'change').length).toBe(1);
      expect(rendered.events[rendered.events.length - 1].name).toBe('change');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('paints its own level while dragging despite a patch reporting the old one, and adopts the ' +
    'tree level once the interaction has ended', async () => {
    const rendered = await renderTree(sliderNode({ [Props.Level]: 0.2 }), withBasis(200));
    const surface = surfaceOf(rendered);

    expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(20, 3);

    const rect = surface.getBoundingClientRect();
    const y = rect.top + rect.height / 2;
    down(surface, rect.left + 0.7 * rect.width, y);
    await tick(rendered);
    expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(70, 3);

    // The host echoes the still-stale level mid-drag; the reader must keep painting its own.
    await updateTree(rendered, { root: sliderNode({ [Props.Level]: 0.2 }) });
    expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(70, 3);

    up(surface, rect.left + 0.7 * rect.width, y);
    await tick(rendered);

    await updateTree(rendered, { root: sliderNode({ [Props.Level]: 0.9 }) });
    expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(90, 3);
  });

  it('keeps the level it landed on until the host answers, so the release does not flash the ' +
    'pre-drag level for a round trip', async () => {
    // A commit-on-release control is told nothing until the release, so its tree still carries the
    // pre-drag level at that moment. Falling back to it there is a full jump back to the start of the
    // drag and then a jump to the answer.
    const rendered = await renderTree(sliderNode({ [Props.Level]: 0.2 }, ['change']), withBasis(200));
    const surface = surfaceOf(rendered);
    const rect = surface.getBoundingClientRect();
    const y = rect.top + rect.height / 2;

    down(surface, rect.left + 0.75 * rect.width, y);
    up(surface, rect.left + 0.75 * rect.width, y);
    await tick(rendered);

    expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(75, 3);

    // Still the pre-drag level: the host has not answered yet.
    await updateTree(rendered, { root: sliderNode({ [Props.Level]: 0.2 }, ['change']) });
    expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(75, 3);

    // The answer, whatever it says - here the provider clamped the value, and the reader defers to it.
    await updateTree(rendered, { root: sliderNode({ [Props.Level]: 0.6 }, ['change']) });
    expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(60, 3);
  });

  it('gives the level back to the host when the host answers with silence, so a refused drag does not ' +
    'leave the pill parked where the user let go', async () => {
    // A refusal - the deck is locked, or the integration behind the action is unreachable - produces no
    // patch at all, so there is no answer to wait for and nothing to compare against.
    jasmine.clock().install();
    try {
      const rendered = await renderTree(sliderNode({ [Props.Level]: 0.2 }, ['change']), withBasis(200));
      const surface = surfaceOf(rendered);
      const rect = surface.getBoundingClientRect();
      const y = rect.top + rect.height / 2;

      down(surface, rect.left + 0.75 * rect.width, y);
      up(surface, rect.left + 0.75 * rect.width, y);
      await tick(rendered);
      expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(75, 3);

      jasmine.clock().tick(1000);
      await tick(rendered);

      expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(20, 3);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('is inert without any declared events: no sends, and the painted fill never leaves the host level', async () => {
    const node: UiNode = { id: 'slider', type: Types.Slider, properties: { [Props.Level]: 0.3 } };
    const rendered = await renderTree(node, withBasis(200));
    const surface = surfaceOf(rendered);

    expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(30, 3);

    const rect = surface.getBoundingClientRect();
    const x = rect.left + 0.9 * rect.width;
    const y = rect.top + rect.height / 2;
    down(surface, x, y);
    move(surface, rect.left + 0.1 * rect.width, y);
    up(surface, rect.left + 0.1 * rect.width, y);
    await tick(rendered);

    expect(rendered.events).toEqual([]);
    expect(parseFloat(fillOf(rendered).style.width)).toBeCloseTo(30, 3);
  });

  it('starts an interaction from anywhere in the box, not only on the drawn (thinner) track', async () => {
    // A 36px-thick track (0.18 of a 200px basis) centred on a 200px-tall box: pressing 60px off
    // centre on the cross axis is well outside the pill, but the whole box is the drag surface.
    const thickTree = sliderNode({ [Props.Thickness]: { basis: 0.18 } });

    const onTrack = await renderTree(thickTree, withBasis(200));
    const onTrackRect = surfaceOf(onTrack).getBoundingClientRect();
    down(surfaceOf(onTrack), onTrackRect.left + 0.5 * onTrackRect.width, onTrackRect.top + onTrackRect.height / 2);
    await tick(onTrack);

    const offTrack = await renderTree(thickTree, withBasis(200));
    const offTrackRect = surfaceOf(offTrack).getBoundingClientRect();
    down(
      surfaceOf(offTrack),
      offTrackRect.left + 0.5 * offTrackRect.width,
      offTrackRect.top + offTrackRect.height / 2 + 60,
    );
    await tick(offTrack);

    expect(onTrack.events.length).toBe(1);
    expect(offTrack.events.length).toBe(1);
    expect(offTrack.events[0].data as number).toBeCloseTo(onTrack.events[0].data as number, 5);
    expect(offTrack.events[0].data as number).toBeCloseTo(0.5, 5);
  });

  it('maps the bottom of a vertical slider\'s box to 0 and the top to 1', async () => {
    const rendered = await renderTree(sliderNode({ [Props.Direction]: 'vertical' }), withBasis(200));
    const surface = surfaceOf(rendered);
    const rect = surface.getBoundingClientRect();
    const x = rect.left + rect.width / 2;

    down(surface, x, rect.bottom);
    await tick(rendered);
    up(surface, x, rect.bottom);
    await tick(rendered);

    down(surface, x, rect.top);
    await tick(rendered);
    up(surface, x, rect.top);
    await tick(rendered);

    const adjusts = rendered.events.filter(e => e.name === 'adjust');
    expect(adjusts[0].data as number).toBeCloseTo(0, 3);
    expect(adjusts[adjusts.length - 1].data as number).toBeCloseTo(1, 3);
  });

  it('snaps a level exactly on a midpoint half up, matching the host\'s own rounding (0.25 at step 0.1 -> 0.3)', async () => {
    const rendered = await renderTree(sliderNode({ [Props.Step]: 0.1 }), withBasis(200));
    const surface = surfaceOf(rendered);
    const rect = surface.getBoundingClientRect();

    down(surface, rect.left + 0.25 * rect.width, rect.top + rect.height / 2);
    await tick(rendered);

    expect(rendered.events.length).toBe(1);
    expect(rendered.events[0].data as number).toBeCloseTo(0.3, 5);
  });

  it('sends the level as a bare number - never a wrapped object, a string, or a percentage', async () => {
    const rendered = await renderTree(sliderNode(), withBasis(200));
    const surface = surfaceOf(rendered);
    const rect = surface.getBoundingClientRect();
    const y = rect.top + rect.height / 2;

    down(surface, rect.left + 0.5 * rect.width, y);
    await tick(rendered);
    up(surface, rect.left + 0.5 * rect.width, y);
    await tick(rendered);

    expect(rendered.events.length).toBeGreaterThan(0);
    for (const event of rendered.events) {
      expect(typeof event.data).toBe('number');
    }
  });

  it('resolves identical proportional geometry at basis 240 and 120, and the accent token when levelColor is absent', async () => {
    const node = sliderNode({ [Props.Level]: 0.4, [Props.Thickness]: { basis: 0.1 } });

    // renderTree() resets the testing module on every call, which detaches the previous fixture from
    // the document - so each fixture's live layout has to be captured before the next one is rendered,
    // not read back afterwards.
    const at240 = await renderTree(node, withBasis(240));
    const track240Height = (el(at240).querySelector('.widget-slider-track') as HTMLElement).getBoundingClientRect().height;
    const fill240Width = fillOf(at240).getBoundingClientRect().width;
    const fill240Background = fillOf(at240).style.background;

    const at120 = await renderTree(node, withBasis(120));
    const track120Height = (el(at120).querySelector('.widget-slider-track') as HTMLElement).getBoundingClientRect().height;
    const fill120Width = fillOf(at120).getBoundingClientRect().width;
    const fill120Background = fillOf(at120).style.background;

    expect(track120Height).toBeCloseTo(track240Height / 2, 1);
    expect(fill120Width).toBeCloseTo(fill240Width / 2, 1);

    expect(fill240Background).toBe('var(--color-accent)');
    expect(fill120Background).toBe('var(--color-accent)');
  });

  it('paints nothing for a level of 0 - not a rounded stub one radius wide', async () => {
    const node: UiNode = { id: 'slider', type: Types.Slider, properties: {} };
    const rendered = await renderTree(node, withBasis(200));

    expect(fillOf(rendered).style.width).toBe('0%');
  });
});
