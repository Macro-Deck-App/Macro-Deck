import { CellDimensions, WIDGET_REFERENCE_CELL_SIZE, WIDGET_REFERENCE_GAP } from '../domain/widget.interface';

export interface GridRect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface GridPlacement extends GridRect {
  id: string;
  locked?: boolean;
}

export interface ReflowResult {
  ok: boolean;
  placements: ReadonlyMap<string, GridRect>;
}

export const DEFAULT_CELL_GAP_RATIO = WIDGET_REFERENCE_GAP / WIDGET_REFERENCE_CELL_SIZE;

export function widgetAspectRatio(w: number, h: number, gapRatio = DEFAULT_CELL_GAP_RATIO): number {
  const cells = (span: number) => {
    const count = Math.max(1, Math.floor(span || 1));
    return count + (count - 1) * Math.max(0, gapRatio);
  };
  return cells(w) / cells(h);
}

export function computeCellDimensions(
  availableWidth: number,
  availableHeight: number,
  cols: number,
  rows: number,
  gapReference: number = WIDGET_REFERENCE_GAP,
): CellDimensions {
  const ratio = gapReference / WIDGET_REFERENCE_CELL_SIZE;
  const cellFromWidth = availableWidth / (cols + (cols - 1) * ratio + 2 * ratio);
  const cellFromHeight = availableHeight / (rows + (rows - 1) * ratio + 2 * ratio);
  const cellSize = Math.max(0, Math.min(cellFromWidth, cellFromHeight));
  return {
    cellWidth: cellSize,
    cellHeight: cellSize,
    gap: cellSize * ratio,
    padding: cellSize * ratio,
  };
}

export function cellFromPoint(
  offsetX: number,
  offsetY: number,
  cell: CellDimensions,
  cols: number,
  rows: number,
): { x: number; y: number } | null {
  const column = axisIndex(offsetX, cell.padding, cell.cellWidth, cell.gap, cols);
  const row = axisIndex(offsetY, cell.padding, cell.cellHeight, cell.gap, rows);
  return column === null || row === null ? null : { x: column, y: row };
}

function axisIndex(offset: number, padding: number, size: number, gap: number, count: number): number | null {
  const stride = size + gap;
  if (stride <= 0) {
    return null;
  }

  const local = offset - padding;
  if (local < 0) {
    return null;
  }

  const index = Math.floor(local / stride);
  return index >= count || local - index * stride > size ? null : index;
}

export function rectsOverlap(a: GridRect, b: GridRect): boolean {
  return a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
}

export function rectsEqual(a: GridRect, b: GridRect): boolean {
  return a.x === b.x && a.y === b.y && a.w === b.w && a.h === b.h;
}

export function clampRectToGrid(rect: GridRect, cols: number, rows: number): GridRect {
  const x = Math.max(0, Math.min(rect.x, cols - rect.w));
  const y = Math.max(0, Math.min(rect.y, rows - rect.h));
  return { ...rect, x, y };
}

export function clampSizeToGrid(rect: GridRect, cols: number, rows: number): GridRect {
  const w = Math.max(1, Math.min(rect.w, cols - rect.x));
  const h = Math.max(1, Math.min(rect.h, rows - rect.y));
  return { ...rect, w, h };
}

export function computePasteRect(
  anchorX: number,
  anchorY: number,
  wantW: number,
  wantH: number,
  cols: number,
  rows: number,
  occupied: readonly GridRect[],
): GridRect | null {
  if (anchorX < 0 || anchorY < 0 || anchorX >= cols || anchorY >= rows) {
    return null;
  }

  const maxW = Math.min(Math.max(1, Math.round(wantW)), cols - anchorX);
  const maxH = Math.min(Math.max(1, Math.round(wantH)), rows - anchorY);

  const cellFree = (cx: number, cy: number): boolean =>
    !occupied.some(r => cx >= r.x && cx < r.x + r.w && cy >= r.y && cy < r.y + r.h);

  if (!cellFree(anchorX, anchorY)) {
    return null;
  }

  let best: GridRect = { x: anchorX, y: anchorY, w: 1, h: 1 };
  let bestArea = 1;
  let widthLimit = maxW;

  for (let h = 1; h <= maxH; h++) {
    let run = 0;
    while (run < widthLimit && cellFree(anchorX + run, anchorY + h - 1)) {
      run++;
    }
    widthLimit = Math.min(widthLimit, run);
    if (widthLimit < 1) {
      break;
    }

    const area = widthLimit * h;
    const shrink = (maxW - widthLimit) + (maxH - h);
    const bestShrink = (maxW - best.w) + (maxH - best.h);
    if (area > bestArea || (area === bestArea && shrink < bestShrink)) {
      best = { x: anchorX, y: anchorY, w: widthLimit, h };
      bestArea = area;
    }
  }

  return best;
}

