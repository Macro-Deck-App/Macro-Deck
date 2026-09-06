import { TestBed } from '@angular/core/testing';

import { LocalizationService } from '../../localization';
import { UiNode, UiComponents, UiMacroDeckComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree, tick, updateTree } from './ui-widget-render-test-support';

const Types = UiComponents;
const MacroDeckTypes = UiMacroDeckComponents;
const Props = UiComponentProperties;

const ANCHOR = '2026-08-25T12:00:00.000Z';
const ANCHOR_MS = Date.parse(ANCHOR);

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function progressValue(positionMs: number, durationMs?: number, rate?: number): unknown {
  const marker: Record<string, unknown> = { positionMs };
  if (durationMs !== undefined) marker['durationMs'] = durationMs;
  if (rate !== undefined) marker['rate'] = rate;
  marker['anchor'] = ANCHOR;
  return { $progress: marker };
}

function barNode(id: string, properties: Record<string, unknown>): UiNode {
  return { id, type: MacroDeckTypes.ProgressBar, properties: { [Props.Thickness]: { basis: 0.05 }, ...properties } };
}

function textNode(id: string, properties: Record<string, unknown>): UiNode {
  return { id, type: MacroDeckTypes.ProgressText, properties: { [Props.Size]: { basis: 0.054 }, ...properties } };
}

function fillPercent(rendered: RenderedTree, id: string): number {
  const fill = el(rendered).querySelector(`[data-node-id="${id}"] .widget-range-bar-fill`) as HTMLElement;
  return parseFloat(fill.style.width);
}

function runText(rendered: RenderedTree, id: string): string {
  const run = el(rendered).querySelector(`[data-node-id="${id}"].widget-progress-text`) as HTMLElement;
  return run.textContent ?? '';
}

describe('macrodeck.progress-bar and macrodeck.progress-text', () => {
  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(ANCHOR_MS));
    localStorage.clear();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    TestBed.resetTestingModule();
  });

  it('carries the position forward on its own clock rather than waiting for a new tree', async () => {
    const rendered = await renderTree(
      barNode('bar', { [Props.Value]: progressValue(42_000, 215_000) }),
      withBasis(240),
    );

    expect(fillPercent(rendered, 'bar')).toBeCloseTo(19.5349, 3);

    // Thirty seconds later, with no patch and no new tree: the whole point of the reference.
    // tick() advances the mocked date as well as firing the shared clock's timer, so mockDate must
    // not also be moved - doing both would advance thirty seconds twice.
    jasmine.clock().tick(30_000);
    await tick(rendered);

    expect(fillPercent(rendered, 'bar')).toBeCloseTo(33.4884, 3);
  });

  it('holds a halted position where it is, however long passes', async () => {
    const rendered = await renderTree(
      barNode('bar', { [Props.Value]: progressValue(90_500, 215_000, 0) }),
      withBasis(240),
    );

    // tick() advances the mocked date as well as firing the shared clock's timer, so mockDate must
    // not also be moved - doing both would advance thirty seconds twice.
    jasmine.clock().tick(30_000);
    await tick(rendered);

    expect(fillPercent(rendered, 'bar')).toBeCloseTo(42.093, 3);
  });

  it('draws an empty track for a reference with no length and for no reference at all', async () => {
    const rendered = await renderTree(
      {
        id: 'root',
        type: Types.Stack,
        children: [
          barNode('stream', { [Props.Value]: progressValue(3_600_000) }),
          barNode('loading', {}),
        ],
      },
      withBasis(240),
    );

    // A fraction of an unknown whole is not a number a reader may invent.
    expect(fillPercent(rendered, 'stream')).toBe(0);
    expect(fillPercent(rendered, 'loading')).toBe(0);
  });

  it('paints the accent colour when the node carries no colours of its own', async () => {
    const rendered = await renderTree(
      {
        id: 'root',
        type: Types.Stack,
        children: [
          barNode('tinted', {
            [Props.Value]: progressValue(42_000, 215_000),
            [Props.StartColor]: '#c9a6ff',
            [Props.EndColor]: '#c9a6ff',
          }),
          barNode('plain', { [Props.Value]: progressValue(42_000, 215_000) }),
        ],
      },
      withBasis(240),
    );

    const background = (id: string) =>
      (el(rendered).querySelector(`[data-node-id="${id}"] .widget-range-bar-fill`) as HTMLElement).style.background;

    expect(background('tinted')).toContain('rgb(201, 166, 255)');
    expect(background('plain')).toContain('var(--color-accent)');
  });

  it('formats each of the three duration runs, and advances only the ones that move', async () => {
    TestBed.resetTestingModule();
    const rendered = await renderTree(
      {
        id: 'root',
        type: Types.Stack,
        children: [
          textNode('elapsed', { [Props.Value]: progressValue(42_000, 215_000), [Props.Format]: 'elapsed' }),
          textNode('duration', { [Props.Value]: progressValue(42_000, 215_000), [Props.Format]: 'duration' }),
          textNode('remaining', {
            [Props.Value]: progressValue(90_500, 215_000, 0),
            [Props.Format]: 'remaining',
          }),
          textNode('stream', { [Props.Value]: progressValue(3_600_000), [Props.Format]: 'elapsed' }),
        ],
      },
      withBasis(240),
    );
    TestBed.inject(LocalizationService).culture.set('en-US');
    await tick(rendered);

    expect(runText(rendered, 'elapsed')).toBe('0:42');
    // Hours appear only once the duration reaches one.
    expect(runText(rendered, 'duration')).toBe('3:35');
    expect(runText(rendered, 'remaining')).toBe('2:04');
    expect(runText(rendered, 'stream')).toBe('1:00:00');

    // tick() advances the mocked date as well as firing the shared clock's timer, so mockDate must
    // not also be moved - doing both would advance thirty seconds twice.
    jasmine.clock().tick(30_000);
    await tick(rendered);

    expect(runText(rendered, 'elapsed')).toBe('1:12');
    expect(runText(rendered, 'duration')).toBe('3:35');
    expect(runText(rendered, 'remaining')).toBe('2:04');
    expect(runText(rendered, 'stream')).toBe('1:00:30');
  });

  it('draws nothing at all for a format it does not know and for a missing reference', async () => {
    const rendered = await renderTree(
      {
        id: 'root',
        type: Types.Stack,
        children: [
          textNode('unknown', { [Props.Value]: progressValue(42_000, 215_000), [Props.Format]: 'sideways' }),
          textNode('valueless', { [Props.Format]: 'elapsed' }),
          textNode('lengthless', { [Props.Value]: progressValue(42_000), [Props.Format]: 'remaining' }),
        ],
      },
      withBasis(240),
    );

    expect(runText(rendered, 'unknown')).toBe('');
    expect(runText(rendered, 'valueless')).toBe('');
    // A countdown from an unknown whole would be a number this reader invented.
    expect(runText(rendered, 'lengthless')).toBe('');
  });
});

