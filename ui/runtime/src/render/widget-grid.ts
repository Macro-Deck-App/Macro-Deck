import {
  ActionButtonTriggerType,
  GridWidget,
  WIDGET_REFERENCE_BORDER_RADIUS,
} from '../domain/widget.interface';
import { GridGeometry, GridMetrics } from '../grid/grid-metrics';
import { UiNode } from '../ui-framework/ui-node.interface';
import { treeClaimsGesture } from '../ui-framework/node-gestures';
import { widgetTileBorder } from '../ui-components/style';
import { PressFeedback } from './press-feedback';
import { renderUiNode, UiNodeRenderHandle } from './ui-node-renderer';
import { renderWidgetBorder, WidgetBorderHandle } from './widget-border';
import { UiRenderHost } from './ui-render-host';
import { disableContextMenu } from '../util/disable-context-menu';
import { supportsCustomProperties, widgetRadiusFallbackCss } from './custom-properties';
import { setStyle } from './dom-writes';

const LONG_PRESS_MS = 600;

let surfaceSequence = 0;

function radiusFallbackFor(surface: HTMLElement): ((radiusPx: number, scale: number) => void) | null {
  if (supportsCustomProperties()) return null;
  surfaceSequence += 1;
  const scope = `md-grid-${surfaceSequence}`;
  surface.setAttribute('data-md-grid', scope);
  const style = document.createElement('style');
  surface.appendChild(style);
  return (radiusPx, scale) => {
    const css = widgetRadiusFallbackCss(`[data-md-grid="${scope}"]`, radiusPx, scale);
    if (style.textContent !== css) style.textContent = css;
  };
}

function geometryKeyOf(geometry: Partial<GridGeometry> | undefined, radius: number | undefined): string {
  const cols = geometry && geometry.cols !== undefined ? geometry.cols : '';
  const rows = geometry && geometry.rows !== undefined ? geometry.rows : '';
  const spacing = geometry && geometry.spacing !== undefined ? geometry.spacing : '';
  return `${cols}x${rows}:${spacing}:${radius === undefined ? '' : radius}`;
}

export interface WidgetGridHandle {
  update(widgets: readonly GridWidget[], trees: TreeLookup): void;

  updateWidget(widgetId: string): boolean;

  configure(geometry: Partial<GridGeometry> | undefined, borderRadius: number | undefined): void;

  setBackground(background: string | null): void;

  setFocusedWidget(widgetId: string | null): void;

  resize(): void;
  destroy(): void;
}

export type TreeLookup = (widgetId: string) => UiNode | undefined;

export interface WidgetGridOptions {
  host: UiRenderHost;

  onWidgetEvent?(widgetId: string, node: UiNode, name: string, data?: unknown): void;

  onWidgetTrigger?(widgetId: string, triggerType: ActionButtonTriggerType): void;

  geometry?: Partial<GridGeometry>;
  borderRadius?: number;
  background?: string | null;
  focusedWidgetId?: string | null;
}

interface Tile {
  widget: GridWidget;
  element: HTMLElement;
  content: HTMLElement;
  surface: HTMLElement;
  borderHost: HTMLElement;
  overlay: HTMLElement;
  border: WidgetBorderHandle;
  mounted: UiNodeRenderHandle | null;
  mountedType: string | null;
  press: PressFeedback;
  pointerId: number | null;
  longPressTimer: ReturnType<typeof setTimeout> | null;
  longPressFired: boolean;
  treePressed: boolean;
}

