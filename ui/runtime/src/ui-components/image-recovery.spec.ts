import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponents } from './ui-component-types';
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

  const iconNode = (type: string): UiNode => ({
    id: 'icon',
    type,
    properties: {
      [UiComponentProperties.Size]: { basis: 0.5 },
      [UiComponentProperties.Source]: { resourceId: 'icon-1', contentHash: 'h1' },
    },
  }) as UiNode;

  function mount(type: string): HTMLImageElement {
    handle = renderUiNode(container, iconNode(type), { width: 100, height: 100 }, null, 100, host);
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
});
