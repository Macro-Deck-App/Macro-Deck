import { GridWidget } from '../domain/widget.interface';
import {
  setCustomPropertySupportForTesting,
  supportsCustomProperties,
  widgetRadiusFallbackCss,
} from './custom-properties';
import { UiRenderHost } from './ui-render-host';
import { renderWidgetBorder } from './widget-border';
import { renderWidgetGrid } from './widget-grid';

const host: UiRenderHost = {
  localization: { translate: (scope, key) => `${scope}:${key}` },
  resourceUrl: () => null,
  now: () => 0,
  culture: () => 'en-GB',
  simpleRendering: () => false,
  fontFamily: () => null,
  fontReady: () => true,
  emit: () => undefined,
};

const widget = (id: string): GridWidget =>
  ({ id, folderId: 'f', x: 0, y: 0, w: 1, h: 1, type: 'action-button', data: {} }) as GridWidget;

describe('custom property support', () => {
  afterEach(() => setCustomPropertySupportForTesting(null));

  it('treats an engine with no CSS.supports as having no custom properties', () => {
    // The Android 4 stock browser has neither, and jsdom happens to be the same shape, so this is
    // the one fallback condition a test can reach honestly. Answering "yes" here would hand a deck
    // the authored stylesheet and none of the fallbacks below.
    expect(typeof (globalThis as { CSS?: { supports?: unknown } }).CSS?.supports).not.toBe('function');
    expect(supportsCustomProperties()).toBe(false);
  });
});

describe('the widget radius fallback', () => {
  it('scales the radius for the elements laid out in screen pixels and not for the rest', () => {
    // The two groups exist because a tile's content resets the scale to 1: a single number would
    // draw the inner corners at the deck scale twice over.
    const css = widgetRadiusFallbackCss('[data-md-grid="g1"]', 20, 1.5);

    expect(css).toContain('[data-md-grid="g1"] .deck-grid-cell');
    expect(css).toContain('border-radius:30px');
    expect(css).toContain('[data-md-grid="g1"] .widget-button.widget-node-root');
    expect(css).toContain('border-radius:20px');
  });

  it('scopes every selector so one deck cannot restyle another on the page', () => {
    const css = widgetRadiusFallbackCss('[data-md-grid="g2"]', 20, 1);

    for (const selector of css.split('{')[0].split(',')) {
      expect(selector.trim().indexOf('[data-md-grid="g2"] ')).toBe(0);
    }
  });
});

describe('a deck rendered without custom properties', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => {
    container.remove();
    setCustomPropertySupportForTesting(null);
  });

  const mount = (borderRadius: number) => {
    const handle = renderWidgetGrid(container, { host, geometry: { cols: 2, rows: 2 }, borderRadius });
    handle.update([widget('w1')], () => undefined);
    return handle;
  };

  it('carries the radius to the elements that paint it', () => {
    setCustomPropertySupportForTesting(false);
    mount(18);

    const style = container.querySelector('.deck-grid > style');
    expect(style).not.toBeNull();
    expect(style?.textContent).toContain('.deck-grid-cell');
    expect(style?.textContent).toContain('border-radius:18px');
  });

  it('writes no rule at all where the cascade can carry the token itself', () => {
    setCustomPropertySupportForTesting(true);
    mount(18);

    expect(container.querySelector('.deck-grid > style')).toBeNull();
  });
});

describe('a border ring rendered without custom properties', () => {
  let overlay: HTMLElement;

  beforeEach(() => {
    overlay = document.createElement('div');
    document.body.appendChild(overlay);
  });

  afterEach(() => {
    overlay.remove();
    setCustomPropertySupportForTesting(null);
  });

  const ring = () => overlay.querySelector('.ring') as HTMLElement;

  it('paints its colour and width as declarations the engine can read', () => {
    setCustomPropertySupportForTesting(false);
    renderWidgetBorder(overlay).update({ style: 'static', color: '#ff0000' } as never);

    expect(ring().style.background).toBe('rgb(255, 0, 0)');
    expect(ring().style.padding).not.toBe('');
    expect(ring().style.animationDelay).not.toBe('');
  });

  it('leaves a style whose colour is drawn in a gradient unpainted', () => {
    // `.wb-comet` puts the colour in a conic gradient on a pseudo-element, which no engine on the
    // floor renders. A flat ring here would show something the modern client never shows.
    setCustomPropertySupportForTesting(false);
    renderWidgetBorder(overlay).update({ style: 'comet', color: '#ff0000' } as never);

    expect(ring().style.background).toBe('');
  });

  it('adds no inline declaration where the tokens work', () => {
    setCustomPropertySupportForTesting(true);
    renderWidgetBorder(overlay).update({ style: 'static', color: '#ff0000' } as never);

    expect(ring().style.background).toBe('');
    expect(ring().style.padding).toBe('');
  });
});