export function renderWidgetGrid(
  container: HTMLElement,
  options: WidgetGridOptions,
): WidgetGridHandle {
  const metrics = new GridMetrics();
  if (options.geometry) metrics.configure(options.geometry);
  let borderRadius = options.borderRadius ?? WIDGET_REFERENCE_BORDER_RADIUS;
  let background: string | null = options.background ?? null;
  let focusedWidgetId: string | null = options.focusedWidgetId ?? null;
  let geometryKey = geometryKeyOf(options.geometry, options.borderRadius);

  const surface = document.createElement('div');
  surface.className = 'deck-grid';
  surface.style.background = background === null ? '' : background;
  disableContextMenu(surface);
  container.appendChild(surface);

  const applyRadiusFallback = radiusFallbackFor(surface);

  const cellLayer = document.createElement('div');
  cellLayer.className = 'deck-grid-cells';
  surface.appendChild(cellLayer);

  let widgets: readonly GridWidget[] = [];
  let trees: TreeLookup = () => undefined;
  const tiles: { [widgetId: string]: Tile } = {};
  const hosts: { [widgetId: string]: UiRenderHost } = {};
  const cells: HTMLElement[] = [];
  let cellKey = '';

  function hostFor(widgetId: string): UiRenderHost {
    const existing = hosts[widgetId];
    if (existing) return existing;

    const base = options.host;
    const host: UiRenderHost = {
      localization: base.localization,
      resourceUrl: resource => base.resourceUrl(resource),
      now: () => base.now(),
      culture: () => base.culture(),
      simpleRendering: () => base.simpleRendering(),
      fontFamily: faceId => base.fontFamily(faceId),
      fontReady: faceId => base.fontReady(faceId),
      uiFontKey: () => base.uiFontKey?.() ?? '',
      emit: (node, name, data) => {
        if (options.onWidgetEvent) options.onWidgetEvent(widgetId, node, name, data);
        else base.emit(node, name, data);
      },
      setPressed: (node, pressed) => {
        const tile = tiles[widgetId];
        if (tile) {
          tile.treePressed = pressed;
          paintPressed(tile);
        }
        if (base.setPressed) base.setPressed(node, pressed);
      },
      // The tile draws the root button's ring itself, beside the scaled content - see `tile.border`.
      ownsRootWidgetBorder: () => true,
    };
    hosts[widgetId] = host;
    return host;
  }

  function paintPressed(tile: Tile): void {
    const pressed = tile.press.isPressed() || tile.treePressed;
    if (pressed) tile.element.classList.add('deck-grid-tile-pressed');
    else tile.element.classList.remove('deck-grid-tile-pressed');
  }

  function trigger(tile: Tile, triggerType: ActionButtonTriggerType): void {
    if (options.onWidgetTrigger) options.onWidgetTrigger(tile.widget.id, triggerType);
  }

  function endTilePress(tile: Tile, committed: boolean): void {
    if (tile.pointerId === null) return;
    tile.pointerId = null;
    if (tile.longPressTimer !== null) {
      clearTimeout(tile.longPressTimer);
      tile.longPressTimer = null;
    }

    tile.press.release();
    trigger(tile, 'onTouchEnd');
    if (committed && !tile.longPressFired) trigger(tile, 'onShortPress');
  }

  function bindTilePress(tile: Tile): void {
    tile.surface.addEventListener('pointerdown', (event: Event) => {
      // A tree that claims the gesture answers for its own press, and running the tile's lifecycle
      // as well would fire the widget's flows twice for one physical press.
      if (treeClaimsGesture(trees(tile.widget.id))) return;

      const pointer = event as PointerEvent;
      if (tile.pointerId !== null || pointer.button > 0) return;

      event.preventDefault();
      tile.pointerId = pointer.pointerId;
      tile.longPressFired = false;
      tile.press.press();
      trigger(tile, 'onTouchStart');

      tile.longPressTimer = setTimeout(() => {
        tile.longPressTimer = null;
        tile.longPressFired = true;
        trigger(tile, 'onLongPress');
      }, LONG_PRESS_MS);
    });

    tile.surface.addEventListener('pointerup', (event: Event) => {
      const pointer = event as PointerEvent;
      if (tile.pointerId === null || pointer.pointerId !== tile.pointerId) return;
      endTilePress(tile, true);
    });

    // Deliberately not filtered by pointer id: a tile press is never multi-pointer, and the id a
    // leave carries is not always the id the press started with. Filtering it strands the tile
    // scaled and tinted with no way back - which is what a drag out of the tile used to leave behind.
    const cancel = () => endTilePress(tile, false);
    tile.surface.addEventListener('pointerleave', cancel);
    tile.surface.addEventListener('pointercancel', cancel);
  }

  function createTile(widget: GridWidget): Tile {
    const element = document.createElement('div');
    element.className = 'deck-grid-tile';
    element.setAttribute('data-widget-id', widget.id);

    // Content is authored against the reference cell and scaled like an image, so a widget keeps its
    // proportions at any deck size. The wrapper carries the scale, so anything inside that
    // multiplies by --widget-scale must not scale a second time.
    const content = document.createElement('div');
    content.className = 'deck-grid-tile-content';
    element.appendChild(content);

    const face = document.createElement('div');
    face.className = 'deck-grid-tile-surface';
    content.appendChild(face);

    const overlay = document.createElement('div');
    overlay.className = 'deck-grid-tile-press-overlay';
    face.appendChild(overlay);

    // Outside the scaled content on purpose: the ring is a fixed width at every deck size, so it
    // must not be scaled with the widget's own artwork.
    const borderHost = document.createElement('div');
    borderHost.className = 'deck-grid-tile-border';
    element.appendChild(borderHost);

    const tile: Tile = {
      widget,
      element,
      content,
      surface: face,
      borderHost,
      overlay,
      border: renderWidgetBorder(borderHost, () => options.host.now()),
      mounted: null,
      mountedType: null,
      press: null as unknown as PressFeedback,
      pointerId: null,
      longPressTimer: null,
      longPressFired: false,
      treePressed: false,
    };
    tile.press = new PressFeedback(() => paintPressed(tile));

    bindTilePress(tile);
    surface.appendChild(element);
    return tile;
  }

  function destroyTile(tile: Tile): void {
    if (tile.longPressTimer !== null) clearTimeout(tile.longPressTimer);
    tile.press.dispose();
    if (tile.mounted) tile.mounted.destroy();
    tile.border.destroy();
    if (tile.element.parentNode) tile.element.parentNode.removeChild(tile.element);
  }

  function drawCells(): void {
    const taken: { [key: string]: true } = {};
    for (let index = 0; index < widgets.length; index++) {
      const widget = widgets[index];
      for (let x = widget.x; x < widget.x + widget.w; x++) {
        for (let y = widget.y; y < widget.y + widget.h; y++) taken[`${x},${y}`] = true;
      }
    }

    let key = `${metrics.width}x${metrics.height}:${borderRadius}`;
    for (const at in taken) {
      if (Object.prototype.hasOwnProperty.call(taken, at)) key += `|${at}`;
    }
    if (key === cellKey) return;
    cellKey = key;

    while (cellLayer.firstChild) cellLayer.removeChild(cellLayer.firstChild);
    cells.length = 0;

    for (let index = 0; index < metrics.cellCount; index++) {
      const rect = metrics.cellRect(index);
      if (taken[`${rect.x},${rect.y}`]) continue;

      const cell = document.createElement('div');
      cell.className = 'deck-grid-cell';
      setStyle(cell, 'left', `${metrics.left(rect)}px`);
      setStyle(cell, 'top', `${metrics.top(rect)}px`);
      setStyle(cell, 'width', `${metrics.widthOf(rect)}px`);
      setStyle(cell, 'height', `${metrics.heightOf(rect)}px`);
      cellLayer.appendChild(cell);
      cells.push(cell);
    }
  }

  function paintFocus(): void {
    for (const widgetId in tiles) {
      if (!Object.prototype.hasOwnProperty.call(tiles, widgetId)) continue;
      const focused = widgetId === focusedWidgetId;
      if (focused) tiles[widgetId].element.classList.add('deck-grid-tile-focused');
      else tiles[widgetId].element.classList.remove('deck-grid-tile-focused');
    }
  }

  function placeTile(tile: Tile): void {
    const widget = tile.widget;
    const rect = { x: widget.x, y: widget.y, w: widget.w, h: widget.h };
    const scale = metrics.contentScale;
    const width = metrics.widthOf(rect);
    const height = metrics.heightOf(rect);

    setStyle(tile.element, 'left', `${metrics.left(rect)}px`);
    setStyle(tile.element, 'top', `${metrics.top(rect)}px`);
    setStyle(tile.element, 'width', `${width}px`);
    setStyle(tile.element, 'height', `${height}px`);

    setStyle(tile.content, 'width', `${width / scale}px`);
    setStyle(tile.content, 'height', `${height / scale}px`);
    setStyle(tile.content, '--wq', `${Math.min(width, height) / scale}px`);
    setStyle(tile.content, '--widget-scale', '1');
    setStyle(tile.content, 'transform', `scale(${scale})`);
  }

  function paintTile(tile: Tile): void {
    const widget = tile.widget;
    const rect = { x: widget.x, y: widget.y, w: widget.w, h: widget.h };
    const scale = metrics.contentScale;
    const width = metrics.widthOf(rect);
    const height = metrics.heightOf(rect);

    const tree = trees(widget.id);
    tile.border.update(widgetTileBorder(widget.type, widget.data, tree ?? null));

    if (tree === undefined) {
      if (tile.mounted) {
        tile.mounted.destroy();
        tile.mounted = null;
        tile.mountedType = null;
      }
      return;
    }

    const box = { width: width / scale, height: height / scale };
    const basis = Math.min(width, height) / scale;
    if (tile.mounted === null || tile.mountedType !== tree.type) {
      if (tile.mounted) tile.mounted.destroy();
      tile.mounted = renderUiNode(tile.surface, tree, box, null, basis, hostFor(widget.id));
      tile.mountedType = tree.type;
    } else {
      // The basis travels with every update, not just the mount: a deck that is measured after its
      // trees have arrived - or resized, or re-solved for a folder's own geometry - would otherwise
      // keep drawing every length against the cell size it first mounted at. That is content at the
      // wrong scale until the page is reloaded.
      tile.mounted.update(tree, box, null, basis);
    }

    // Kept last inside the face so the tint stays over the widget's own content.
    if (tile.overlay.nextSibling !== null) tile.surface.appendChild(tile.overlay);
  }

  function draw(): void {
    setStyle(surface, 'width', `${metrics.width}px`);
    setStyle(surface, 'height', `${metrics.height}px`);

    const scale = metrics.contentScale;
    setStyle(surface, '--widget-scale', String(scale));
    // The same number under a name the tile content does not reset, so a ring drawn inside the
    // scaled content can divide its fixed pixel width back out. See `widget-border.css`.
    setStyle(surface, '--deck-scale', String(scale));
    setStyle(surface, '--widget-radius', `${borderRadius}px`);
    if (applyRadiusFallback !== null) applyRadiusFallback(borderRadius, scale);

    drawCells();

    const wanted: { [widgetId: string]: true } = {};
    for (let index = 0; index < widgets.length; index++) wanted[widgets[index].id] = true;

    for (const widgetId in tiles) {
      if (!Object.prototype.hasOwnProperty.call(tiles, widgetId)) continue;
      if (wanted[widgetId]) continue;
      destroyTile(tiles[widgetId]);
      delete tiles[widgetId];
      delete hosts[widgetId];
    }

    // Every widget on the deck, not only the ones whose tree actually changed: a language, theme,
    // font or rendering-mode change repaints through here with no widget's own tree touched at all,
    // and it is this loop that has to reach every tile for that. `placeTile`/`paintTile` are already
    // dirty-checked underneath, so a widget nothing moved for costs no write once it is redrawn.
    for (let index = 0; index < widgets.length; index++) {
      const widget = widgets[index];

      let tile = tiles[widget.id];
      if (tile === undefined) {
        tile = createTile(widget);
        tiles[widget.id] = tile;
      }
      tile.widget = widget;

      placeTile(tile);
      paintTile(tile);
    }

    paintFocus();
  }

  function resize(): void {
    if (metrics.measure(container.clientWidth, container.clientHeight)) draw();
  }

  resize();

  return {
    update(nextWidgets: readonly GridWidget[], nextTrees: TreeLookup): void {
      widgets = nextWidgets;
      trees = nextTrees;
      metrics.measure(container.clientWidth, container.clientHeight);
      draw();
    },

    updateWidget(widgetId: string): boolean {
      const tile = tiles[widgetId];
      if (tile === undefined) return false;
      paintTile(tile);
      return true;
    },

    configure(geometry: Partial<GridGeometry> | undefined, radius: number | undefined): void {
      const key = geometryKeyOf(geometry, radius);
      if (key === geometryKey) return;
      geometryKey = key;

      borderRadius = radius ?? WIDGET_REFERENCE_BORDER_RADIUS;
      metrics.configure(geometry ?? {});
      metrics.measure(container.clientWidth, container.clientHeight);
      draw();
    },

    setBackground(next: string | null): void {
      if (next === background) return;
      background = next;
      // Empty rather than a literal colour: the stylesheet's own background is the fallback, and an
      // inline one would outrank a theme switch.
      setStyle(surface, 'background', next);
    },

    setFocusedWidget(widgetId: string | null): void {
      if (widgetId === focusedWidgetId) return;
      focusedWidgetId = widgetId;
      paintFocus();
    },

    resize,

    destroy(): void {
      for (const widgetId in tiles) {
        if (Object.prototype.hasOwnProperty.call(tiles, widgetId)) destroyTile(tiles[widgetId]);
        delete tiles[widgetId];
      }
      if (surface.parentNode) surface.parentNode.removeChild(surface);
    },
  };
}
