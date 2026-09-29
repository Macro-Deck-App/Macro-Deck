import {
  renderWidgetGrid,
  uiResourceUrl,
  type GridWidget,
  type UiNode,
  type UiRenderHost,
  type UiResource,
} from '@macro-deck/runtime';
import { IconResolutionStore } from './icon-resolution';

const STORAGE_KEY = 'macro-deck.icon-resolution';

const icon: UiResource = { resourceId: 'app.macro-deck.widget-icon.icon-pack.a', contentHash: 'h' };
const artwork: UiResource = { resourceId: 'app.macro-deck.music-player.artwork', contentHash: 'h' };
const other: UiResource = { resourceId: 'app.macro-deck.widget-icon.icon-pack.b', contentHash: 'h' };

describe('icon resolution', () => {
  beforeEach(() => window.localStorage.removeItem(STORAGE_KEY));
  afterEach(() => window.localStorage.removeItem(STORAGE_KEY));

  it('starts on automatic and remembers a choice across reloads', () => {
    expect(new IconResolutionStore().get()).toBe('auto');

    new IconResolutionStore().set(512);

    expect(new IconResolutionStore().get()).toBe(512);
  });

  it('falls back to automatic for a stored value it does not know', () => {
    window.localStorage.setItem(STORAGE_KEY, '300');

    expect(new IconResolutionStore().get()).toBe('auto');
  });

  it('still works when storage refuses to be read', () => {
    spyOn(Object.getPrototypeOf(window.localStorage) as Storage, 'getItem').and.throwError('denied');
    spyOn(Object.getPrototypeOf(window.localStorage) as Storage, 'setItem').and.throwError('denied');

    const store = new IconResolutionStore();
    store.set(128);

    expect(store.get()).toBe(128);
  });

  it('asks for a fixed resolution whatever the icon is painted at', () => {
    const store = new IconResolutionStore();
    store.set(128);

    expect(store.sizeFor(icon, { displayPx: 900, widgetId: 'w' })).toBe(128);
    expect(store.sizeFor(icon)).toBe(128);
  });

  it('never sizes a resource that is not an icon', () => {
    const store = new IconResolutionStore();
    expect(store.sizeFor(artwork, { displayPx: 90, widgetId: 'w' })).toBeUndefined();

    store.set(512);
    expect(store.sizeFor(artwork, { displayPx: 90, widgetId: 'w' })).toBeUndefined();
  });

  it('picks the automatic resolution from the painted size in css pixels', () => {
    const store = new IconResolutionStore();

    expect(store.sizeFor(icon, { displayPx: 96, widgetId: 'phone' })).toBe(128);
    expect(store.sizeFor(icon, { displayPx: 200, widgetId: 'tablet' })).toBe(256);
    expect(store.sizeFor(icon, { displayPx: 400, widgetId: 'wall' })).toBe(512);
  });

  it('leaves the host default in place when it does not know the painted size', () => {
    const store = new IconResolutionStore();

    expect(store.sizeFor(icon)).toBeUndefined();
    expect(store.sizeFor(icon, { displayPx: 0, widgetId: 'w' })).toBeUndefined();
  });

  it('never steps an icon down again when the deck shrinks', () => {
    const store = new IconResolutionStore();

    expect(store.sizeFor(icon, { displayPx: 200, widgetId: 'w' })).toBe(256);
    expect(store.sizeFor(icon, { displayPx: 120, widgetId: 'w' })).toBe(256);
    expect(store.sizeFor(other, { displayPx: 120, widgetId: 'w' })).toBe(128);
  });

  it('reports the automatic resolutions used by the widgets on screen', () => {
    const store = new IconResolutionStore();
    store.sizeFor(icon, { displayPx: 100, widgetId: 'a' });
    store.sizeFor(icon, { displayPx: 200, widgetId: 'b' });
    store.sizeFor(icon, { displayPx: 400, widgetId: 'elsewhere' });

    expect(store.observedRange(['a'])).toEqual({ min: 128, max: 128 });
    expect(store.observedRange(['a', 'b'])).toEqual({ min: 128, max: 256 });
    expect(store.observedRange(['nothing'])).toBeNull();
  });

  describe('on a deck', () => {
    let container: HTMLElement;

    beforeEach(() => {
      container = document.createElement('div');
      Object.defineProperty(container, 'clientWidth', { value: 800, configurable: true });
      Object.defineProperty(container, 'clientHeight', { value: 480, configurable: true });
      document.body.appendChild(container);
    });

    afterEach(() => container.remove());

    it('makes the painted icons ask for the newly chosen resolution', () => {
      const store = new IconResolutionStore();
      const urls: string[] = [];
      const host: UiRenderHost = {
        localization: { translate: (scope, key) => `${scope}:${key}` },
        resourceUrl: (resource, hint) => {
          const url = uiResourceUrl('http://host', resource, store.sizeFor(resource, hint));
          if (url !== null) urls.push(url);
          return url;
        },
        now: () => 0,
        culture: () => 'en-GB',
        simpleRendering: () => false,
        fontFamily: () => null,
        fontReady: () => true,
        emit: () => undefined,
      };
      const widgets = [{ id: 'w', folderId: 'f', x: 0, y: 0, w: 1, h: 1, type: 'action-button', data: {} } as GridWidget];
      const tree = { id: 'root', type: 'ui.button', properties: { source: icon } } as UiNode;
      const grid = renderWidgetGrid(container, { host, geometry: { cols: 5, rows: 3 } });

      grid.update(widgets, () => tree);
      const automatic = urls[urls.length - 1];
      store.set(512);
      grid.update(widgets, () => tree);

      expect(automatic).toMatch(/size=(128|256)$/);
      expect(urls[urls.length - 1]).toBe('http://host/api/ui/resources/app.macro-deck.widget-icon.icon-pack.a?v=h&size=512');
    });
  });
});
