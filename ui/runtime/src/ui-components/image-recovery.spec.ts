import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponentImageTransitions, UiComponents } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { renderUiNode, UiNodeRenderHandle } from '../render/ui-node-renderer';
import { UiRenderHost } from '../render/ui-render-host';
import { reloadFailedImages } from './image-recovery';

describe('image recovery', () => {
  let container: HTMLElement;
  let handle: UiNodeRenderHandle | null = null;

  const host: UiRenderHost = {
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: resource => (resource ? `/api/ui/resources/${resource.resourceId}` : null),
    now: () => 0,
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: () => undefined,
  };

  const iconNode = (type: string, resourceId = 'icon-1', extra: Record<string, unknown> = {}): UiNode => ({
    id: 'icon',
    type,
    properties: {
      [UiComponentProperties.Size]: { basis: 0.5 },
      [UiComponentProperties.Source]: { resourceId, contentHash: 'h1' },
      ...extra,
    },
  }) as UiNode;

  function mount(type: string, node: UiNode = iconNode(type)): HTMLImageElement {
    handle = renderUiNode(container, node, { width: 100, height: 100 }, null, 100, host);
    return container.querySelector('img') as HTMLImageElement;
  }

  function repaint(node: UiNode): HTMLImageElement {
    handle!.update(node, { width: 100, height: 100 }, null, 100);
    return container.querySelector('img') as HTMLImageElement;
  }

  function watchSource(image: HTMLImageElement): () => string[] {
    const spy = spyOn(image, 'setAttribute').and.callThrough();
    return () => spy.calls.all()
      .filter(call => call.args[0] === 'src')
      .map(call => String(call.args[1]));
  }

  beforeEach(() => {
    jasmine.clock().install();
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => {
    if (handle !== null) handle.destroy();
    handle = null;
    container.remove();
    jasmine.clock().uninstall();
  });

  for (const type of [UiComponents.Image, UiComponents.Button]) {
    it(`asks again for a ${type} icon whose request failed`, () => {
      const image = mount(type);
      const sources = watchSource(image);

      // What a deck client sees after its device wakes up: the icon URL never changes, so nothing but
      // an explicit reload can undo a single refused request.
      image.dispatchEvent(new Event('error'));
      jasmine.clock().tick(1000);

      expect(sources()).toEqual(['/api/ui/resources/icon-1']);
      expect(image.getAttribute('src')).toBe('/api/ui/resources/icon-1');
    });
  }

  it('stops asking for an icon that keeps failing', () => {
    const image = mount(UiComponents.Image);
    const sources = watchSource(image);

    for (let attempt = 0; attempt < 10; attempt++) {
      image.dispatchEvent(new Event('error'));
      jasmine.clock().tick(60000);
    }
    const attempted = sources().length;

    image.dispatchEvent(new Event('error'));
    jasmine.clock().tick(60000);

    expect(attempted).toBeGreaterThan(0);
    expect(sources().length).toBe(attempted);
  });

  it('reloads a failed icon as soon as the caller says the host is reachable again', () => {
    const image = mount(UiComponents.Image);
    image.dispatchEvent(new Event('error'));
    const sources = watchSource(image);

    expect(reloadFailedImages(container)).toBe(1);

    expect(sources()).toEqual(['/api/ui/resources/icon-1']);
  });

  it('leaves an icon that is on screen alone', () => {
    const image = mount(UiComponents.Image);
    image.dispatchEvent(new Event('error'));
    image.dispatchEvent(new Event('load'));
    const sources = watchSource(image);

    expect(reloadFailedImages(container)).toBe(0);

    expect(sources()).toEqual([]);
  });

  for (const type of [UiComponents.Image, UiComponents.Button]) {
    it(`draws nothing for a ${type} source that cannot be resolved`, () => {
      const image = mount(type, iconNode(type, 'does-not-exist'));

      image.dispatchEvent(new Event('error'));

      expect(image.style.visibility).toBe('hidden');
    });

    it(`keeps a ${type} source that cannot be resolved hidden across repaints`, () => {
      const node = iconNode(type, 'does-not-exist');
      mount(type, node).dispatchEvent(new Event('error'));

      const image = repaint(iconNode(type, 'does-not-exist'));

      expect(image.style.visibility).toBe('hidden');
    });

    it(`shows a ${type} icon again once a retry loads it`, () => {
      const image = mount(type);
      image.dispatchEvent(new Event('error'));
      jasmine.clock().tick(1000);

      image.dispatchEvent(new Event('load'));

      expect(image.style.visibility).toBe('');
    });

    it(`does not hide a new ${type} source for the failure of the previous one`, () => {
      mount(type, iconNode(type, 'does-not-exist')).dispatchEvent(new Event('error'));

      const image = repaint(iconNode(type, 'icon-2'));

      expect(image.getAttribute('src')).toBe('/api/ui/resources/icon-2');
      expect(image.style.visibility).toBe('');
    });
  }

  it('draws nothing when a crossfade lands on a source that cannot be resolved', () => {
    const preloads: HTMLImageElement[] = [];
    const RealImage = window.Image;
    spyOn(window, 'Image').and.callFake(() => {
      const preload = new RealImage();
      preloads.push(preload);
      return preload;
    });
    const crossfade = { [UiComponentProperties.Transition]: UiComponentImageTransitions.Crossfade };
    const first = mount(UiComponents.Image, iconNode(UiComponents.Image, 'icon-1', crossfade));
    first.dispatchEvent(new Event('load'));

    repaint(iconNode(UiComponents.Image, 'does-not-exist', crossfade));
    preloads[preloads.length - 1].onerror!(new Event('error'));
    const settled = container.querySelector('img') as HTMLImageElement;
    settled.dispatchEvent(new Event('error'));

    expect(settled.getAttribute('src')).toBe('/api/ui/resources/does-not-exist');
    expect(settled.style.visibility).toBe('hidden');
  });

  it('draws nothing for a tinted button source that cannot be resolved', () => {
    const hadCss = 'CSS' in window;
    const supports = () => true;
    if (hadCss) spyOn(CSS, 'supports').and.callFake(supports);
    else (window as unknown as { CSS: unknown }).CSS = { supports };
    try {
      const tinted = { [UiComponentProperties.Tint]: '#ff0000' };
      const image = mount(UiComponents.Button, iconNode(UiComponents.Button, 'does-not-exist', tinted));

      image.dispatchEvent(new Event('error'));
      repaint(iconNode(UiComponents.Button, 'does-not-exist', tinted));

      expect(image.style.visibility).toBe('hidden');
    } finally {
      if (!hadCss) delete (window as unknown as { CSS?: unknown }).CSS;
    }
  });
});
