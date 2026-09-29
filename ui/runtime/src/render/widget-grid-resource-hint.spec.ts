import { GridWidget } from '../domain/widget.interface';
import { UiNode } from '../ui-framework/ui-node.interface';
import { UiResource, UiResourceHint } from '../ui-framework/ui-resource';
import { UiRenderHost } from './ui-render-host';
import { renderWidgetGrid } from './widget-grid';

const icon: UiResource = { resourceId: 'app.macro-deck.widget-icon.icon-pack.a', contentHash: 'h' };

const widget = (id: string, x: number, w = 1, h = 1): GridWidget =>
  ({ id, folderId: 'f', x, y: 0, w, h, type: 'action-button', data: {} }) as GridWidget;

const button = (zoom?: number): UiNode =>
  ({ id: 'root', type: 'ui.button', properties: { source: icon, ...(zoom ? { zoom } : {}) } }) as UiNode;

const image = (zoom?: number): UiNode =>
  ({ id: 'img', type: 'ui.image', properties: { source: icon, size: { basis: 0.5 }, ...(zoom ? { zoom } : {}) } }) as UiNode;

describe('runtime widget grid resource hints', () => {
  let container: HTMLElement;
  let hints: { [widgetId: string]: UiResourceHint | undefined };

  const host: UiRenderHost = {
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: (_resource, hint) => {
      if (hint?.widgetId !== undefined) hints[hint.widgetId] = hint;
      return null;
    },
    now: () => 0,
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: () => undefined,
  };

  beforeEach(() => {
    hints = {};
    container = document.createElement('div');
    Object.defineProperty(container, 'clientWidth', { value: 800, configurable: true });
    Object.defineProperty(container, 'clientHeight', { value: 480, configurable: true });
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  function paint(widgets: GridWidget[], trees: { [id: string]: UiNode }): void {
    const handle = renderWidgetGrid(container, { host, geometry: { cols: 5, rows: 3 } });
    handle.update(widgets, id => trees[id]);
  }

  it('tells the host which widget an icon belongs to and how large it is painted', () => {
    paint([widget('small', 0), widget('large', 1, 2, 2)], { small: button(), large: button() });

    const small = hints['small']!.displayPx!;
    const large = hints['large']!.displayPx!;
    expect(small).toBeGreaterThan(0);
    expect(large).toBeGreaterThan(small * 1.5);
  });

  it('counts the icon zoom into the painted size', () => {
    paint([widget('plain', 0), widget('zoomed', 1)], { plain: button(), zoomed: button(2) });

    expect(hints['zoomed']!.displayPx).toBeCloseTo(hints['plain']!.displayPx! * 2, 5);
  });

  it('sizes an image by its own edge, not the whole tile', () => {
    paint([widget('tile', 0), widget('image', 1), widget('zoomed', 2)], { tile: button(), image: image(), zoomed: image(2) });

    expect(hints['image']!.displayPx).toBeCloseTo(hints['tile']!.displayPx! / 2, 5);
    expect(hints['zoomed']!.displayPx).toBeCloseTo(hints['image']!.displayPx! * 2, 5);
  });
});
