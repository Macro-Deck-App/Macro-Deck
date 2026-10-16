import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponents } from '../ui-components/ui-component-types';
import { UiComponentProperties } from '../ui-components/component-properties';
import { renderUiNode, UiNodeRenderHandle } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';

describe('artwork crossfade', () => {
  const CROSSFADE_PROMOTE_MS = 280;
  const DECODE_GRACE_MS = 100;

  class FakeImage {
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    complete = false;
    naturalWidth = 0;
    decode?: () => Promise<void>;
    decoding: { resolve: () => void; reject: (reason: unknown) => void } | null = null;
    private source = '';

    constructor() {
      created.push(this);
      if (withDecode) {
        this.decode = () => new Promise<void>((resolve, reject) => {
          this.decoding = { resolve, reject };
        });
      }
    }

    get src(): string {
      return this.source;
    }

    set src(value: string) {
      this.source = value;
      const cached = cachedSources.indexOf(value) >= 0;
      this.complete = cached;
      this.naturalWidth = cached ? 64 : 0;
    }

    load(): void {
      this.complete = true;
      this.naturalWidth = 64;
      if (this.onload) this.onload();
    }

    fail(): void {
      this.complete = true;
      this.naturalWidth = 0;
      if (this.onerror) this.onerror();
    }
  }

  let created: FakeImage[];
  let cachedSources: string[];
  let withDecode: boolean;
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

  function imageNode(resourceId: string, transition?: string, extra: Record<string, unknown> = {}): UiNode {
    const properties: Record<string, unknown> = {
      [UiComponentProperties.Size]: { basis: 0.5 },
      [UiComponentProperties.Source]: { resourceId, contentHash: resourceId },
      ...extra,
    };
    if (transition !== undefined) properties[UiComponentProperties.Transition] = transition;
    return { id: 'art', type: UiComponents.Image, properties } as UiNode;
  }

  function buttonNode(resourceId: string, zoom: number): UiNode {
    return {
      id: 'button',
      type: UiComponents.Button,
      properties: {
        [UiComponentProperties.Source]: { resourceId, contentHash: resourceId },
        [UiComponentProperties.Zoom]: zoom,
      },
    } as UiNode;
  }

  function images(): HTMLImageElement[] {
    return Array.prototype.slice.call(container.querySelectorAll('img'));
  }

  function sources(): string[] {
    return images().map(img => img.getAttribute('src') ?? '');
  }

  function showBitmaps(): void {
    for (const img of images()) {
      Object.defineProperty(img, 'complete', { configurable: true, get: () => true });
      Object.defineProperty(img, 'naturalWidth', { configurable: true, get: () => 64 });
    }
  }

  function settleFrames(): void {
    for (let frame = 0; frame < 2; frame++) {
      const due = frames;
      frames = [];
      for (let index = 0; index < due.length; index++) due[index]();
    }
  }

  async function flushPromises(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) await Promise.resolve();
  }

  beforeEach(() => {
    created = [];
    cachedSources = [];
    withDecode = false;
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

  const box = { width: 240, height: 240 };

  describe('without a transition', () => {
    it('replaces artwork that is not on screen yet at once, with nothing preloaded', () => {
      mount(imageNode('first')).update(imageNode('second'), box, null);

      expect(sources()).toEqual(['/api/ui/resources/second']);
      expect(created.length).toBe(0);
    });

    it('keeps the outgoing button artwork at its own zoom until the incoming one has loaded', () => {
      const mounted = mount(buttonNode('open', 0.9));
      showBitmaps();

      mounted.update(buttonNode('ror-on', 1), box, null);

      expect(sources()).toEqual(['/api/ui/resources/open']);
      expect(images()[0].style.transform).toContain('scale(0.9)');

      created[0].load();

      expect(sources()).toEqual(['/api/ui/resources/ror-on']);
      expect(images()[0].style.transform).toContain('scale(1)');
    });

    it('keeps the outgoing image at its own framing until the incoming one has loaded', () => {
      const mounted = mount(imageNode('first', undefined, {
        [UiComponentProperties.Zoom]: 0.5,
        [UiComponentProperties.Opacity]: 0.4,
      }));
      showBitmaps();

      mounted.update(imageNode('second'), box, null);

      expect(images()[0].style.transform).toContain('scale(0.5)');
      expect(images()[0].style.opacity).toBe('0.4');

      created[0].load();

      expect(sources()).toEqual(['/api/ui/resources/second']);
      expect(images()[0].style.transform).toContain('scale(1)');
      expect(images()[0].style.opacity).toBe('1');
    });

    it('applies a new zoom of the same artwork at once', () => {
      const mounted = mount(buttonNode('open', 0.9));
      showBitmaps();

      mounted.update(buttonNode('open', 1.2), box, null);

      expect(images()[0].style.transform).toContain('scale(1.2)');
    });

    it('swaps to artwork the browser already holds in the same update', () => {
      cachedSources = ['/api/ui/resources/ror-on'];
      const mounted = mount(buttonNode('open', 0.9));
      showBitmaps();

      mounted.update(buttonNode('ror-on', 1), box, null);

      expect(sources()).toEqual(['/api/ui/resources/ror-on']);
      expect(images()[0].style.transform).toContain('scale(1)');
    });

    it('replaces the outgoing artwork at once when the incoming one cannot be loaded', () => {
      const mounted = mount(buttonNode('open', 0.9));
      showBitmaps();

      mounted.update(buttonNode('broken', 1), box, null);
      created[0].fail();

      expect(sources()).toEqual(['/api/ui/resources/broken']);
    });

    it('shows the latest framing of artwork that changed again while it loaded', () => {
      const mounted = mount(buttonNode('open', 0.9));
      showBitmaps();

      mounted.update(buttonNode('ror-on', 1), box, null);
      mounted.update(buttonNode('ror-on', 1.3), box, null);

      expect(images()[0].style.transform).toContain('scale(0.9)');

      created[0].load();

      expect(sources()).toEqual(['/api/ui/resources/ror-on']);
      expect(images()[0].style.transform).toContain('scale(1.3)');
    });

    it('stays on the outgoing artwork when the source returns to it before the incoming one loaded', () => {
      const mounted = mount(buttonNode('open', 0.9));
      showBitmaps();

      mounted.update(buttonNode('ror-on', 1), box, null);
      mounted.update(buttonNode('open', 0.9), box, null);

      expect(created[0].src).toBe('');
      created[0].load();

      expect(sources()).toEqual(['/api/ui/resources/open']);
      expect(images()[0].style.transform).toContain('scale(0.9)');
    });

    it('paints nothing into a destroyed button once a pending load finishes', () => {
      const mounted = mount(buttonNode('open', 0.9));
      showBitmaps();
      mounted.update(buttonNode('ror-on', 1), box, null);

      mounted.destroy();
      handle = null;

      expect(() => created[0].load()).not.toThrow();
      expect(container.querySelectorAll('img').length).toBe(0);
    });

    describe('with decode', () => {
      beforeEach(() => {
        withDecode = true;
      });

      it('swaps once the incoming artwork has decoded', async () => {
        const mounted = mount(buttonNode('open', 0.9));
        showBitmaps();
        mounted.update(buttonNode('ror-on', 1), box, null);

        created[0].load();
        expect(sources()).toEqual(['/api/ui/resources/open']);

        created[0].decoding!.resolve();
        await flushPromises();

        expect(sources()).toEqual(['/api/ui/resources/ror-on']);
        expect(images()[0].style.transform).toContain('scale(1)');
      });

      it('swaps after a short grace when the decode never settles', () => {
        const mounted = mount(buttonNode('open', 0.9));
        showBitmaps();
        mounted.update(buttonNode('ror-on', 1), box, null);

        created[0].load();
        expect(sources()).toEqual(['/api/ui/resources/open']);

        jasmine.clock().tick(DECODE_GRACE_MS);
        expect(sources()).toEqual(['/api/ui/resources/ror-on']);
      });

      it('swaps on load without waiting for a decode while the page is hidden', () => {
        spyOnProperty(document, 'visibilityState', 'get').and.returnValue('hidden');
        const mounted = mount(buttonNode('open', 0.9));
        showBitmaps();
        mounted.update(buttonNode('ror-on', 1), box, null);

        created[0].load();

        expect(sources()).toEqual(['/api/ui/resources/ror-on']);
      });

      it('replaces the outgoing artwork at once when the incoming one fails to decode', async () => {
        const mounted = mount(buttonNode('open', 0.9));
        showBitmaps();
        mounted.update(buttonNode('broken', 1), box, null);

        created[0].decoding!.reject(new Error('EncodingError'));
        await flushPromises();

        expect(sources()).toEqual(['/api/ui/resources/broken']);
      });

      it('ignores the decode failure of a download it abandoned', async () => {
        const mounted = mount(buttonNode('open', 0.9));
        showBitmaps();
        mounted.update(buttonNode('ror-on', 1), box, null);
        mounted.update(buttonNode('open', 0.9), box, null);

        created[0].decoding!.reject(new Error('EncodingError'));
        await flushPromises();

        expect(sources()).toEqual(['/api/ui/resources/open']);
        expect(images()[0].style.transform).toContain('scale(0.9)');
      });
    });
  });

  it('keeps the outgoing artwork until the incoming one has decoded, then promotes it', () => {
    mount(imageNode('first', 'crossfade'))
      .update(imageNode('second', 'crossfade'), box, null);

    // Still only the settled layer: the incoming artwork has not decoded yet.
    expect(sources().length).toBe(1);
    expect(sources()[0]).toContain('first');
    expect(created.length).toBe(1);

    created[0].load();

    // Both layers, the incoming one fading in over the settled one.
    expect(sources().length).toBe(2);
    expect(sources()[0]).toContain('first');
    expect(sources()[1]).toContain('second');

    const incoming = images()[1];
    jasmine.clock().tick(CROSSFADE_PROMOTE_MS);
    settleFrames();

    expect(sources().length).toBe(1);
    expect(sources()[0]).toContain('second');
    expect(images()[0]).toBe(incoming);
  });

  it('hands the faded-in element over as the settled one, so an animated picture is not started over', () => {
    const mounted = mount(imageNode('first', 'crossfade', { [UiComponentProperties.Opacity]: 0.7 }));
    mounted.update(imageNode('second', 'crossfade', { [UiComponentProperties.Opacity]: 0.7 }), box, null);
    created[0].load();
    const incoming = images()[1];

    jasmine.clock().tick(CROSSFADE_PROMOTE_MS);
    settleFrames();
    mounted.update(imageNode('second', 'crossfade', { [UiComponentProperties.Opacity]: 0.7 }), box, null);

    expect(images()).toEqual([incoming]);
    expect(incoming.getAttribute('src')).toBe('/api/ui/resources/second');
    expect(incoming.className).toBe('');
    expect(incoming.getAttribute('aria-hidden')).toBeNull();
    expect(incoming.style.opacity).toBe('0.7');
    expect(incoming.style.getPropertyValue('--widget-artwork-opacity')).toBe('');
  });

  it('hands a button\'s faded-in artwork over as its settled artwork and fades the next one in above it', () => {
    const mounted = mount({ ...buttonNode('first', 1), properties: { ...buttonNode('first', 1).properties, [UiComponentProperties.Transition]: 'crossfade' } } as UiNode);
    const crossfading = (resourceId: string) =>
      ({ ...buttonNode(resourceId, 1), properties: { ...buttonNode(resourceId, 1).properties, [UiComponentProperties.Transition]: 'crossfade' } }) as UiNode;
    mounted.update(crossfading('second'), box, null);
    created[0].load();
    const incoming = images()[1];

    jasmine.clock().tick(CROSSFADE_PROMOTE_MS);
    settleFrames();

    expect(images()).toEqual([incoming]);
    expect(incoming.className).toBe('widget-button-artwork');

    showBitmaps();
    mounted.update(crossfading('third'), box, null);
    created[1].load();
    expect(images()[0]).toBe(incoming);
    expect(images()[1].className).toContain('widget-artwork-incoming');
  });

  it('eases the outgoing layer with the current brightness while it fades out', () => {
    const mounted = mount(imageNode('first', 'crossfade'));
    mounted.update(imageNode('second', 'crossfade', { [UiComponentProperties.Brightness]: 0.6 }), box, null);
    created[0].load();

    expect(images()[0].getAttribute('src')).toContain('first');
    expect(images()[0].style.filter).toContain('brightness(0.6)');
  });

  it('replaces artwork that cannot be decoded at once rather than holding the previous cover', () => {
    mount(imageNode('first', 'crossfade'))
      .update(imageNode('broken', 'crossfade'), box, null);

    created[0].fail();

    // Holding the previous cover would attribute it to whatever the element now stands for.
    expect(sources().length).toBe(1);
    expect(sources()[0]).toContain('broken');
  });

  it('abandons a running fade when the source changes again', () => {
    const mounted = mount(imageNode('first', 'crossfade'));
    mounted.update(imageNode('second', 'crossfade'), box, null);
    mounted.update(imageNode('third', 'crossfade'), box, null);

    // The first preload finishing must not resurrect artwork two tracks out of date.
    expect(created[0].src).toBe('');
    created[0].load();
    expect(sources().some(src => src.indexOf('second') >= 0)).toBeFalse();

    created[1].load();
    jasmine.clock().tick(CROSSFADE_PROMOTE_MS);
    settleFrames();

    expect(sources().length).toBe(1);
    expect(sources()[0]).toContain('third');
  });

  it('promotes nothing into a destroyed subtree', () => {
    const mounted = mount(imageNode('first', 'crossfade'));
    mounted.update(imageNode('second', 'crossfade'), box, null);

    created[0].load();
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