export function canPlaceGroup(
  rects: readonly GridRect[],
  cols: number,
  rows: number,
  occupied: readonly GridRect[],
): boolean {
  return rects.every(rect => {
    if (rect.x < 0 || rect.y < 0 || rect.x + rect.w > cols || rect.y + rect.h > rows) {
      return false;
    }
    return !occupied.some(other => rectsOverlap(rect, other));
  });
}

export function snapDrag(
  initial: GridRect,
  dxPx: number,
  dyPx: number,
  cell: CellDimensions,
  cols: number,
  rows: number,
): GridRect {
  const dxGrid = Math.round(dxPx / (cell.cellWidth + cell.gap));
  const dyGrid = Math.round(dyPx / (cell.cellHeight + cell.gap));
  return clampRectToGrid({ ...initial, x: initial.x + dxGrid, y: initial.y + dyGrid }, cols, rows);
}

export function snapGroupDelta(
  bounds: GridRect,
  dxPx: number,
  dyPx: number,
  cell: CellDimensions,
  cols: number,
  rows: number,
): { dx: number; dy: number } {
  const dxGrid = Math.round(dxPx / (cell.cellWidth + cell.gap));
  const dyGrid = Math.round(dyPx / (cell.cellHeight + cell.gap));
  const dx = (Math.max(-bounds.x, Math.min(dxGrid, cols - bounds.w - bounds.x)) || 0);
  const dy = (Math.max(-bounds.y, Math.min(dyGrid, rows - bounds.h - bounds.y)) || 0);
  return { dx, dy };
}

export function validateGroupMove(
  layout: readonly GridPlacement[],
  memberIds: ReadonlySet<string>,
  dx: number,
  dy: number,
  cols: number,
  rows: number,
): ReadonlyMap<string, GridRect> | null {
  const obstacles = layout.filter(widget => !memberIds.has(widget.id));
  const placements = new Map<string, GridRect>();

  for (const widget of layout) {
    if (!memberIds.has(widget.id)) continue;

    const rect: GridRect = { x: widget.x + dx, y: widget.y + dy, w: widget.w, h: widget.h };
    if (rect.x < 0 || rect.y < 0 || rect.x + rect.w > cols || rect.y + rect.h > rows) {
      return null;
    }
    if (obstacles.some(obstacle => rectsOverlap(rect, obstacle))) {
      return null;
    }
    placements.set(widget.id, rect);
  }

  return placements;
}

export function snapResize(
  initial: GridRect,
  dxPx: number,
  dyPx: number,
  cell: CellDimensions,
  cols: number,
  rows: number,
): GridRect {
  const dxGrid = Math.round(dxPx / (cell.cellWidth + cell.gap));
  const dyGrid = Math.round(dyPx / (cell.cellHeight + cell.gap));
  return clampSizeToGrid({ ...initial, w: initial.w + dxGrid, h: initial.h + dyGrid }, cols, rows);
}

export function reflowLayout(
  widgets: readonly GridPlacement[],
  movedId: string,
  candidate: GridRect,
  cols: number,
  rows: number,
): ReflowResult {
  const placements = new Map<string, GridRect>();
  const settled: GridRect[] = [];

  const moved = widgets.find(widget => widget.id === movedId);
  if (!moved) {
    return { ok: false, placements };
  }

  for (const widget of widgets) {
    if (!widget.locked || widget.id === movedId) continue;
    const rect = { x: widget.x, y: widget.y, w: widget.w, h: widget.h };
    placements.set(widget.id, rect);
    settled.push(rect);
  }

  const target = { x: candidate.x, y: candidate.y, w: candidate.w, h: candidate.h };
  if (settled.some(rect => rectsOverlap(rect, target))) {
    return { ok: false, placements: new Map() };
  }
  placements.set(movedId, target);
  settled.push(target);

  const others = widgets
    .filter(widget => widget.id !== movedId && !widget.locked)
    .sort((a, b) => a.y - b.y || a.x - b.x || a.id.localeCompare(b.id));

  for (const widget of others) {
    const original = { x: widget.x, y: widget.y, w: widget.w, h: widget.h };
    const spot = settled.some(rect => rectsOverlap(rect, original))
      ? findNearestFreeSpot(original, settled, cols, rows)
      : original;
    if (!spot) {
      return { ok: false, placements: new Map() };
    }
    placements.set(widget.id, spot);
    settled.push(spot);
  }

  return { ok: true, placements };
}

function findNearestFreeSpot(
  original: GridRect,
  settled: readonly GridRect[],
  cols: number,
  rows: number,
): GridRect | null {
  let best: GridRect | null = null;
  let bestDistance = Number.POSITIVE_INFINITY;

  for (let y = 0; y <= rows - original.h; y++) {
    for (let x = 0; x <= cols - original.w; x++) {
      const dx = x - original.x;
      const dy = y - original.y;
      const distance = dx * dx + dy * dy;
      if (distance >= bestDistance) {
        continue;
      }
      const rect = { ...original, x, y };
      if (!settled.some(other => rectsOverlap(other, rect))) {
        best = rect;
        bestDistance = distance;
      }
    }
  }

  return best;
}
