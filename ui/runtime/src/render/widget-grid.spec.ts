import { GridWidget } from '../domain/widget.interface';
import { UiNode } from '../ui-framework/ui-node.interface';
import { PRESS_FEEDBACK_MIN_VISIBLE_MS } from './press-feedback';
import { UiRenderHost } from './ui-render-host';
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

const widget = (id: string, x: number, y: number, w = 1, h = 1): GridWidget =>
  ({ id, folderId: 'f', x, y, w, h, type: 'action-button', data: {} }) as GridWidget;

const tree = (text: string): UiNode =>
  ({ id: `t-${text}`, type: 'ui.text', properties: { text } }) as UiNode;

const pressable = (): UiNode =>
  ({ id: 'root', type: 'ui.button', properties: { events: ['press'] } }) as UiNode;

function pointer(type: string, pointerId = 1): Event {
  const event = new Event(type) as Event & { clientX: number; clientY: number; pointerId: number; button: number };
  event.clientX = 0;
  event.clientY = 0;
  event.pointerId = pointerId;
  event.button = 0;
  return event;
}

function press(element: HTMLElement): void {
  element.dispatchEvent(pointer('pointerdown'));
  element.dispatchEvent(pointer('pointerup'));
}

describe('runtime widget grid', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    // jsdom reports zero for clientWidth/Height, so the box is stated outright.
    Object.defineProperty(container, 'clientWidth', { value: 800, configurable: true });
    Object.defineProperty(container, 'clientHeight', { value: 480, configurable: true });
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  const mount = (geometry = { cols: 5, rows: 3 }) =>
    renderWidgetGrid(container, { host, geometry });

  it('paints the folder background and gives it back to the theme when the folder has none', () => {
    const handle = renderWidgetGrid(container, { host, geometry: { cols: 5, rows: 3 }, background: '#101820' });
    const surface = container.querySelector('.deck-grid') as HTMLElement;

    expect(surface.style.background).toBe('rgb(16, 24, 32)');

    // Empty rather than a literal fallback colour: an inline background would outrank the sheet's,
    // and with it every theme switch.
    handle.setBackground(null);
    expect(surface.style.background).toBe('');
  });

  it('rings only the focused widget, and moves the ring rather than redrawing the deck', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0), widget('b', 1, 0)], () => undefined);
    const tileA = container.querySelector('[data-widget-id="a"]') as HTMLElement;

    handle.setFocusedWidget('a');
    expect(container.querySelectorAll('.deck-grid-tile-focused').length).toBe(1);
    expect(tileA.classList.contains('deck-grid-tile-focused')).toBeTrue();

    handle.setFocusedWidget('b');
    expect(container.querySelectorAll('.deck-grid-tile-focused').length).toBe(1);
    expect(tileA.classList.contains('deck-grid-tile-focused')).toBeFalse();

    // Every client without hardware controls passes null, and must render exactly as before.
    handle.setFocusedWidget(null);
    expect(container.querySelectorAll('.deck-grid-tile-focused').length).toBe(0);
  });

  it('keeps the focus ring on the right widget when the deck is redrawn', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0), widget('b', 1, 0)], () => undefined);
    handle.setFocusedWidget('b');

    handle.update([widget('a', 0, 0), widget('b', 1, 0), widget('c', 2, 0)], () => undefined);

    expect(container.querySelectorAll('.deck-grid-tile-focused').length).toBe(1);
  });

  it('answers a long press itself rather than letting the browser open its context menu', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => undefined);
    const surface = container.querySelector('.deck-grid-tile-surface') as HTMLElement;

    const event = new Event('contextmenu', { cancelable: true, bubbles: true });
    surface.dispatchEvent(event);

    expect(event.defaultPrevented).toBeTrue();
    handle.destroy();
  });

  it('draws one cell per empty position and none under a widget', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => undefined);

    expect(container.querySelectorAll('.deck-grid-cell').length).toBe(14);
    expect(container.querySelectorAll('.deck-grid-tile').length).toBe(1);
  });

  it('leaves no cell under a widget that spans several positions', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0, 2, 2)], () => undefined);

    expect(container.querySelectorAll('.deck-grid-cell').length).toBe(15 - 4);
  });

  it('positions every cell and tile absolutely rather than by grid tracks', () => {
    const handle = mount();
    handle.update([widget('a', 1, 1)], () => undefined);

    const surface = container.querySelector('.deck-grid') as HTMLElement;
    expect(surface.style.display).not.toBe('grid');

    const tile = container.querySelector('.deck-grid-tile') as HTMLElement;
    expect(tile.style.left.endsWith('px')).toBeTrue();
    expect(tile.style.top.endsWith('px')).toBeTrue();
    expect(parseFloat(tile.style.left)).toBeGreaterThan(0);
    expect(parseFloat(tile.style.top)).toBeGreaterThan(0);
  });

  it('gives a wider widget a proportionally wider tile', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0, 1, 1), widget('b', 1, 0, 2, 1)], () => undefined);

    const tiles = Array.from(container.querySelectorAll('.deck-grid-tile')) as HTMLElement[];
    const single = parseFloat(tiles[0].style.width);
    const double = parseFloat(tiles[1].style.width);

    // Two cells plus the gap they straddle, so more than twice one cell.
    expect(double).toBeGreaterThan(single * 2);
  });

  it('lays content out in reference space and scales the wrapper', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => undefined);

    const tile = container.querySelector('.deck-grid-tile') as HTMLElement;
    const content = container.querySelector('.deck-grid-tile-content') as HTMLElement;

    expect(content.style.transform).toMatch(/^scale\(/);
    // The wrapper carries the scale, so descendants must not apply it a second time.
    expect(content.style.getPropertyValue('--widget-scale')).toBe('1');

    const scale = parseFloat(content.style.transform.replace(/[^\d.]/g, ''));
    expect(parseFloat(content.style.width) * scale).toBeCloseTo(parseFloat(tile.style.width), 3);
  });

  it('renders each widget tree inside its own tile', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0), widget('b', 1, 0)],
      id => tree(id === 'a' ? 'first' : 'second'));

    const tiles = Array.from(container.querySelectorAll('.deck-grid-tile')) as HTMLElement[];

    expect(tiles[0].querySelector('.widget-text')!.textContent).toBe('first');
    expect(tiles[1].querySelector('.widget-text')!.textContent).toBe('second');
  });

  it('leaves a tile empty when no tree has arrived for it yet', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => undefined);

    expect(container.querySelector('.deck-grid-tile')!.querySelector('.widget-text')).toBeNull();
  });

  it('reflects a widget that moved on the next update', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => undefined);
    const before = (container.querySelector('.deck-grid-tile') as HTMLElement).style.left;

    handle.update([widget('a', 3, 0)], () => undefined);
    const after = (container.querySelector('.deck-grid-tile') as HTMLElement).style.left;

    expect(after).not.toBe(before);
  });

  it('re-solves for a container that changed size', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => undefined);
    const before = parseFloat((container.querySelector('.deck-grid-tile') as HTMLElement).style.width);

    // Both axes, because a cell is fitted to whichever constrains it - widening a container whose
    // height already limits the cell is correctly a no-op.
    Object.defineProperty(container, 'clientWidth', { value: 1600, configurable: true });
    Object.defineProperty(container, 'clientHeight', { value: 960, configurable: true });
    handle.resize();
    const after = parseFloat((container.querySelector('.deck-grid-tile') as HTMLElement).style.width);

    expect(after).toBeGreaterThan(before);
  });

  it('draws a tile at the size its widget is now, not the size it was mounted at', () => {
    const sized = (): UiNode =>
      ({ id: 'sized', type: 'ui.text', properties: { text: 'x', size: { basis: 0.5 } } }) as UiNode;
    const fontSize = () =>
      (container.querySelector('.widget-text') as HTMLElement).style.fontSize;

    const grown = mount();
    grown.update([widget('a', 0, 0)], () => sized());
    const atOneCell = fontSize();
    grown.update([widget('a', 0, 0, 2, 2)], () => sized());
    const afterGrowing = fontSize();
    grown.destroy();

    // The same widget, two cells from the start. This is what a reload produces, and it is the size
    // the reader is meant to see either way.
    const settled = mount();
    settled.update([widget('a', 0, 0, 2, 2)], () => sized());
    const mountedAtTwoCells = fontSize();
    settled.destroy();

    expect(afterGrowing).toBe(mountedAtTwoCells);
    expect(afterGrowing).not.toBe(atOneCell);
  });

  it('leaves nothing behind after destroy', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => tree('x'));

    handle.destroy();

    expect(container.querySelector('.deck-grid')).toBeNull();
  });

  it('says which widget an interaction came from', () => {
    const seen: Array<{ widgetId: string; name: string }> = [];
    const handle = renderWidgetGrid(container, {
      host,
      geometry: { cols: 5, rows: 3 },
      onWidgetEvent: (widgetId, _node, name) => seen.push({ widgetId, name }),
    });
    handle.update([widget('a', 0, 0)], () => pressable());

    press(container.querySelector('.widget-button') as HTMLElement);

    // The renderer knows the node; only the grid knows the widget it sits in.
    expect(seen).toEqual([{ widgetId: 'a', name: 'press' }]);
  });

  it('falls back to the shared host when no widget-aware handler was given', () => {
    const seen: string[] = [];
    const handle = renderWidgetGrid(container, {
      host: { ...host, emit: (_node, name) => seen.push(name) },
      geometry: { cols: 5, rows: 3 },
    });
    handle.update([widget('a', 0, 0)], () => pressable());

    press(container.querySelector('.widget-button') as HTMLElement);

    expect(seen).toEqual(['press']);
  });

  it('hands each tile the hour cycle the shared host prefers', () => {
    const handle = renderWidgetGrid(container, {
      host: { ...host, now: () => Date.parse('2026-01-02T03:04:05Z'), hourCycle: () => 'h12' },
      geometry: { cols: 5, rows: 3 },
    });
    handle.update([widget('a', 0, 0)], () => ({
      id: 'clock', type: 'macrodeck.dynamic-text', properties: { format: 'time', value: { $time: { zone: 'UTC' } } },
    }) as UiNode);

    expect(container.querySelector('.widget-dynamic-text')!.textContent).toMatch(/^3:04\s?am$/i);
  });

  it('keeps a tile across updates instead of building it again', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => tree('before'));
    const tile = container.querySelector('.deck-grid-tile');
    const text = container.querySelector('.widget-text');

    handle.update([widget('a', 0, 0)], () => tree('after'));

    // An update arrives for every tree the host pushes - once a second for a clock. Rebuilding would
    // restart every animation and re-decode every cover, several times a second, across the deck.
    expect(container.querySelector('.deck-grid-tile')).toBe(tile);
    expect(container.querySelector('.widget-text')).toBe(text);
  });

  it('gives every tile an opaque face of its own', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => tree('x'));

    // Without it a widget is drawn straight onto the deck background - see #824's regression.
    expect(container.querySelector('.deck-grid-tile-surface')).not.toBeNull();
  });

  it('runs its own press lifecycle for a tree that claims no gesture', () => {
    const fired: string[] = [];
    const handle = renderWidgetGrid(container, {
      host,
      geometry: { cols: 5, rows: 3 },
      onWidgetTrigger: (widgetId, triggerType) => fired.push(`${widgetId}:${triggerType}`),
    });
    handle.update([widget('a', 0, 0)], () => tree('clock'));

    press(container.querySelector('.deck-grid-tile-surface') as HTMLElement);

    expect(fired).toEqual(['a:onTouchStart', 'a:onTouchEnd', 'a:onShortPress']);
  });

  it('leaves the press to a tree that claims the gesture itself', () => {
    const fired: string[] = [];
    const handle = renderWidgetGrid(container, {
      host,
      geometry: { cols: 5, rows: 3 },
      onWidgetTrigger: (widgetId, triggerType) => fired.push(`${widgetId}:${triggerType}`),
    });
    handle.update([widget('a', 0, 0)], () => pressable());

    press(container.querySelector('.deck-grid-tile-surface') as HTMLElement);

    // One physical press must not run two lifecycles - the widget's flows would fire twice.
    expect(fired).toEqual([]);
  });

  it('scales the tile while the tree it holds reports itself pressed', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => pressable());
    const tile = container.querySelector('.deck-grid-tile') as HTMLElement;

    (container.querySelector('.widget-button') as HTMLElement).dispatchEvent(pointer('pointerdown'));

    expect(tile.classList.contains('deck-grid-tile-pressed')).toBeTrue();
  });

  it('releases a press the pointer dragged out of, whatever pointer the leave carries', () => {
    jasmine.clock().install();
    jasmine.clock().mockDate();
    try {
      const handle = mount();
      handle.update([widget('a', 0, 0)], () => tree('clock'));
      const tile = container.querySelector('.deck-grid-tile') as HTMLElement;
      const surface = container.querySelector('.deck-grid-tile-surface') as HTMLElement;

      surface.dispatchEvent(pointer('pointerdown', 1));
      expect(tile.classList.contains('deck-grid-tile-pressed')).toBeTrue();

      // A different id than the press started with: filtering on it strands the tile scaled and
      // tinted with no way back.
      surface.dispatchEvent(pointer('pointerleave', 7));
      jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);

      expect(tile.classList.contains('deck-grid-tile-pressed')).toBeFalse();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('holds the pressed look for long enough to be seen', () => {
    jasmine.clock().install();
    jasmine.clock().mockDate();
    try {
      const handle = mount();
      handle.update([widget('a', 0, 0)], () => tree('clock'));
      const tile = container.querySelector('.deck-grid-tile') as HTMLElement;
      const surface = container.querySelector('.deck-grid-tile-surface') as HTMLElement;

      // A tap can be shorter than a frame, which would otherwise leave the press unpainted (#454).
      surface.dispatchEvent(pointer('pointerdown'));
      surface.dispatchEvent(pointer('pointerup'));

      expect(tile.classList.contains('deck-grid-tile-pressed')).toBeTrue();
      jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);
      expect(tile.classList.contains('deck-grid-tile-pressed')).toBeFalse();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('re-solves for a folder with a grid of its own', () => {
    const handle = mount();
    handle.update([widget('a', 0, 0)], () => undefined);
    const before = container.querySelectorAll('.deck-grid-cell').length;

    handle.configure({ cols: 4, rows: 2 }, 22);
    handle.update([widget('a', 0, 0)], () => undefined);

    expect(before).toBe(14);
    expect(container.querySelectorAll('.deck-grid-cell').length).toBe(7);
  });

  describe('updateWidget', () => {
    it('repaints one widget and leaves another tile\'s DOM untouched', () => {
      const trees: { [id: string]: UiNode } = { a: tree('A1'), b: tree('B1') };
      const handle = mount();
      handle.update([widget('a', 0, 0), widget('b', 1, 0)], id => trees[id]);

      const bTile = container.querySelector('[data-widget-id="b"]') as HTMLElement;
      const bRoot = bTile.querySelector('[data-node-id]');

      trees['a'] = tree('A2');
      trees['b'] = tree('B2');
      handle.updateWidget('a');

      const aTile = container.querySelector('[data-widget-id="a"]') as HTMLElement;
      expect(aTile.querySelector('.widget-text')!.textContent).toBe('A2');
      expect(bTile.querySelector('.widget-text')!.textContent).toBe('B1');
      expect(container.querySelector('[data-widget-id="b"]')).toBe(bTile);
      expect(bTile.querySelector('[data-node-id]')).toBe(bRoot);
    });

    it('touches no attribute or child of the tile it leaves alone', async () => {
      const trees: { [id: string]: UiNode } = { a: tree('A1'), b: tree('B1') };
      const handle = mount();
      handle.update([widget('a', 0, 0), widget('b', 1, 0)], id => trees[id]);
      const bTile = container.querySelector('[data-widget-id="b"]') as HTMLElement;

      const records: MutationRecord[] = [];
      const observer = new MutationObserver(mutations => { records.push(...mutations); });
      observer.observe(bTile, { subtree: true, attributes: true, childList: true, characterData: true });

      trees['a'] = tree('A2');
      trees['b'] = tree('B2');
      handle.updateWidget('a');

      for (let turn = 0; turn < 6; turn++) await Promise.resolve();
      observer.disconnect();

      expect(records).toEqual([]);
    });

    it('answers false for an id with no tile, and changes neither tile', () => {
      const trees: { [id: string]: UiNode } = { a: tree('A1'), b: tree('B1') };
      const handle = mount();
      handle.update([widget('a', 0, 0), widget('b', 1, 0)], id => trees[id]);
      const aTile = container.querySelector('[data-widget-id="a"]');
      const bTile = container.querySelector('[data-widget-id="b"]');
      const aText = aTile!.querySelector('.widget-text')!.textContent;
      const bText = bTile!.querySelector('.widget-text')!.textContent;

      expect(handle.updateWidget('nope')).toBeFalse();

      expect(container.querySelector('[data-widget-id="a"]')).toBe(aTile);
      expect(container.querySelector('[data-widget-id="b"]')).toBe(bTile);
      expect(aTile!.querySelector('.widget-text')!.textContent).toBe(aText);
      expect(bTile!.querySelector('.widget-text')!.textContent).toBe(bText);
    });

    it('answers false before any update() call, without throwing', () => {
      const handle = mount();
      expect(() => expect(handle.updateWidget('a')).toBeFalse()).not.toThrow();
    });

    it('answers true and unmounts the tree when the lookup now has none for it', () => {
      const trees: { [id: string]: UiNode | undefined } = { a: tree('A1') };
      const handle = mount();
      handle.update([widget('a', 0, 0)], id => trees[id]);
      const aTile = container.querySelector('[data-widget-id="a"]') as HTMLElement;
      expect(aTile.querySelector('.widget-text')).not.toBeNull();

      trees['a'] = undefined;

      expect(handle.updateWidget('a')).toBeTrue();
      expect(aTile.querySelector('.widget-text')).toBeNull();
    });

    it('answers true and remounts when the lookup now returns a different type', () => {
      const trees: { [id: string]: UiNode } = { a: tree('A1') };
      const handle = mount();
      handle.update([widget('a', 0, 0)], id => trees[id]);
      const aTile = container.querySelector('[data-widget-id="a"]') as HTMLElement;
      expect(aTile.querySelector('.widget-text')).not.toBeNull();

      trees['a'] = pressable();

      expect(handle.updateWidget('a')).toBeTrue();
      expect(aTile.querySelector('.widget-text')).toBeNull();
      expect(aTile.querySelector('.widget-button')).not.toBeNull();
    });

    it('does not re-solve the deck geometry when repainting one widget', () => {
      const trees: { [id: string]: UiNode } = { a: tree('A1'), b: tree('B1') };
      const handle = mount();
      handle.update([widget('a', 0, 0), widget('b', 1, 0)], id => trees[id]);

      const aTile = container.querySelector('[data-widget-id="a"]') as HTMLElement;
      const surface = container.querySelector('.deck-grid') as HTMLElement;
      const before = {
        left: aTile.style.left, top: aTile.style.top, width: aTile.style.width, height: aTile.style.height,
        scale: surface.style.getPropertyValue('--widget-scale'),
      };
      const cellCount = container.querySelectorAll('.deck-grid-cell').length;

      trees['a'] = tree('A2');
      handle.updateWidget('a');

      expect(aTile.style.left).toBe(before.left);
      expect(aTile.style.top).toBe(before.top);
      expect(aTile.style.width).toBe(before.width);
      expect(aTile.style.height).toBe(before.height);
      expect(surface.style.getPropertyValue('--widget-scale')).toBe(before.scale);
      expect(container.querySelectorAll('.deck-grid-cell').length).toBe(cellCount);
    });
  });
});
