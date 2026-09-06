import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponents } from '../ui-components/ui-component-types';
import { UiComponentProperties } from '../ui-components/component-properties';
import { renderUiNode, UiNodeRenderHandle } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';

describe('artwork crossfade', () => {
  const CROSSFADE_PROMOTE_MS = 280;

  class FakeImage {
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    src = '';

    constructor() {
      created.push(this);
    }
  }

  let created: FakeImage[];
  let realImage: unknown;
  let realRequestFrame: typeof requestAnimationFrame;
  let realCancelFrame: typeof cancelAnimationFrame;
  let frames: Array<() => void>;
  let container: HTMLElement;
  let handle: UiNodeRenderHandle | null = null;

  const host = (): UiRenderHost => ({
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: resource => (resource ? `/api/ui/resources/${resource.resourceId}` : null),
    now: () => Date.parse('2026-01-02T03:04:05Z'),
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: () => undefined,
  });

  function imageNode(resourceId: string, transition?: string): UiNode {
    const properties: Record<string, unknown> = {
      [UiComponentProperties.Size]: { basis: 0.5 },
      [UiComponentProperties.Source]: { resourceId, contentHash: resourceId },
    };
    if (transition !== undefined) properties[UiComponentProperties.Transition] = transition;
    return { id: 'art', type: UiComponents.Image, properties } as UiNode;
  }

  function sources(): string[] {
    return Array.prototype.slice
      .call(container.querySelectorAll('img'))
      .map((img: Element) => img.getAttribute('src') ?? '');
  }

  function settleFrames(): void {
    for (let frame = 0; frame < 2; frame++) {
      const due = frames;
      frames = [];
      for (let index = 0; index < due.length; index++) due[index]();
    }
  }

  beforeEach(() => {
    created = [];
    frames = [];
    realImage = (globalThis as { Image?: unknown }).Image;
    (globalThis as { Image?: unknown }).Image = FakeImage;
    realRequestFrame = globalThis.requestAnimationFrame;
    realCancelFrame = globalThis.cancelAnimationFrame;
    globalThis.requestAnimationFrame = ((callback: () => void) =>
      frames.push(callback)) as unknown as typeof requestAnimationFrame;
    globalThis.cancelAnimationFrame = ((token: number) => {
      frames[token - 1] = () => undefined;
    }) as unknown as typeof cancelAnimationFrame;
    container = document.createElement('div');
    document.body.appendChild(container);
    jasmine.clock().install();
  });

  afterEach(() => {
    handle?.destroy();
    handle = null;
    container.remove();
    (globalThis as { Image?: unknown }).Image = realImage;
    globalThis.requestAnimationFrame = realRequestFrame;
    globalThis.cancelAnimationFrame = realCancelFrame;
    jasmine.clock().uninstall();
  });

  const mount = (tree: UiNode) => {
    handle = renderUiNode(container, tree, { width: 240, height: 240 }, null, 240, host());
    return handle;
  };

  it('replaces the artwork at once when the node declares no transition', () => {
    mount(imageNode('first')).update(imageNode('second'), { width: 240, height: 240 }, null);

    expect(sources().length).toBe(1);
    expect(sources()[0]).toContain('second');
    // Nothing was preloaded, because nothing had to be waited for.
    expect(created.length).toBe(0);
  });

  it('keeps the outgoing artwork until the incoming one has decoded, then promotes it', () => {
    mount(imageNode('first', 'crossfade'))
      .update(imageNode('second', 'crossfade'), { width: 240, height: 240 }, null);

    // Still only the settled layer: the incoming artwork has not decoded yet.
    expect(sources().length).toBe(1);
    expect(sources()[0]).toContain('first');
    expect(created.length).toBe(1);

    created[0].onload!();

    // Both layers, the incoming one fading in over the settled one.
    expect(sources().length).toBe(2);
    expect(sources()[0]).toContain('first');
    expect(sources()[1]).toContain('second');

    jasmine.clock().tick(CROSSFADE_PROMOTE_MS);
    settleFrames();

    expect(sources().length).toBe(1);
    expect(sources()[0]).toContain('second');
  });

  it('replaces artwork that cannot be decoded at once rather than holding the previous cover', () => {
    mount(imageNode('first', 'crossfade'))
      .update(imageNode('broken', 'crossfade'), { width: 240, height: 240 }, null);

    created[0].onerror!();

    // Holding the previous cover would attribute it to whatever the element now stands for.
    expect(sources().length).toBe(1);
    expect(sources()[0]).toContain('broken');
  });

  it('abandons a running fade when the source changes again', () => {
    const mounted = mount(imageNode('first', 'crossfade'));
    mounted.update(imageNode('second', 'crossfade'), { width: 240, height: 240 }, null);
    mounted.update(imageNode('third', 'crossfade'), { width: 240, height: 240 }, null);

    // The first preload finishing must not resurrect artwork two tracks out of date.
    created[0].onload!();
    expect(sources().some(src => src.indexOf('second') >= 0)).toBeFalse();

    created[1].onload!();
    jasmine.clock().tick(CROSSFADE_PROMOTE_MS);
    settleFrames();

    expect(sources().length).toBe(1);
    expect(sources()[0]).toContain('third');
  });

  it('promotes nothing into a destroyed subtree', () => {
    const mounted = mount(imageNode('first', 'crossfade'));
    mounted.update(imageNode('second', 'crossfade'), { width: 240, height: 240 }, null);

    created[0].onload!();
    mounted.destroy();
    handle = null;

    // A promote queued behind the fade must not reach back into DOM that is already gone.
    expect(() => {
      jasmine.clock().tick(CROSSFADE_PROMOTE_MS);
    }).not.toThrow();
    settleFrames();
    expect(container.querySelectorAll('img').length).toBe(0);
  });
});