describe('artwork transition', () => {
  const CROSSFADE_PROMOTE_MS = 280;

  let created: FakeImage[];
  let realImage: typeof Image;

  class FakeImage {
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    private _src = '';

    constructor() {
      created.push(this);
    }

    get src(): string {
      return this._src;
    }

    set src(value: string) {
      this._src = value;
    }
  }

  beforeEach(() => {
    created = [];
    realImage = window.Image;
    // The preload the crossfade waits on: driven by hand so the swap is deterministic rather than
    // dependent on a headless browser actually fetching a resource URL.
    (window as unknown as { Image: unknown }).Image = FakeImage;
    jasmine.clock().install();
  });

  afterEach(() => {
    (window as unknown as { Image: unknown }).Image = realImage;
    jasmine.clock().uninstall();
    TestBed.resetTestingModule();
  });

  function imageNode(resourceId: string, transition?: string): UiNode {
    const properties: Record<string, unknown> = {
      [Props.Size]: { basis: 0.5 },
      [Props.Source]: { resourceId, contentHash: resourceId },
    };
    if (transition !== undefined) properties[Props.Transition] = transition;
    return { id: 'art', type: Types.Image, properties };
  }

  async function settleFrames(rendered: RenderedTree): Promise<void> {
    await new Promise<void>(resolve =>
      requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
    await tick(rendered);
  }

  function sources(rendered: RenderedTree): string[] {
    return [...el(rendered).querySelectorAll('[data-node-id="art"] img')].map(img =>
      (img as HTMLImageElement).getAttribute('src') ?? '');
  }

  it('replaces the artwork immediately when the node declares no transition', async () => {
    const rendered = await renderTree(imageNode('first'), withBasis(240));
    await updateTree(rendered, { root: imageNode('second') });

    expect(sources(rendered).length).toBe(1);
    expect(sources(rendered)[0]).toContain('second');
    expect(created.length).toBe(0);
  });

  it('keeps the outgoing artwork until the incoming one has decoded, then promotes it', async () => {
    const rendered = await renderTree(imageNode('first', 'crossfade'), withBasis(240));
    await updateTree(rendered, { root: imageNode('second', 'crossfade') });

    // Still only the settled layer: the incoming artwork has not decoded yet.
    expect(sources(rendered).length).toBe(1);
    expect(sources(rendered)[0]).toContain('first');
    expect(created.length).toBe(1);

    created[0].onload!();
    await tick(rendered);

    // Both layers, the incoming one fading in over the settled one.
    expect(sources(rendered).length).toBe(2);
    expect(sources(rendered)[0]).toContain('first');
    expect(sources(rendered)[1]).toContain('second');

    jasmine.clock().tick(CROSSFADE_PROMOTE_MS);
    await settleFrames(rendered);

    expect(sources(rendered).length).toBe(1);
    expect(sources(rendered)[0]).toContain('second');
  });

  it('replaces artwork that cannot be decoded at once rather than holding the previous cover', async () => {
    const rendered = await renderTree(imageNode('first', 'crossfade'), withBasis(240));
    await updateTree(rendered, { root: imageNode('broken', 'crossfade') });

    created[0].onerror!();
    await tick(rendered);

    // Holding the previous cover would attribute it to whatever the element now stands for.
    expect(sources(rendered).length).toBe(1);
    expect(sources(rendered)[0]).toContain('broken');
  });

  it('abandons a running fade when the source changes again', async () => {
    const rendered = await renderTree(imageNode('first', 'crossfade'), withBasis(240));
    await updateTree(rendered, { root: imageNode('second', 'crossfade') });
    await updateTree(rendered, { root: imageNode('third', 'crossfade') });

    // The first preload finishing must not resurrect artwork two tracks out of date.
    created[0].onload!();
    await tick(rendered);

    expect(sources(rendered).some(src => src.includes('second'))).toBeFalse();

    created[1].onload!();
    jasmine.clock().tick(CROSSFADE_PROMOTE_MS);
    await settleFrames(rendered);

    expect(sources(rendered).length).toBe(1);
    expect(sources(rendered)[0]).toContain('third');
  });
});
